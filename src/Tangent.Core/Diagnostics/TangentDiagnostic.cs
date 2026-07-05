using System;
using System.Globalization;
using Tangent.Core.Text;

namespace Tangent.Core.Diagnostics;

/// <summary>One reported problem, in a <c>.tan</c> file or in C# source.</summary>
public readonly struct TangentDiagnostic : IEquatable<TangentDiagnostic>
{
    /// <summary>The diagnostic id, for example <c>TNG1010</c>.</summary>
    public string Code { get; }

    /// <summary>The severity this instance was reported with.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>The fully formatted message.</summary>
    public string Message { get; }

    /// <summary>Where the problem is.</summary>
    public SourceSpan Span { get; }

    /// <summary>The path of the file <see cref="Span"/> refers to, when known.</summary>
    public string? FilePath { get; }

    /// <summary>Creates a diagnostic.</summary>
    public TangentDiagnostic(string code, DiagnosticSeverity severity, string message, SourceSpan span, string? filePath)
    {
        Code = code;
        Severity = severity;
        Message = message;
        Span = span;
        FilePath = filePath;
    }

    /// <summary>Formats the diagnostic using the descriptor's default severity.</summary>
    public static TangentDiagnostic Create(DiagnosticDescriptor descriptor, SourceSpan span, string? filePath, params object?[] arguments) =>
        new TangentDiagnostic(descriptor.Id, descriptor.DefaultSeverity, descriptor.Format(arguments), span, filePath);

    /// <summary>Formats the diagnostic with an explicit severity.</summary>
    public static TangentDiagnostic Create(DiagnosticDescriptor descriptor, DiagnosticSeverity severity, SourceSpan span, string? filePath, params object?[] arguments) =>
        new TangentDiagnostic(descriptor.Id, severity, descriptor.Format(arguments), span, filePath);

    /// <summary>True when this diagnostic is an error.</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;

    /// <inheritdoc/>
    public bool Equals(TangentDiagnostic other) =>
        string.Equals(Code, other.Code, StringComparison.Ordinal) &&
        Severity == other.Severity &&
        string.Equals(Message, other.Message, StringComparison.Ordinal) &&
        Span.Equals(other.Span) &&
        string.Equals(FilePath, other.FilePath, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TangentDiagnostic other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Code is null ? 0 : Code.GetHashCode();
            hash = (hash * 397) ^ (Message is null ? 0 : Message.GetHashCode());
            hash = (hash * 397) ^ Span.GetHashCode();
            return hash;
        }
    }

    /// <summary>Renders the diagnostic as <c>path(line,col): severity CODE: message</c>.</summary>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}({1},{2}): {3} {4}: {5}",
        FilePath ?? "<unknown>",
        Span.Line,
        Span.Column,
        Severity.ToString().ToLowerInvariant(),
        Code,
        Message);
}
