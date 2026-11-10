using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.OpenType.Tests;

/// <summary>
/// Builds OpenType layout structures for tests. A node is a run of fields; Offset16/Offset32 fields point at child
/// nodes and are written relative to the start of the node that holds them, as the OpenType layout tables define.
/// Children are placed after their parent, depth first.
/// </summary>
internal sealed class Node
{
    private readonly List<(int Size, long Value, Node? Child)> _items = new List<(int, long, Node?)>();

    internal int Size => _items.Sum(i => i.Size);

    internal Node U16(params int[] values)
    {
        foreach (int v in values)
        {
            _items.Add((2, v & 0xFFFF, null));
        }

        return this;
    }

    internal Node U24(int v)
    {
        _items.Add((3, v, null));
        return this;
    }

    internal Node U32(long v)
    {
        _items.Add((4, v, null));
        return this;
    }

    internal Node TagField(string tag)
    {
        uint v = ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
        return U32(v);
    }

    internal Node O16(Node? child)
    {
        _items.Add((2, 0, child));
        return this;
    }

    internal Node O32(Node? child)
    {
        _items.Add((-4, 0, child));
        return this;
    }

    internal byte[] Build()
    {
        Dictionary<Node, int> at = new Dictionary<Node, int>(ReferenceEqualityComparer.Instance);
        List<Node> order = new List<Node>();
        int next = 0;
        Place(this);
        byte[] b = new byte[next];
        foreach (Node n in order)
        {
            int p = at[n];
            foreach ((int size, long value, Node? child) in n._items)
            {
                int width = Math.Abs(size);
                long v = value;
                if (size == -4 || (size == 2 && child is not null))
                {
                    v = child is null ? 0 : at[child] - at[n];
                }

                for (int k = width - 1; k >= 0; k--)
                {
                    b[p + k] = (byte)v;
                    v >>= 8;
                }

                p += width;
            }
        }

        return b;

        void Place(Node n)
        {
            if (at.ContainsKey(n))
            {
                return;
            }

            at[n] = next;
            next += n._items.Sum(i => Math.Abs(i.Size));
            order.Add(n);
            foreach ((int _, long _, Node? child) in n._items)
            {
                if (child is not null)
                {
                    Place(child);
                }
            }
        }
    }
}

/// <summary>Shorthands for common layout structures.</summary>
internal static class L
{
    internal static Node N() => new Node();

    internal static Node Cov1(params int[] glyphs) => N().U16(1, glyphs.Length).U16(glyphs);

    /// <summary>Coverage format 2 from (start, end) ranges; coverage indices run on across ranges.</summary>
    internal static Node Cov2(params (int Start, int End)[] ranges)
    {
        Node n = N().U16(2, ranges.Length);
        int index = 0;
        foreach ((int s, int e) in ranges)
        {
            n.U16(s, e, index);
            index += e - s + 1;
        }

        return n;
    }

    internal static Node Class1(int start, params int[] classes) => N().U16(1, start, classes.Length).U16(classes);

    internal static Node Class2(params (int Start, int End, int Class)[] ranges)
    {
        Node n = N().U16(2, ranges.Length);
        foreach ((int s, int e, int c) in ranges)
        {
            n.U16(s, e, c);
        }

        return n;
    }

    internal static Node Anchor1(int x, int y) => N().U16(1, x, y);

    internal static Node Anchor2(int x, int y, int point) => N().U16(2, x, y, point);

    internal static Node Anchor3(int x, int y, Node? xDevice, Node? yDevice) => N().U16(3, x, y).O16(xDevice).O16(yDevice);

    internal static Node Feature(params int[] lookups) => N().O16(null).U16(lookups.Length).U16(lookups);

    /// <summary>A lookup with subtables; markFilteringSet is written when the flag has bit 0x0010.</summary>
    internal static Node Lookup(int type, int flag, int markFilteringSet, params Node[] subtables)
    {
        Node n = N().U16(type, flag, subtables.Length);
        foreach (Node s in subtables)
        {
            n.O16(s);
        }

        if ((flag & 0x0010) != 0)
        {
            n.U16(markFilteringSet);
        }

        return n;
    }

    internal static Node List(params Node[] items)
    {
        Node n = N().U16(items.Length);
        foreach (Node i in items)
        {
            n.O16(i);
        }

        return n;
    }

    internal static Node LangSys(int required, params int[] features) => N().O16(null).U16(required, features.Length).U16(features);

    /// <summary>A GSUB or GPOS table: header version 1.0, or 1.1 when featureVariations is given.</summary>
    internal static Node LayoutTable(Node scripts, Node features, Node lookups, Node? featureVariations = null)
    {
        Node n = N().U16(1, featureVariations is null ? 0 : 1).O16(scripts).O16(features).O16(lookups);
        if (featureVariations is not null)
        {
            n.O32(featureVariations);
        }

        return n;
    }

    /// <summary>A minimal 'fvar' with the given number of axes (each 20 bytes, no instances).</summary>
    internal static byte[] Fvar(int axisCount)
    {
        Node n = N().U16(1, 0, 16, 2, axisCount, 20, 0, 4);
        for (int i = 0; i < axisCount; i++)
        {
            n.TagField("wght").U32(100L << 16).U32(400L << 16).U32(900L << 16).U16(0, 256);
        }

        return n.Build();
    }
}
