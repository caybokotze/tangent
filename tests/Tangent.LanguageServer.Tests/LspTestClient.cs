using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace Tangent.LanguageServer.Tests;

/// <summary>
/// A deliberately small LSP client: framed JSON-RPC over two in-memory pipes, responses matched by
/// id, notifications queued. It keeps the tests free of any dependency on the server's own
/// serialisation stack, so a test failure means the wire format is wrong, not the harness.
/// </summary>
internal sealed class LspTestClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Pipe _toServer = new();
    private readonly Pipe _fromServer = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentQueue<(string Method, JsonElement Params)> _notifications = new();
    private readonly SemaphoreSlim _notificationSignal = new(0);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Stream _write;
    private readonly Stream _read;
    private readonly Task _serverTask;
    private readonly Task _readerTask;
    private int _nextId;

    /// <summary>Starts a server over in-memory pipes with the given workspace root.</summary>
    public LspTestClient(string? workspaceRoot)
    {
        _write = _toServer.Writer.AsStream();
        _read = _fromServer.Reader.AsStream();

        _serverTask = TangentLanguageServerHost.RunAsync(
            _toServer.Reader.AsStream(),
            _fromServer.Writer.AsStream(),
            workspaceRoot,
            _cancellation.Token);

        _readerTask = Task.Run(ReadLoopAsync);
    }

    /// <summary>Every notification received so far, for assertions that do not need to wait.</summary>
    public IReadOnlyCollection<(string Method, JsonElement Params)> Notifications => _notifications.ToArray();

    /// <summary>Sends a request and awaits its result.</summary>
    public async Task<JsonElement> RequestAsync(string method, object? parameters, int timeoutMilliseconds = 30000)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        await SendAsync(new { jsonrpc = "2.0", id, method, @params = parameters }).ConfigureAwait(false);

        Task winner = await Task.WhenAny(
            completion.Task,
            Task.Delay(timeoutMilliseconds, _cancellation.Token)).ConfigureAwait(false);

        if (winner != completion.Task)
        {
            throw new TimeoutException($"No response to '{method}' within {timeoutMilliseconds} ms.");
        }

        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>Sends a notification.</summary>
    public Task NotifyAsync(string method, object? parameters) =>
        SendAsync(new { jsonrpc = "2.0", method, @params = parameters });

    /// <summary>Performs the initialize / initialized handshake and returns the initialize result.</summary>
    public async Task<JsonElement> InitializeAsync(string rootPath)
    {
        JsonElement result = await RequestAsync("initialize", new
        {
            processId = Environment.ProcessId,
            rootUri = new Uri(rootPath).AbsoluteUri,
            rootPath,
            capabilities = new
            {
                textDocument = new
                {
                    synchronization = new { dynamicRegistration = false },
                    semanticTokens = new
                    {
                        dynamicRegistration = false,
                        requests = new { full = true, range = true },
                        tokenTypes = Array.Empty<string>(),
                        tokenModifiers = Array.Empty<string>(),
                        formats = new[] { "relative" },
                    },
                    completion = new { dynamicRegistration = false },
                    hover = new { dynamicRegistration = false },
                    definition = new { dynamicRegistration = false },
                    references = new { dynamicRegistration = false },
                    publishDiagnostics = new { relatedInformation = false },
                },
                workspace = new { workspaceFolders = false },
            },
            workspaceFolders = (object?)null,
        }).ConfigureAwait(false);

        await NotifyAsync("initialized", new { }).ConfigureAwait(false);
        return result;
    }

    /// <summary>Opens a document.</summary>
    public Task OpenAsync(string path, string text, string languageId = "tangent") =>
        NotifyAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = new Uri(path).AbsoluteUri,
                languageId,
                version = 1,
                text,
            },
        });

    /// <summary>Replaces the whole text of an open document.</summary>
    public Task ChangeAsync(string path, string text, int version = 2) =>
        NotifyAsync("textDocument/didChange", new
        {
            textDocument = new { uri = new Uri(path).AbsoluteUri, version },
            contentChanges = new object[] { new { text } },
        });

    /// <summary>
    /// Waits for a <c>textDocument/publishDiagnostics</c> notification for a file whose path ends
    /// with <paramref name="fileNameSuffix"/> and which satisfies <paramref name="predicate"/>.
    /// </summary>
    public async Task<JsonElement> WaitForDiagnosticsAsync(
        string fileNameSuffix,
        Func<JsonElement, bool>? predicate = null,
        int timeoutMilliseconds = 30000)
    {
        predicate ??= static _ => true;
        var seen = new List<JsonElement>();
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        while (true)
        {
            foreach ((string method, JsonElement parameters) in _notifications)
            {
                if (method != "textDocument/publishDiagnostics") continue;
                string uri = parameters.GetProperty("uri").GetString() ?? string.Empty;
                if (!uri.EndsWith(fileNameSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                JsonElement diagnostics = parameters.GetProperty("diagnostics");
                if (!predicate(diagnostics)) continue;
                return diagnostics;
            }

            seen.Clear();
            try
            {
                await _notificationSignal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"No matching publishDiagnostics for '{fileNameSuffix}' within {timeoutMilliseconds} ms.");
            }
        }
    }

    private async Task SendAsync(object message)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        byte[] header = Encoding.ASCII.GetBytes(
            "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");

        await _writeLock.WaitAsync(_cancellation.Token).ConfigureAwait(false);
        try
        {
            await _write.WriteAsync(header, _cancellation.Token).ConfigureAwait(false);
            await _write.WriteAsync(body, _cancellation.Token).ConfigureAwait(false);
            await _write.FlushAsync(_cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                int length = await ReadHeaderAsync().ConfigureAwait(false);
                if (length < 0) return;

                byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    await _read.ReadExactlyAsync(buffer.AsMemory(0, length), _cancellation.Token).ConfigureAwait(false);
                    using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
                    Dispatch(document.RootElement.Clone());
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (EndOfStreamException)
        {
            // Server closed the pipe.
        }
        catch (IOException)
        {
            // Server closed the pipe.
        }
    }

    private void Dispatch(JsonElement message)
    {
        if (message.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number &&
            !message.TryGetProperty("method", out _))
        {
            if (_pending.TryRemove(id.GetInt32(), out TaskCompletionSource<JsonElement>? completion))
            {
                if (message.TryGetProperty("error", out JsonElement error))
                {
                    completion.TrySetException(new InvalidOperationException("LSP error: " + error.ToString()));
                }
                else
                {
                    completion.TrySetResult(message.TryGetProperty("result", out JsonElement result)
                        ? result
                        : default);
                }
            }

            return;
        }

        if (message.TryGetProperty("method", out JsonElement method))
        {
            JsonElement parameters = message.TryGetProperty("params", out JsonElement p) ? p : default;
            _notifications.Enqueue((method.GetString() ?? string.Empty, parameters));
            _notificationSignal.Release();
        }
    }

    private async Task<int> ReadHeaderAsync()
    {
        var line = new StringBuilder(64);
        int length = -1;
        byte[] one = new byte[1];

        while (true)
        {
            int read = await _read.ReadAsync(one.AsMemory(0, 1), _cancellation.Token).ConfigureAwait(false);
            if (read == 0) return -1;

            char c = (char)one[0];
            if (c == '\n')
            {
                string header = line.ToString().TrimEnd('\r');
                line.Clear();
                if (header.Length == 0) return length;
                const string marker = "Content-Length:";
                if (header.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    length = int.Parse(header.AsSpan(marker.Length).Trim(), CultureInfo.InvariantCulture);
                }
            }
            else
            {
                line.Append(c);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await NotifyAsync("shutdown", null).ConfigureAwait(false);
            await NotifyAsync("exit", null).ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            // The pipe may already be closed.
        }

        await Task.WhenAny(_serverTask, Task.Delay(5000)).ConfigureAwait(false);
        await _cancellation.CancelAsync().ConfigureAwait(false);
        await _toServer.Writer.CompleteAsync().ConfigureAwait(false);
        await Task.WhenAny(_readerTask, Task.Delay(1000)).ConfigureAwait(false);
        _cancellation.Dispose();
        _notificationSignal.Dispose();
        _writeLock.Dispose();
    }
}
