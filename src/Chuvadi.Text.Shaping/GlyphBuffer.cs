using System;
using System.Collections;
using System.Collections.Generic;

namespace Chuvadi.Text.Shaping;

/// <summary>
/// The output of <see cref="TextShaper"/>: glyphs in visual order with their
/// clusters and positions in font units. Reuse one buffer across calls to avoid
/// allocation. A buffer is not safe to share between threads.
/// </summary>
public sealed class GlyphBuffer
{
    private int[] _glyphs;
    private int[] _clusters;
    private int[] _advances;
    private int[] _xOffsets;
    private int[] _yOffsets;

    /// <summary>Creates an empty buffer.</summary>
    /// <param name="capacity">Initial capacity in glyphs.</param>
    public GlyphBuffer(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _glyphs = new int[capacity];
        _clusters = new int[capacity];
        _advances = new int[capacity];
        _xOffsets = new int[capacity];
        _yOffsets = new int[capacity];
    }

    /// <summary>Gets the number of glyphs.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the units per em of the font the buffer was last shaped with (positions are in these units).</summary>
    public int UnitsPerEm { get; private set; }

    /// <summary>Gets the glyph indices.</summary>
    public ReadOnlySpan<int> GlyphIds => _glyphs.AsSpan(0, Count);

    /// <summary>Gets each glyph's cluster: the UTF-16 index, in the text passed to the shaper, of the first character of its cluster.</summary>
    public ReadOnlySpan<int> Clusters => _clusters.AsSpan(0, Count);

    /// <summary>Gets each glyph's horizontal advance, in font units.</summary>
    public ReadOnlySpan<int> XAdvances => _advances.AsSpan(0, Count);

    /// <summary>Gets each glyph's horizontal placement offset from its pen position, in font units.</summary>
    public ReadOnlySpan<int> XOffsets => _xOffsets.AsSpan(0, Count);

    /// <summary>Gets each glyph's vertical placement offset from its pen position (up positive), in font units.</summary>
    public ReadOnlySpan<int> YOffsets => _yOffsets.AsSpan(0, Count);

    /// <summary>Gets one glyph.</summary>
    /// <param name="index">The glyph position, 0..Count-1.</param>
    /// <returns>The glyph.</returns>
    public PositionedGlyph this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new PositionedGlyph(_glyphs[index], _clusters[index], _advances[index], _xOffsets[index], _yOffsets[index]);
        }
    }

    /// <summary>Removes all glyphs.</summary>
    public void Clear() => Count = 0;

    /// <summary>Copies the glyphs into a new immutable run.</summary>
    /// <returns>The run.</returns>
    public ShapedRun ToShapedRun()
    {
        PositionedGlyph[] glyphs = new PositionedGlyph[Count];
        for (int i = 0; i < Count; i++)
        {
            glyphs[i] = this[i];
        }

        return new ShapedRun(glyphs, UnitsPerEm);
    }

    internal void Reset(int unitsPerEm, int expected)
    {
        Count = 0;
        UnitsPerEm = unitsPerEm;
        EnsureCapacity(expected);
    }

    internal void Add(int glyph, int cluster, int advance, int xOffset, int yOffset)
    {
        if (Count == _glyphs.Length)
        {
            EnsureCapacity(Math.Max(16, Count * 2));
        }

        _glyphs[Count] = glyph;
        _clusters[Count] = cluster;
        _advances[Count] = advance;
        _xOffsets[Count] = xOffset;
        _yOffsets[Count] = yOffset;
        Count++;
    }

    private void EnsureCapacity(int capacity)
    {
        if (capacity <= _glyphs.Length)
        {
            return;
        }

        Array.Resize(ref _glyphs, capacity);
        Array.Resize(ref _clusters, capacity);
        Array.Resize(ref _advances, capacity);
        Array.Resize(ref _xOffsets, capacity);
        Array.Resize(ref _yOffsets, capacity);
    }
}

/// <summary>One shaped glyph, positions in font units.</summary>
/// <param name="GlyphId">The glyph index.</param>
/// <param name="Cluster">The UTF-16 index of the first character of the glyph's cluster.</param>
/// <param name="XAdvance">The horizontal advance.</param>
/// <param name="XOffset">The horizontal placement offset.</param>
/// <param name="YOffset">The vertical placement offset, up positive.</param>
public readonly record struct PositionedGlyph(int GlyphId, int Cluster, int XAdvance, int XOffset, int YOffset);

/// <summary>An immutable shaped run.</summary>
public sealed class ShapedRun : IReadOnlyList<PositionedGlyph>
{
    private readonly PositionedGlyph[] _glyphs;

    internal ShapedRun(PositionedGlyph[] glyphs, int unitsPerEm)
    {
        _glyphs = glyphs;
        UnitsPerEm = unitsPerEm;
    }

    /// <summary>Gets the units per em of the font (positions are in these units).</summary>
    public int UnitsPerEm { get; }

    /// <summary>Gets the number of glyphs.</summary>
    public int Count => _glyphs.Length;

    /// <summary>Gets one glyph.</summary>
    /// <param name="index">The position.</param>
    /// <returns>The glyph.</returns>
    public PositionedGlyph this[int index] => _glyphs[index];

    /// <summary>Enumerates the glyphs in visual order.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<PositionedGlyph> GetEnumerator() => ((IEnumerable<PositionedGlyph>)_glyphs).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
