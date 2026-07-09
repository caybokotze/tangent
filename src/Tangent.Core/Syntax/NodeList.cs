using System;
using Tangent.Core.Text;

namespace Tangent.Core.Syntax;

/// <summary>One AST node. Nodes live in a flat array and refer to each other by index.</summary>
public readonly struct Node
{
    /// <summary>What this node is.</summary>
    public NodeKind Kind { get; }

    /// <summary>The source range the node covers.</summary>
    public SourceSpan Span { get; }

    /// <summary>Index of the first child, or -1.</summary>
    public int FirstChild { get; }

    /// <summary>Index of the next sibling, or -1.</summary>
    public int NextSibling { get; }

    /// <summary>Kind-specific payload; see <see cref="NodeKind"/>.</summary>
    public int Payload { get; }

    /// <summary>Second kind-specific payload; see <see cref="NodeKind"/>.</summary>
    public int Payload2 { get; }

    /// <summary>Creates a node.</summary>
    public Node(NodeKind kind, SourceSpan span, int firstChild, int nextSibling, int payload, int payload2)
    {
        Kind = kind;
        Span = span;
        FirstChild = firstChild;
        NextSibling = nextSibling;
        Payload = payload;
        Payload2 = payload2;
    }

    /// <summary>Returns a copy with a different first child.</summary>
    public Node WithFirstChild(int value) => new Node(Kind, Span, value, NextSibling, Payload, Payload2);

    /// <summary>Returns a copy with a different next sibling.</summary>
    public Node WithNextSibling(int value) => new Node(Kind, Span, FirstChild, value, Payload, Payload2);

    /// <summary>Returns a copy with a different span.</summary>
    public Node WithSpan(SourceSpan value) => new Node(Kind, value, FirstChild, NextSibling, Payload, Payload2);

    /// <summary>Returns a copy with different payloads.</summary>
    public Node WithPayloads(int payload, int payload2) => new Node(Kind, Span, FirstChild, NextSibling, payload, payload2);
}

/// <summary>A growable, index-addressed store of AST nodes.</summary>
public sealed class NodeList
{
    private Node[] _nodes;
    private int[] _lastChild;
    private int _count;

    /// <summary>Creates an empty node list.</summary>
    public NodeList(int capacity = 64)
    {
        if (capacity < 4) capacity = 4;
        _nodes = new Node[capacity];
        _lastChild = new int[capacity];
        _count = 0;
    }

    /// <summary>The number of nodes.</summary>
    public int Count => _count;

    /// <summary>Reads a node by index.</summary>
    public ref readonly Node this[int index] => ref _nodes[index];

    /// <summary>Appends a node and returns its index.</summary>
    public int Add(NodeKind kind, SourceSpan span, int payload = 0, int payload2 = 0)
    {
        if (_count == _nodes.Length)
        {
            Array.Resize(ref _nodes, _nodes.Length * 2);
            Array.Resize(ref _lastChild, _lastChild.Length * 2);
        }

        _nodes[_count] = new Node(kind, span, -1, -1, payload, payload2);
        _lastChild[_count] = -1;
        return _count++;
    }

    /// <summary>Appends <paramref name="child"/> to the child list of <paramref name="parent"/>.</summary>
    public void AddChild(int parent, int child)
    {
        if (parent < 0 || child < 0) return;
        int last = _lastChild[parent];
        if (last < 0)
        {
            _nodes[parent] = _nodes[parent].WithFirstChild(child);
        }
        else
        {
            _nodes[last] = _nodes[last].WithNextSibling(child);
        }

        _lastChild[parent] = child;
    }

    /// <summary>Replaces a node's span.</summary>
    public void SetSpan(int index, SourceSpan span) => _nodes[index] = _nodes[index].WithSpan(span);

    /// <summary>Replaces a node's payloads.</summary>
    public void SetPayloads(int index, int payload, int payload2) =>
        _nodes[index] = _nodes[index].WithPayloads(payload, payload2);

    /// <summary>Index of the first child of <paramref name="index"/>, or -1.</summary>
    public int FirstChild(int index) => index < 0 ? -1 : _nodes[index].FirstChild;

    /// <summary>Index of the next sibling of <paramref name="index"/>, or -1.</summary>
    public int NextSibling(int index) => index < 0 ? -1 : _nodes[index].NextSibling;

    /// <summary>The number of children of <paramref name="index"/>.</summary>
    public int ChildCount(int index)
    {
        int n = 0;
        for (int c = FirstChild(index); c >= 0; c = NextSibling(c)) n++;
        return n;
    }

    /// <summary>The child at <paramref name="ordinal"/>, or -1.</summary>
    public int ChildAt(int index, int ordinal)
    {
        int c = FirstChild(index);
        while (c >= 0 && ordinal > 0)
        {
            c = NextSibling(c);
            ordinal--;
        }

        return c;
    }

    /// <summary>Enumerates the children of a node without allocating.</summary>
    /// <param name="index">The parent node index.</param>
    public ChildEnumerator Children(int index) => new ChildEnumerator(this, index);

    /// <summary>A struct enumerator over child node indices.</summary>
    public struct ChildEnumerator
    {
        private readonly NodeList _list;
        private int _next;

        internal ChildEnumerator(NodeList list, int parent)
        {
            _list = list;
            _next = list.FirstChild(parent);
            Current = -1;
        }

        /// <summary>The current child index.</summary>
        public int Current { get; private set; }

        /// <summary>Advances to the next child.</summary>
        public bool MoveNext()
        {
            if (_next < 0) return false;
            Current = _next;
            _next = _list.NextSibling(_next);
            return true;
        }

        /// <summary>Returns this enumerator, so it can be used in <c>foreach</c>.</summary>
        public ChildEnumerator GetEnumerator() => this;
    }
}
