using System;
using System.Collections.Concurrent;
using Tangent.Core.Binding;

namespace Tangent.Core;

/// <summary>
/// Caches bound models by a caller-supplied content-hash key. Roslyn creates many compilations for one
/// solution, so the analyzer and the CLI must parse a given <c>.tangent</c> folder once per unique content,
/// not once per compilation.
/// </summary>
public static class ModelCache
{
    private static readonly ConcurrentDictionary<string, BindResult> Entries =
        new ConcurrentDictionary<string, BindResult>(StringComparer.Ordinal);

    /// <summary>The number of cached models.</summary>
    public static int Count => Entries.Count;

    /// <summary>Returns the cached model for <paramref name="key"/>, invoking <paramref name="factory"/> on a miss.</summary>
    public static BindResult GetOrAdd(string key, Func<BindResult> factory)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        if (Entries.TryGetValue(key, out BindResult? existing)) return existing;
        BindResult created = factory();
        return Entries.GetOrAdd(key, created);
    }

    /// <summary>Looks up a cached model without creating one.</summary>
    public static bool TryGet(string key, out BindResult result)
    {
        if (key is null)
        {
            result = null!;
            return false;
        }

        return Entries.TryGetValue(key, out result!);
    }

    /// <summary>Drops every cached model.</summary>
    public static void Clear() => Entries.Clear();
}
