using System;
using System.Collections.Generic;
using Chuvadi.Layout.Fonts.Internal;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>
    /// One parsed font face: identity, metrics, character map, advances and kerning. Immutable and
    /// thread-safe after construction. All raw values are in font units; helpers convert to points.
    /// The original bytes are retained (<see cref="Data"/>) so output adapters can subset and embed.
    /// </summary>
    public sealed class FontFace
    {
        private readonly CmapTable _cmap;
        private readonly ushort[] _advances;
        private readonly KerningTables _kerning;
        private readonly BigEndian? _hdmx;
        private readonly Dictionary<int, long>? _hdmxRecords;

        private FontFace(ReadOnlyMemory<byte> data, int faceIndex, FontOrigin origin, string? sourceName)
        {
            Data = data;
            FaceIndex = faceIndex;
            Origin = origin;
            SourceName = sourceName;

            SfntDirectory dir = SfntDirectory.Read(data, faceIndex);
            OutlineFormat = dir.OutlineFormat;

            BigEndian head = dir.Table(data, "head");
            UnitsPerEm = head.U16(18);
            if (UnitsPerEm < 16 || UnitsPerEm > 16384)
            {
                throw new FontFormatException($"head.unitsPerEm {UnitsPerEm} is outside 16..16384.");
            }
            int macStyle = head.U16(44);
            BoundingBox = (head.I16(36), head.I16(38), head.I16(40), head.I16(42));

            BigEndian maxp = dir.Table(data, "maxp");
            GlyphCount = maxp.U16(4);
            if (GlyphCount == 0)
            {
                throw new FontFormatException("Font has no glyphs.");
            }

            BigEndian hhea = dir.Table(data, "hhea");
            HheaMetrics = new FontLineMetrics(hhea.I16(4), hhea.I16(6), hhea.I16(8));
            int numberOfHMetrics = hhea.U16(34);
            if (numberOfHMetrics == 0)
            {
                throw new FontFormatException("hhea.numberOfHMetrics is 0.");
            }

            BigEndian hmtx = dir.Table(data, "hmtx");
            numberOfHMetrics = Math.Min(numberOfHMetrics, Math.Min(GlyphCount, hmtx.Length / 4));
            if (numberOfHMetrics == 0)
            {
                throw new FontFormatException("hmtx table is empty.");
            }
            _advances = new ushort[GlyphCount];
            for (int i = 0; i < numberOfHMetrics; i++)
            {
                _advances[i] = hmtx.U16(4L * i);
            }
            for (int i = numberOfHMetrics; i < GlyphCount; i++)
            {
                _advances[i] = _advances[numberOfHMetrics - 1];
            }

            Dictionary<int, string> names = NameTable.Read(dir.TryTable(data, "name"));
            string? legacyFamily = names.GetValueOrDefault(1);
            string? legacySub = names.GetValueOrDefault(2);
            TypographicFamilyName = names.GetValueOrDefault(16) ?? legacyFamily ?? string.Empty;
            TypographicSubfamilyName = names.GetValueOrDefault(17) ?? legacySub ?? string.Empty;
            FamilyName = legacyFamily ?? TypographicFamilyName;
            SubfamilyName = legacySub ?? TypographicSubfamilyName;
            FullName = names.GetValueOrDefault(4) ?? (FamilyName + " " + SubfamilyName).Trim();
            PostScriptName = names.GetValueOrDefault(6) ?? FullName.Replace(" ", string.Empty, StringComparison.Ordinal);
            Version = names.GetValueOrDefault(5) ?? string.Empty;

            BigEndian? os2 = dir.TryTable(data, "OS/2");
            bool italic = (macStyle & 2) != 0;
            bool bold = (macStyle & 1) != 0;
            if (os2 is BigEndian o && o.Length >= 78)
            {
                int version = o.U16(0);
                AverageCharWidth = o.I16(2);
                Weight = Math.Clamp((int)o.U16(4), 1, 1000);
                WidthClass = Math.Clamp((int)o.U16(6), 1, 9);
                EmbeddingRights = (FontEmbeddingRights)(o.U16(8) & 0x030E);
                int fsSelection = o.U16(62);
                italic = (fsSelection & 0x0001) != 0 || italic;
                bold = (fsSelection & 0x0020) != 0 || bold;
                UseTypoMetrics = (fsSelection & 0x0080) != 0;
                TypoMetrics = new FontLineMetrics(o.I16(68), o.I16(70), o.I16(72));
                WindowsMetrics = new FontLineMetrics(o.U16(74), -o.U16(76), 0);
                StrikeoutSize = o.I16(26);
                StrikeoutPosition = o.I16(28);
                if (version >= 2 && o.Length >= 90)
                {
                    XHeight = o.I16(86);
                    CapHeight = o.I16(88);
                }
            }
            else
            {
                Weight = bold ? 700 : 400;
                WidthClass = 5;
                TypoMetrics = HheaMetrics;
                WindowsMetrics = new FontLineMetrics(HheaMetrics.Ascender, HheaMetrics.Descender, 0);
                StrikeoutSize = UnitsPerEm / 20;
                StrikeoutPosition = UnitsPerEm / 4;
            }
            if (bold && Weight < 600)
            {
                Weight = 700;
            }
            IsItalic = italic;

            BigEndian? post = dir.TryTable(data, "post");
            if (post is BigEndian p && p.Length >= 16)
            {
                ItalicAngle = p.Fixed(4);
                UnderlinePosition = p.I16(8);
                UnderlineThickness = p.I16(10);
                IsFixedPitch = p.U32(12) != 0;
            }
            else
            {
                UnderlinePosition = -UnitsPerEm / 10;
                UnderlineThickness = UnitsPerEm / 20;
            }
            if (XHeight == 0)
            {
                XHeight = UnitsPerEm / 2;
            }
            if (CapHeight == 0)
            {
                CapHeight = (int)(UnitsPerEm * 0.7);
            }

            _cmap = CmapTable.Read(dir.Table(data, "cmap"));
            _kerning = KerningTables.Read(dir.TryTable(data, "GPOS"), dir.TryTable(data, "kern"));
            HasGsub = dir.Has("GSUB");
            HasGpos = dir.Has("GPOS");

            _hdmx = dir.TryTable(data, "hdmx");
            if (_hdmx is BigEndian h && h.Length >= 8 && h.U16(0) == 0)
            {
                int n = h.I16(2);
                int size = h.I32(4);
                if (n > 0 && size >= GlyphCount + 2)
                {
                    _hdmxRecords = new Dictionary<int, long>();
                    for (int i = 0; i < n; i++)
                    {
                        long rec = 8 + ((long)size * i);
                        if (!h.Has(rec, 2 + GlyphCount))
                        {
                            break;
                        }
                        _hdmxRecords[h.U8(rec)] = rec;
                    }
                }
            }
        }

        /// <summary>Parses face <paramref name="faceIndex"/> of a TrueType, OpenType or TrueType-collection font.</summary>
        /// <exception cref="FontFormatException">The bytes are not a supported, well-formed sfnt font.</exception>
        public static FontFace Load(ReadOnlyMemory<byte> data, int faceIndex = 0, FontOrigin origin = FontOrigin.Supplied, string? sourceName = null)
        {
            try
            {
                return new FontFace(data, faceIndex, origin, sourceName);
            }
            catch (FontFormatException)
            {
                throw;
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidOperationException)
            {
                throw new FontFormatException("Font data is malformed: " + ex.Message, ex);
            }
        }

        /// <summary>Number of faces in the font data: 1, or n for a TrueType collection (.ttc).</summary>
        public static int CountFaces(ReadOnlyMemory<byte> data) => SfntDirectory.CountFaces(data);

        /// <summary>The complete font file this face was read from (for a collection, the whole .ttc).</summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>Index of this face within <see cref="Data"/> (0 unless the data is a collection).</summary>
        public int FaceIndex { get; }

        public FontOrigin Origin { get; }

        /// <summary>Optional description of where the bytes came from (file name, resource name, part name).</summary>
        public string? SourceName { get; }

        public FontOutlineFormat OutlineFormat { get; }

        /// <summary>Legacy family name (name ID 1), e.g. "Calibri Light". This is what OOXML documents reference.</summary>
        public string FamilyName { get; }

        /// <summary>Legacy subfamily name (name ID 2), e.g. "Bold Italic".</summary>
        public string SubfamilyName { get; }

        /// <summary>Typographic family (name ID 16), falling back to <see cref="FamilyName"/>.</summary>
        public string TypographicFamilyName { get; }

        /// <summary>Typographic subfamily (name ID 17), falling back to <see cref="SubfamilyName"/>.</summary>
        public string TypographicSubfamilyName { get; }

        public string FullName { get; }

        public string PostScriptName { get; }

        /// <summary>Version string (name ID 5).</summary>
        public string Version { get; }

        /// <summary>OS/2 usWeightClass (100..900), or 400/700 from macStyle when there is no OS/2 table.</summary>
        public int Weight { get; }

        /// <summary>OS/2 usWidthClass, 1 (ultra-condensed) .. 9 (ultra-expanded); 5 is normal.</summary>
        public int WidthClass { get; }

        public bool IsItalic { get; }

        public bool IsBold => Weight >= 600;

        public bool IsFixedPitch { get; }

        public int UnitsPerEm { get; }

        public int GlyphCount { get; }

        /// <summary>head bounding box (xMin, yMin, xMax, yMax) in font units.</summary>
        public (int XMin, int YMin, int XMax, int YMax) BoundingBox { get; }

        public FontLineMetrics HheaMetrics { get; }

        public FontLineMetrics TypoMetrics { get; }

        /// <summary>usWinAscent / −usWinDescent, line gap 0.</summary>
        public FontLineMetrics WindowsMetrics { get; }

        /// <summary>
        /// The line metrics Word and Excel lay out with: usWinAscent and usWinDescent, plus the external leading
        /// Windows reports (the part of the hhea line gap not already inside the Windows ascent and descent), added
        /// above the ascent. For Arial and Times New Roman this is the hhea line gap (single spacing 1.15 em); for
        /// Calibri it is zero.
        /// </summary>
        public FontLineMetrics WordLineMetrics
        {
            get
            {
                int winHeight = WindowsMetrics.Ascender - WindowsMetrics.Descender;
                int hheaHeight = HheaMetrics.Ascender - HheaMetrics.Descender;
                int leading = Math.Max(0, HheaMetrics.LineGap - (winHeight - hheaHeight));
                return new FontLineMetrics(WindowsMetrics.Ascender + leading, WindowsMetrics.Descender, 0);
            }
        }

        /// <summary>OS/2 fsSelection bit 7 (USE_TYPO_METRICS).</summary>
        public bool UseTypoMetrics { get; }

        public int AverageCharWidth { get; }

        public int XHeight { get; }

        public int CapHeight { get; }

        public double ItalicAngle { get; }

        /// <summary>post.underlinePosition (top of the underline, negative below the baseline).</summary>
        public int UnderlinePosition { get; }

        public int UnderlineThickness { get; }

        public int StrikeoutPosition { get; }

        public int StrikeoutSize { get; }

        public FontEmbeddingRights EmbeddingRights { get; }

        /// <summary>The face has OpenType substitution rules (complex scripts need a shaper that applies them).</summary>
        public bool HasGsub { get; }

        public bool HasGpos { get; }

        public bool HasKerning => _kerning.HasKerning;

        /// <summary>True when the cmap is a Windows Symbol subtable.</summary>
        public bool IsSymbolFont => _cmap.IsSymbol;

        /// <summary>Glyph id for a Unicode code point, or 0 (.notdef) when the face has no glyph for it.</summary>
        public int GetGlyphId(int codepoint)
            => (uint)codepoint < LowGlyphCount ? (_lowGlyphs ?? BuildLowGlyphs())[codepoint] : _cmap.GetGlyph(codepoint);

        public bool HasGlyph(int codepoint) => GetGlyphId(codepoint) != 0;

        // Glyph ids of U+0000..U+07FF (Latin, Greek, Cyrillic, Hebrew, Arabic), filled on first use; the cmap
        // search is then skipped for most text. Built completely before it is published, so racing threads are safe.
        private const int LowGlyphCount = 0x800;
        private int[]? _lowGlyphs;

        private int[] BuildLowGlyphs()
        {
            int[] t = new int[LowGlyphCount];
            for (int cp = 0; cp < t.Length; cp++)
            {
                t[cp] = _cmap.GetGlyph(cp);
            }
            _lowGlyphs = t;
            return t;
        }

        /// <summary>Advance width of a glyph in font units (out-of-range ids return the .notdef advance).</summary>
        public int GetAdvance(int glyphId)
            => (uint)glyphId < (uint)_advances.Length ? _advances[glyphId] : _advances[0];

        /// <summary>Horizontal pair kerning between two glyphs, in font units (0 when none).</summary>
        public int GetKerning(int leftGlyph, int rightGlyph) => _kerning.Get(leftGlyph, rightGlyph);

        /// <summary>Converts font units to points at <paramref name="sizePt"/>.</summary>
        public double ToPoints(double fontUnits, double sizePt) => fontUnits * sizePt / UnitsPerEm;

        /// <summary>Selected vertical metrics.</summary>
        public FontLineMetrics GetLineMetrics(VerticalMetricsSource source) => source switch
        {
            VerticalMetricsSource.Windows => WindowsMetrics,
            VerticalMetricsSource.Hhea => HheaMetrics,
            VerticalMetricsSource.Typographic => TypoMetrics,
            _ => UseTypoMetrics ? TypoMetrics : HheaMetrics,
        };

        /// <summary>Unhinted advance width of a string in points: sum of cmap-mapped glyph advances, no kerning, no shaping.</summary>
        public double MeasureSimple(string text, double sizePt)
        {
            ArgumentNullException.ThrowIfNull(text);
            long units = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                units += GetAdvance(GetGlyphId(cp));
            }
            return ToPoints(units, sizePt);
        }

        /// <summary>
        /// Device advance in whole pixels at an integer ppem, from the 'hdmx' table when the face has a
        /// record for that ppem; null otherwise.
        /// </summary>
        public int? GetDeviceAdvance(int glyphId, int ppem)
        {
            if (_hdmx is BigEndian h && _hdmxRecords is not null && _hdmxRecords.TryGetValue(ppem, out long rec)
                && (uint)glyphId < (uint)GlyphCount)
            {
                return h.U8(rec + 2 + glyphId);
            }
            return null;
        }

        /// <summary>
        /// Excel's maximum digit width (ECMA-376 Part 1, 18.3.1.13): the widest of the digits 0-9 in whole
        /// pixels at <paramref name="dpi"/>, for this face at <paramref name="sizePt"/>. The pixel size is
        /// ppem = round(size × dpi / 72) as GDI computes it. When the face carries an 'hdmx' record for that ppem
        /// (hinted device widths, as Office fonts do) it is used; otherwise the unhinted advance is truncated to
        /// whole pixels, which reproduces Excel's documented reference points (Calibri 11 → 7 px, Arial 10 → 7 px).
        /// </summary>
        public int MaxDigitWidthPixels(double sizePt, double dpi = 96.0)
        {
            int ppem = (int)Math.Round(sizePt * dpi / 72.0, MidpointRounding.AwayFromZero);
            int max = 0;
            for (int d = '0'; d <= '9'; d++)
            {
                int g = GetGlyphId(d);
                int px = GetDeviceAdvance(g, ppem) ?? (int)Math.Floor((double)GetAdvance(g) * ppem / UnitsPerEm);
                max = Math.Max(max, px);
            }
            return Math.Max(1, max);
        }

        public override string ToString() => $"{FullName} ({Origin}{(SourceName is null ? string.Empty : ": " + SourceName)})";
    }
}
