using System;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Tangent.Core;
using Tangent.Core.Binding;
using Tangent.Core.Text;

namespace Tangent.Analyzers;

/// <summary>
/// How Tangent finds <c>.tan</c> contract files in a compilation and turns them into a bound model.
/// The CLI uses the same helpers so that it computes the same cache key and loads the same model as
/// the IDE does.
/// </summary>
public static class ContractFiles
{
    /// <summary>The MSBuild item metadata that marks an <c>AdditionalFiles</c> entry as a contract file.</summary>
    public const string MetadataKey = "build_metadata.AdditionalFiles.TangentContract";

    /// <summary>The contract file extension.</summary>
    public const string Extension = ".tan";

    /// <summary>
    /// True when <paramref name="file"/> is a Tangent contract file: either it carries
    /// <c>TangentContract="true"</c> metadata, or its path ends with <c>.tan</c>.
    /// </summary>
    public static bool IsContractFile(AdditionalText file, AnalyzerConfigOptionsProvider options)
    {
        if (file is null) return false;

        if (options != null &&
            options.GetOptions(file).TryGetValue(MetadataKey, out string? value) &&
            value != null &&
            value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string path = file.Path;
        return path != null && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A content hash key for a set of contract files: the checksum of each file's text, ordered so the
    /// key does not depend on the order the files were handed to the compiler.
    /// </summary>
    public static string ComputeCacheKey(ImmutableArray<AdditionalText> files)
    {
        if (files.IsDefaultOrEmpty) return string.Empty;

        var parts = new string[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            SourceText? text = files[i].GetText();
            parts[i] = text is null ? files[i].Path ?? string.Empty : ToHex(text.GetChecksum());
        }

        Array.Sort(parts, StringComparer.Ordinal);

        var builder = new StringBuilder(parts.Length * 41);
        for (int i = 0; i < parts.Length; i++)
        {
            builder.Append(parts[i]).Append(';');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Loads the model for a set of contract files, parsing at most once per unique content through
    /// <see cref="ModelCache"/>. Files are ordered by path so diagnostics have stable file indices.
    /// </summary>
    public static BindResult LoadModel(ImmutableArray<AdditionalText> files)
    {
        if (files.IsDefaultOrEmpty) return new BindResult(null, ImmutableArray<Core.Diagnostics.TangentDiagnostic>.Empty);

        string key = ComputeCacheKey(files);
        if (ModelCache.TryGet(key, out BindResult cached)) return cached;

        AdditionalText[] ordered = new AdditionalText[files.Length];
        files.CopyTo(ordered);
        Array.Sort(ordered, PathComparer.Instance);

        var sources = new SourceFile[ordered.Length];
        for (int i = 0; i < ordered.Length; i++)
        {
            SourceText? text = ordered[i].GetText();
            sources[i] = new SourceFile(ordered[i].Path ?? string.Empty, text is null ? string.Empty : text.ToString());
        }

        return ModelCache.GetOrAdd(key, () => ModelLoader.Load(sources));
    }

    private static string ToHex(ImmutableArray<byte> bytes)
    {
        if (bytes.IsDefaultOrEmpty) return string.Empty;
        var builder = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            builder.Append(bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private sealed class PathComparer : System.Collections.Generic.IComparer<AdditionalText>
    {
        internal static readonly PathComparer Instance = new PathComparer();

        public int Compare(AdditionalText? x, AdditionalText? y) =>
            string.CompareOrdinal(x?.Path ?? string.Empty, y?.Path ?? string.Empty);
    }
}
