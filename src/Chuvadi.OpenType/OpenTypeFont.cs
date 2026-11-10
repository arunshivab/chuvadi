using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.OpenType.Internal;
using Chuvadi.OpenType.Tables;

namespace Chuvadi.OpenType;

/// <summary>
/// One OpenType / TrueType font face. Tables are read in place from the font
/// bytes; nothing is copied. The object is immutable and safe to use from
/// several threads at once.
/// </summary>
/// <remarks>
/// Damaged data never throws once the font has loaded: a table whose record
/// points outside the data is treated as absent, and reads past the end of a
/// table return zero. Loading throws only when the container header, the table
/// directory, 'head' or 'maxp' is unusable.
/// </remarks>
public sealed class OpenTypeFont
{
    private static readonly Tag HeadTag = new Tag(0x68656164); // head
    private static readonly Tag MaxpTag = new Tag(0x6D617870); // maxp
    private static readonly Tag HheaTag = new Tag(0x68686561); // hhea
    private static readonly Tag HmtxTag = new Tag(0x686D7478); // hmtx
    private static readonly Tag CmapTag = new Tag(0x636D6170); // cmap
    private static readonly Tag Os2Tag = new Tag(0x4F532F32);  // OS/2
    private static readonly Tag PostTag = new Tag(0x706F7374); // post
    private static readonly Tag NameTag = new Tag(0x6E616D65); // name
    private static readonly Tag KernTag = new Tag(0x6B65726E); // kern
    private static readonly Tag GlyfTag = new Tag(0x676C7966); // glyf
    private static readonly Tag LocaTag = new Tag(0x6C6F6361); // loca
    private static readonly Tag CffTag = new Tag(0x43464620);  // 'CFF '
    private static readonly Tag Cff2Tag = new Tag(0x43464632); // CFF2
    private static readonly Tag GsubTag = new Tag(0x47535542); // GSUB
    private static readonly Tag GposTag = new Tag(0x47504F53); // GPOS
    private static readonly Tag GdefTag = new Tag(0x47444546); // GDEF
    private static readonly Tag FvarTag = new Tag(0x66766172); // fvar

    private readonly ReadOnlyMemory<byte> _data;
    private readonly TableRecord[] _tables;
    private readonly Dictionary<Tag, TableRecord> _byTag;
    private readonly int _numberOfHMetrics;

    private CharacterMap? _cmap;
    private Os2Table? _os2;
    private PostTable? _post;
    private NameTable? _name;
    private KernTable? _kern;
    private CffOutlines? _cff;
    private byte[]? _faceProgram;
    private readonly bool _fromCollection;
    private int _os2Read;
    private int _postRead;
    private int _nameRead;
    private int _kernRead;
    private Layout.LayoutTable? _gsub;
    private Layout.LayoutTable? _gpos;
    private Layout.GdefTable? _gdef;
    private int _gsubRead;
    private int _gposRead;
    private int _gdefRead;
    private FaceMetrics? _metrics;
    private int _metricsRead;

    private OpenTypeFont(ReadOnlyMemory<byte> data, int faceIndex, uint sfntVersion, TableRecord[] tables, Dictionary<Tag, TableRecord> byTag, bool fromCollection)
    {
        _data = data;
        _fromCollection = fromCollection;
        FaceIndex = faceIndex;
        SfntVersion = sfntVersion;
        _tables = tables;
        _byTag = byTag;

        ReadOnlySpan<byte> head = TableSpan(HeadTag);
        // TRACE R-OT-0006
        if (head.Length < 54)
        {
            throw new OpenTypeFormatException("The font has no usable 'head' table.");
        }

        Head = new HeadTable(head);
        if (Head.UnitsPerEm == 0)
        {
            throw new OpenTypeFormatException("The 'head' table gives unitsPerEm = 0.");
        }

        ReadOnlySpan<byte> maxp = TableSpan(MaxpTag);
        // TRACE R-OT-0007
        if (maxp.Length < 6)
        {
            throw new OpenTypeFormatException("The font has no usable 'maxp' table.");
        }

        Maxp = new MaxpTable(maxp);

        ReadOnlySpan<byte> hhea = TableSpan(HheaTag);
        if (hhea.Length >= 36)
        {
            Hhea = new HheaTable(hhea);
            _numberOfHMetrics = Hhea.NumberOfHMetrics;
        }

        // TRACE R-OT-0008
        if (sfntVersion == 0x4F54544Fu && _byTag.ContainsKey(CffTag))
        {
            OutlineFormat = OutlineFormat.Cff;
        }
        else if (sfntVersion == 0x4F54544Fu && _byTag.ContainsKey(Cff2Tag))
        {
            OutlineFormat = OutlineFormat.Cff2;
        }
        else if (_byTag.ContainsKey(GlyfTag) && _byTag.ContainsKey(LocaTag))
        {
            OutlineFormat = OutlineFormat.TrueType;
        }
        else if (_byTag.ContainsKey(CffTag))
        {
            OutlineFormat = OutlineFormat.Cff;
        }
        else if (_byTag.ContainsKey(Cff2Tag))
        {
            OutlineFormat = OutlineFormat.Cff2;
        }
        else
        {
            OutlineFormat = OutlineFormat.None;
        }
    }

    /// <summary>Gets the font data this face reads from: the bytes given (the whole collection for a face of a collection), or for WOFF and WOFF2 the decoded sfnt or collection.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Gets this face as a standalone sfnt font program, for embedding (for example in a PDF).
    /// For a plain sfnt it is <see cref="Data"/> itself, not a copy. For a face of a collection it is built
    /// on first use and kept: a new sfnt with this face's tables only, its own table directory, recomputed
    /// checksums and 'head' checkSumAdjustment. The face's 'DSIG' table is left out, because a signature no
    /// longer matches the rebuilt file (D-153).
    /// </summary>
    public ReadOnlyMemory<byte> FaceProgram
    {
        get
        {
            if (!_fromCollection)
            {
                return _data;
            }

            byte[]? p = Volatile.Read(ref _faceProgram);
            if (p is null)
            {
                p = SfntWriter.WriteFace(_data.Span, SfntVersion, _tables);
                p = Interlocked.CompareExchange(ref _faceProgram, p, null) ?? p;
            }

            return p;
        }
    }

    /// <summary>Gets the zero-based index of this face in its collection (0 for a plain sfnt).</summary>
    public int FaceIndex { get; }

    /// <summary>Gets the sfnt version of the face: 0x00010000, 'OTTO' (0x4F54544F) or 'true' (0x74727565).</summary>
    public uint SfntVersion { get; }

    /// <summary>Gets the kind of glyph outlines.</summary>
    public OutlineFormat OutlineFormat { get; }

    /// <summary>Gets whether the outlines are CFF version 1 ('CFF '). Same as <c>OutlineFormat == OutlineFormat.Cff</c>; false for CFF2.</summary>
    public bool IsCff => OutlineFormat == OutlineFormat.Cff;

    /// <summary>Gets the units per em from 'head'.</summary>
    public int UnitsPerEm => Head.UnitsPerEm;

    /// <summary>Gets the number of glyphs from 'maxp'.</summary>
    public int GlyphCount => Maxp.NumGlyphs;

    /// <summary>Gets the table directory in directory order. Records pointing outside the data are left out.</summary>
    public IReadOnlyList<TableRecord> Tables => _tables;

    /// <summary>Gets the 'head' table.</summary>
    public HeadTable Head { get; }

    /// <summary>Gets the 'maxp' table.</summary>
    public MaxpTable Maxp { get; }

    /// <summary>Gets the 'hhea' table, or null when it is missing or shorter than 36 bytes.</summary>
    public HheaTable? Hhea { get; }

    /// <summary>Gets the 'OS/2' table, or null when it is missing or shorter than 68 bytes.</summary>
    public Os2Table? Os2 => ReadOnce(ref _os2, ref _os2Read, this, static f => { ReadOnlySpan<byte> s = f.TableSpan(Os2Tag); return s.Length >= 68 ? new Os2Table(s) : null; });

    /// <summary>Gets the 'post' table, or null when it is missing or shorter than 32 bytes.</summary>
    public PostTable? Post => ReadOnce(ref _post, ref _postRead, this, static f => { ReadOnlySpan<byte> s = f.TableSpan(PostTag); return s.Length >= 32 ? new PostTable(s) : null; });

    /// <summary>Gets the 'name' table, or null when it is missing or shorter than its header.</summary>
    public NameTable? Names => ReadOnce(ref _name, ref _nameRead, this, static f => { ReadOnlySpan<byte> s = f.TableSpan(NameTag); return s.Length >= 6 ? new NameTable(s) : null; });

    /// <summary>Gets the 'kern' table, or null when it is missing or unreadable.</summary>
    public KernTable? Kern => ReadOnce(ref _kern, ref _kernRead, this, static f => { ReadOnlySpan<byte> s = f.TableSpan(KernTag); return s.Length >= 4 ? KernTable.TryRead(s) : null; });

    /// <summary>
    /// Gets the 'GSUB' table, or null when it is missing, empty or not version 1.x. Read at the default instance of a
    /// variable font (D-194).
    /// </summary>
    public Layout.LayoutTable? Gsub => ReadOnce(ref _gsub, ref _gsubRead, this, static f => f.ReadLayout(Layout.LayoutTableKind.Gsub, GsubTag));

    /// <summary>
    /// Gets the 'GPOS' table, or null when it is missing, empty or not version 1.x. Read at the default instance of a
    /// variable font (D-194).
    /// </summary>
    public Layout.LayoutTable? Gpos => ReadOnce(ref _gpos, ref _gposRead, this, static f => f.ReadLayout(Layout.LayoutTableKind.Gpos, GposTag));

    /// <summary>Gets the 'GDEF' table, or null when it is missing, empty or not version 1.x.</summary>
    public Layout.GdefTable? Gdef => ReadOnce(ref _gdef, ref _gdefRead, this, static f =>
    {
        // TRACE R-OT-0053
        ReadOnlyMemory<byte> m = f.GetTableData(GdefTag);
        return m.Length >= 12 && Internal.BigEndian.U16(m.Span, 0) == 1 ? new Layout.GdefTable(m) : null;
    });

    /// <summary>Derived names, metrics and style flags, computed on first use (D-247).</summary>
    public FaceMetrics Metrics => ReadOnce(ref _metrics, ref _metricsRead, this, static f => new FaceMetrics(f))!;

    /// <summary>The number of variation axes in the 'fvar' table; 0 for a font that is not variable.</summary>
    public int VariationAxisCount
    {
        get
        {
            ReadOnlySpan<byte> s = TableSpan(FvarTag);
            return Internal.BigEndian.U16(s, 0) == 1 ? Internal.BigEndian.U16(s, 8) : 0;
        }
    }

    /// <summary>Gets the character map. It is empty when the font has no usable 'cmap'.</summary>
    public CharacterMap CharacterMap
    {
        get
        {
            CharacterMap? m = Volatile.Read(ref _cmap);
            if (m is null)
            {
                m = new CharacterMap(GetTableData(CmapTag), GlyphCount);
                m = Interlocked.CompareExchange(ref _cmap, m, null) ?? m;
            }

            return m;
        }
    }

    /// <summary>Loads a font from bytes.</summary>
    /// <param name="data">The font file bytes (sfnt or collection).</param>
    /// <param name="faceIndex">The face to load from a collection; must be 0 for a plain sfnt.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is not a face of the data.</exception>
    /// <exception cref="OpenTypeFormatException">The data is not a usable font.</exception>
    public static OpenTypeFont Load(byte[] data, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Load(new ReadOnlyMemory<byte>(data), faceIndex);
    }

    /// <summary>Loads a font from memory. The memory must stay unchanged for the font's lifetime.</summary>
    /// <param name="data">The font file bytes (sfnt or collection).</param>
    /// <param name="faceIndex">The face to load from a collection; must be 0 for a plain sfnt.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is not a face of the data.</exception>
    /// <exception cref="OpenTypeFormatException">The data is not a usable font.</exception>
    public static OpenTypeFont Load(ReadOnlyMemory<byte> data, int faceIndex = 0) =>
        FontCollection.Load(data).GetFont(faceIndex);

    /// <summary>Loads a font from memory with load options. The memory must stay unchanged for the font's lifetime.</summary>
    /// <param name="data">The font file bytes (sfnt, collection, WOFF or WOFF2).</param>
    /// <param name="faceIndex">The face to load.</param>
    /// <param name="options">The options; null for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The font.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is not a face of the data, or the options are invalid.</exception>
    /// <exception cref="OpenTypeFormatException">The data is not a usable font.</exception>
    public static OpenTypeFont Load(ReadOnlyMemory<byte> data, int faceIndex, FontLoadOptions? options) =>
        FontCollection.Load(data, options).GetFont(faceIndex);

    /// <summary>Tries to load a font; returns false instead of throwing for unusable data or a bad face index.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <param name="faceIndex">The face to load.</param>
    /// <param name="font">The font, or null.</param>
    /// <returns>True when the font loaded.</returns>
    public static bool TryLoad(ReadOnlyMemory<byte> data, int faceIndex, out OpenTypeFont? font) =>
        TryLoad(data, faceIndex, null, out font);

    /// <summary>Tries to load a font with load options; returns false instead of throwing for unusable data or a bad face index.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <param name="faceIndex">The face to load.</param>
    /// <param name="options">The options; null for <see cref="FontLoadOptions.Default"/>.</param>
    /// <param name="font">The font, or null.</param>
    /// <returns>True when the font loaded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The options are invalid.</exception>
    public static bool TryLoad(ReadOnlyMemory<byte> data, int faceIndex, FontLoadOptions? options, out OpenTypeFont? font)
    {
        font = null;
        if (!FontCollection.TryLoad(data, (options ?? FontLoadOptions.Default).Validate(), out FontCollection? c, out _) || (uint)faceIndex >= (uint)c!.Count)
        {
            return false;
        }

        try
        {
            font = c.GetFont(faceIndex);
            return true;
        }
        catch (OpenTypeFormatException)
        {
            return false;
        }
    }

    /// <summary>Returns whether the table directory has a usable record for a tag.</summary>
    /// <param name="tag">The table tag.</param>
    /// <returns>True when the table is present.</returns>
    public bool HasTable(Tag tag) => _byTag.ContainsKey(tag);

    /// <summary>Gets the directory record for a table.</summary>
    /// <param name="tag">The table tag.</param>
    /// <param name="record">The record, or default when absent.</param>
    /// <returns>True when the table is present.</returns>
    public bool TryGetTable(Tag tag, out TableRecord record) => _byTag.TryGetValue(tag, out record);

    /// <summary>Gets a table's bytes, or empty memory when the table is absent.</summary>
    /// <param name="tag">The table tag.</param>
    /// <returns>The table bytes, a slice of <see cref="Data"/>.</returns>
    public ReadOnlyMemory<byte> GetTableData(Tag tag) =>
        _byTag.TryGetValue(tag, out TableRecord r) ? _data.Slice(r.Offset, r.Length) : ReadOnlyMemory<byte>.Empty;

    /// <summary>Gets the advance width of a glyph from 'hmtx', in font units.</summary>
    /// <param name="glyphId">The glyph index.</param>
    /// <returns>The advance width; 0 for a glyph index outside 0..GlyphCount-1 or when 'hhea'/'hmtx' are unusable.</returns>
    public int GetAdvanceWidth(int glyphId)
    {
        // TRACE R-OT-0009
        if ((uint)glyphId >= (uint)GlyphCount || _numberOfHMetrics == 0)
        {
            return 0;
        }

        int index = glyphId < _numberOfHMetrics ? glyphId : _numberOfHMetrics - 1;
        return BigEndian.U16(TableSpan(HmtxTag), index * 4);
    }

    /// <summary>Gets the left side bearing of a glyph from 'hmtx', in font units.</summary>
    /// <param name="glyphId">The glyph index.</param>
    /// <returns>The left side bearing; 0 for a glyph index outside 0..GlyphCount-1 or missing data.</returns>
    public int GetLeftSideBearing(int glyphId)
    {
        // TRACE R-OT-0009
        if ((uint)glyphId >= (uint)GlyphCount || _numberOfHMetrics == 0)
        {
            return 0;
        }

        ReadOnlySpan<byte> hmtx = TableSpan(HmtxTag);
        return glyphId < _numberOfHMetrics
            ? BigEndian.I16(hmtx, (glyphId * 4) + 2)
            : BigEndian.I16(hmtx, (_numberOfHMetrics * 4) + ((glyphId - _numberOfHMetrics) * 2));
    }

    /// <summary>
    /// Gets a glyph's bounding box in font units. For TrueType outlines this is the box stored in the glyph header;
    /// for CFF outlines it is computed from the charstring (curve extremes included).
    /// </summary>
    /// <param name="glyphId">The glyph index.</param>
    /// <param name="bounds">The bounds, or default when the glyph has no outline.</param>
    /// <returns>False when the glyph is empty, out of range, unreadable, or the outline format is not read.</returns>
    public bool TryGetGlyphBounds(int glyphId, out GlyphBounds bounds)
    {
        bounds = default;
        if ((uint)glyphId >= (uint)GlyphCount)
        {
            return false;
        }

        switch (OutlineFormat)
        {
            case OutlineFormat.TrueType:
                return GlyfBounds.TryGet(TableSpan(GlyfTag), TableSpan(LocaTag), Head.IndexToLocFormat, GlyphCount, glyphId, out bounds);
            case OutlineFormat.Cff:
                return Cff().TryGetBounds(glyphId, out bounds);
            default:
                return false;
        }
    }

    /// <summary>Gets a glyph's bounding box, or (0, 0, 0, 0) when it has none. See <see cref="TryGetGlyphBounds"/>.</summary>
    /// <param name="glyphId">The glyph index.</param>
    /// <returns>The bounds.</returns>
    public GlyphBounds GetGlyphBounds(int glyphId) => TryGetGlyphBounds(glyphId, out GlyphBounds b) ? b : default;

    internal static OpenTypeFont Create(ReadOnlyMemory<byte> data, int faceIndex, int directoryOffset, bool fromCollection)
    {
        ReadOnlySpan<byte> s = data.Span;
        // TRACE R-OT-0004
        uint sfntVersion = BigEndian.U32(s, directoryOffset);
        if (sfntVersion != 0x00010000u && sfntVersion != 0x4F54544Fu && sfntVersion != 0x74727565u)
        {
            throw new OpenTypeFormatException("The font's table directory has an unknown sfnt version.");
        }

        int numTables = BigEndian.U16(s, directoryOffset + 4);
        if (!BigEndian.Has(s, directoryOffset + 12, numTables * 16))
        {
            throw new OpenTypeFormatException("The font's table directory runs past the end of the data.");
        }

        List<TableRecord> list = new List<TableRecord>(numTables);
        Dictionary<Tag, TableRecord> byTag = new Dictionary<Tag, TableRecord>(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int r = directoryOffset + 12 + (16 * i);
            Tag tag = Tag.Read(s, r);
            uint checksum = BigEndian.U32(s, r + 4);
            uint offset = BigEndian.U32(s, r + 8);
            uint length = BigEndian.U32(s, r + 12);
            if ((ulong)offset + length > (ulong)s.Length || byTag.ContainsKey(tag))
            {
                continue;
            }

            TableRecord rec = new TableRecord(tag, checksum, (int)offset, (int)length);
            list.Add(rec);
            byTag.Add(tag, rec);
        }

        return new OpenTypeFont(data, faceIndex, sfntVersion, list.ToArray(), byTag, fromCollection);
    }

    private Layout.LayoutTable? ReadLayout(Layout.LayoutTableKind kind, Tag tag)
    {
        // TRACE R-OT-0040
        ReadOnlyMemory<byte> m = GetTableData(tag);
        return m.Length >= 10 && Internal.BigEndian.U16(m.Span, 0) == 1 ? new Layout.LayoutTable(kind, m, VariationAxisCount) : null;
    }

    internal ReadOnlySpan<byte> TableSpan(Tag tag) =>
        _byTag.TryGetValue(tag, out TableRecord r) ? _data.Span.Slice(r.Offset, r.Length) : ReadOnlySpan<byte>.Empty;

    private CffOutlines Cff()
    {
        CffOutlines? c = Volatile.Read(ref _cff);
        if (c is null)
        {
            c = new CffOutlines(GetTableData(CffTag), GlyphCount);
            c = Interlocked.CompareExchange(ref _cff, c, null) ?? c;
        }

        return c;
    }

    // The reader is a static lambda taking the font, so a property read allocates nothing once the value is cached.
    private static T? ReadOnce<T>(ref T? field, ref int done, OpenTypeFont font, Func<OpenTypeFont, T?> read)
        where T : class
    {
        if (Volatile.Read(ref done) == 0)
        {
            T? v = read(font);
            Interlocked.CompareExchange(ref field, v, null);
            Volatile.Write(ref done, 1);
        }

        return Volatile.Read(ref field);
    }
}
