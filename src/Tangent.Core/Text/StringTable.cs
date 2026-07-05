using System;

namespace Tangent.Core.Text;

/// <summary>Interns identifier text taken from <see cref="ReadOnlySpan{T}"/> without allocating per lookup.</summary>
public sealed class StringTable
{
    private const int MinCapacity = 16;

    private string[] _values;
    private int[] _hashes;
    private int _count;
    private int[] _buckets; // stores index + 1; 0 means empty
    private int _mask;

    /// <summary>Creates a table sized for roughly <paramref name="capacity"/> entries.</summary>
    public StringTable(int capacity = 256)
    {
        int size = MinCapacity;
        while (size < capacity) size <<= 1;
        _values = new string[size];
        _hashes = new int[size];
        _buckets = new int[size * 2];
        _mask = _buckets.Length - 1;
        _count = 0;
    }

    /// <summary>The number of distinct strings held.</summary>
    public int Count => _count;

    /// <summary>Interns <paramref name="text"/>, allocating a string only the first time it is seen.</summary>
    public StringId Intern(ReadOnlySpan<char> text)
    {
        int hash = ComputeHash(text);
        int slot = hash & _mask;
        while (true)
        {
            int entry = _buckets[slot];
            if (entry == 0) break;
            int index = entry - 1;
            if (_hashes[index] == hash && text.SequenceEqual(_values[index].AsSpan()))
                return new StringId(index);
            slot = (slot + 1) & _mask;
        }

        if (_count == _values.Length) GrowValues();
        int newIndex = _count++;
        _values[newIndex] = text.ToString();
        _hashes[newIndex] = hash;

        if (_count * 4 >= _buckets.Length * 3)
        {
            Rehash();
        }
        else
        {
            _buckets[slot] = newIndex + 1;
        }

        return new StringId(newIndex);
    }

    /// <summary>Interns a string.</summary>
    public StringId Intern(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        return Intern(text.AsSpan());
    }

    /// <summary>Finds an already-interned string without adding it.</summary>
    public bool TryFind(ReadOnlySpan<char> text, out StringId id)
    {
        int hash = ComputeHash(text);
        int slot = hash & _mask;
        while (true)
        {
            int entry = _buckets[slot];
            if (entry == 0)
            {
                id = StringId.Invalid;
                return false;
            }

            int index = entry - 1;
            if (_hashes[index] == hash && text.SequenceEqual(_values[index].AsSpan()))
            {
                id = new StringId(index);
                return true;
            }

            slot = (slot + 1) & _mask;
        }
    }

    /// <summary>Returns the string behind a handle, or the empty string for <see cref="StringId.Invalid"/>.</summary>
    public string GetString(StringId id)
    {
        if (!id.IsValid || id.Value >= _count) return string.Empty;
        return _values[id.Value];
    }

    /// <summary>Returns the characters behind a handle without allocating.</summary>
    public ReadOnlySpan<char> GetSpan(StringId id)
    {
        if (!id.IsValid || id.Value >= _count) return ReadOnlySpan<char>.Empty;
        return _values[id.Value].AsSpan();
    }

    private void GrowValues()
    {
        int newSize = _values.Length * 2;
        Array.Resize(ref _values, newSize);
        Array.Resize(ref _hashes, newSize);
    }

    private void Rehash()
    {
        int newSize = _buckets.Length * 2;
        _buckets = new int[newSize];
        _mask = newSize - 1;
        for (int i = 0; i < _count; i++)
        {
            int slot = _hashes[i] & _mask;
            while (_buckets[slot] != 0) slot = (slot + 1) & _mask;
            _buckets[slot] = i + 1;
        }
    }

    private static int ComputeHash(ReadOnlySpan<char> text)
    {
        unchecked
        {
            int hash = (int)2166136261;
            for (int i = 0; i < text.Length; i++)
            {
                hash = (hash ^ text[i]) * 16777619;
            }

            return hash & 0x7FFFFFFF;
        }
    }
}
