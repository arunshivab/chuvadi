using System;

namespace Chuvadi.Text.Shaping.Engine;

/// <summary>One glyph of the run being shaped (E11: one record per glyph).</summary>
internal struct GlyphInfo
{
    internal int Glyph;
    internal int Cluster;
    internal int CodePoint;
    internal int SourceIndex;
    internal ulong Mask;
    internal byte Class;
    internal bool Ignorable;
    internal int LigatureId;
    internal int Component;
    internal int XAdvance;
    internal int XOffset;
    internal int YOffset;
    internal int AttachTo;
    internal byte AttachKind;
    internal int AttachDx;
    internal int AttachDy;

    /// <summary>The width a missing space character takes from the fallback (D-354), or -1 for none.</summary>
    internal int FallbackAdvance;
}

/// <summary>A growable array of glyph records with insertion and removal.</summary>
internal sealed class GlyphRun
{
    internal const byte AttachMark = 1;
    internal const byte AttachCursive = 2;

    internal GlyphInfo[] Items = new GlyphInfo[64];

    internal int Count;

    internal ref GlyphInfo this[int index] => ref Items[index];

    internal void Clear() => Count = 0;

    internal void Add(in GlyphInfo info)
    {
        Ensure(Count + 1);
        Items[Count++] = info;
    }

    /// <summary>Opens <paramref name="count"/> slots at <paramref name="at"/>, moving the rest along.</summary>
    internal void Insert(int at, int count)
    {
        Ensure(Count + count);
        Array.Copy(Items, at, Items, at + count, Count - at);
        Count += count;
    }

    internal void RemoveAt(int at)
    {
        Array.Copy(Items, at + 1, Items, at, Count - at - 1);
        Count--;
    }

    private void Ensure(int size)
    {
        if (size > Items.Length)
        {
            Array.Resize(ref Items, Math.Max(size, Items.Length * 2));
        }
    }
}
