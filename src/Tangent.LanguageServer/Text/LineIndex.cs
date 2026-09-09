namespace Tangent.LanguageServer.Text;

/// <summary>
/// Maps between character offsets and zero-based (line, character) pairs for one document.
/// Characters are UTF-16 code units, which is the LSP default position encoding and also
/// exactly what a .NET <see cref="string"/> is indexed by, so no re-encoding is needed.
/// </summary>
internal sealed class LineIndex
{
    private readonly int[] _lineStarts;

    /// <summary>Builds the index for <paramref name="text"/>.</summary>
    public LineIndex(string text)
    {
        Text = text ?? string.Empty;
        var starts = new List<int>(Math.Max(8, Text.Length / 32)) { 0 };
        for (int i = 0; i < Text.Length; i++)
        {
            char c = Text[i];
            if (c == '\r')
            {
                if (i + 1 < Text.Length && Text[i + 1] == '\n') i++;
                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }

        _lineStarts = starts.ToArray();
    }

    /// <summary>The indexed text.</summary>
    public string Text { get; }

    /// <summary>The number of lines; always at least one.</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>The offset of the first character of a zero-based line.</summary>
    public int LineStart(int line)
    {
        if (line <= 0) return 0;
        if (line >= _lineStarts.Length) return Text.Length;
        return _lineStarts[line];
    }

    /// <summary>The offset one past the last character of a zero-based line, excluding the line break.</summary>
    public int LineEnd(int line)
    {
        int next = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] : Text.Length;
        int end = next;
        while (end > LineStart(line) && (Text[end - 1] == '\n' || Text[end - 1] == '\r')) end--;
        return end;
    }

    /// <summary>The zero-based line containing <paramref name="offset"/>.</summary>
    public int LineOf(int offset)
    {
        if (offset <= 0) return 0;
        if (offset >= Text.Length) return _lineStarts.Length - 1;

        int low = 0;
        int high = _lineStarts.Length - 1;
        while (low < high)
        {
            int mid = low + ((high - low + 1) / 2);
            if (_lineStarts[mid] <= offset) low = mid;
            else high = mid - 1;
        }

        return low;
    }

    /// <summary>The zero-based line and character of <paramref name="offset"/>.</summary>
    public (int Line, int Character) PositionOf(int offset)
    {
        int clamped = offset < 0 ? 0 : offset > Text.Length ? Text.Length : offset;
        int line = LineOf(clamped);
        return (line, clamped - LineStart(line));
    }

    /// <summary>The offset of a zero-based (line, character) pair, clamped into the document.</summary>
    public int OffsetOf(int line, int character)
    {
        if (line < 0) return 0;
        if (line >= _lineStarts.Length) return Text.Length;
        int start = _lineStarts[line];
        int end = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] : Text.Length;
        int offset = start + (character < 0 ? 0 : character);
        return offset > end ? end : offset;
    }
}
