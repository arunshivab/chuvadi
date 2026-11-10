using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>Kinds of measured segment.</summary>
    internal enum SegmentKind
    {
        Text,
        Tab,
        LineBreak,
        PageBreak,
        Image,
    }

    /// <summary>A glyph with its advance and offsets in points.</summary>
    internal readonly record struct MGlyph(int GlyphId, int Cluster, double Advance, double XOffset, double YOffset);

    /// <summary>A run of the paragraph text with one face, size and format.</summary>
    internal sealed class Segment
    {
        public required int Start { get; init; }
        public required int End { get; init; }
        public required SegmentKind Kind { get; init; }
        public required CharacterFormat Format { get; init; }
        public FontResolution? Font { get; init; }
        public double FontSize { get; init; }
        public double HorizontalScale { get; init; } = 1;
        public MGlyph[] Glyphs { get; init; } = Array.Empty<MGlyph>();

        /// <summary>Ascent above the baseline (positive) including any raise.</summary>
        public double Ascent { get; init; }

        /// <summary>Descent below the baseline (positive) including any lowering.</summary>
        public double Descent { get; init; }

        /// <summary>Baseline offset (negative = raised), applied to glyph Y.</summary>
        public double BaselineOffset { get; init; }

        public ImageInline? Image { get; init; }

        /// <summary>An inline text box (Kind is Image: it is measured like a picture).</summary>
        public TextBoxInline? TextBox { get; init; }

        public FieldInline? Field { get; init; }

        public string? HyperlinkUri { get; init; }

        public string? HyperlinkAnchor { get; init; }
    }

    /// <summary>A paragraph measured for line fitting: text, per-character advances, segments and break opportunities.</summary>
    internal sealed class MeasuredParagraph
    {
        public required FlowParagraph Source { get; init; }
        public required string Text { get; init; }

        /// <summary>Advance of each UTF-16 index (the cluster's glyph advances and letter spacing; tabs are 0 here).</summary>
        public required double[] Advance { get; init; }

        /// <summary>Prefix sums of <see cref="Advance"/>; length = Text.Length + 1.</summary>
        public required double[] Prefix { get; init; }

        public required LineBreakOpportunity[] Breaks { get; init; }

        public required bool[] ClusterStart { get; init; }

        public required List<Segment> Segments { get; init; }

        /// <summary>Segment index for each UTF-16 index.</summary>
        public required int[] SegmentOf { get; init; }

        /// <summary>Metrics of the paragraph mark (height of an empty paragraph and part of the last line).</summary>
        public required double MarkAscent { get; init; }

        public required double MarkDescent { get; init; }

        /// <summary>Width of a hyphen in the format at each soft-hyphen position (drawn when a line breaks there).</summary>
        public required Dictionary<int, (double Width, MGlyph Glyph)> SoftHyphens { get; init; }

        /// <summary>Positional tabs (w:ptab) by text index; null when the paragraph has none.</summary>
        public Dictionary<int, PositionalTab>? PositionalTabs { get; init; }

        /// <summary>Footnote and endnote references by text index (the label's first character); null when none.</summary>
        public List<(int Index, FlowNote Note)>? Notes { get; init; }

        public double Width(int start, int end) => Prefix[end] - Prefix[start];

        public long ApproximateBytes => (Text.Length * 24L) + (Segments.Count * 96L) + GlyphCount() * 40L;

        private long GlyphCount()
        {
            long n = 0;
            foreach (Segment s in Segments)
            {
                n += s.Glyphs.Length;
            }
            return n;
        }
    }

    /// <summary>Field values available while measuring and emitting.</summary>
    internal interface IFieldValues
    {
        string Evaluate(FieldInline field);
    }

    /// <summary>Measures paragraphs.</summary>
    internal static class ParagraphMeasurer
    {
        /// <summary>Superscript/subscript size factor and baseline shifts (fractions of the run's font size).</summary>
        internal const double SuperSubScale = 2.0 / 3.0;
        internal const double SuperscriptRise = 1.0 / 3.0;
        internal const double SubscriptDrop = 0.14;

        /// <summary>Small capitals are uppercase letters at this fraction of the font size.</summary>
        internal const double SmallCapsScale = 0.8;

        internal const char LineSeparator = '\u2028';
        internal const char PageBreakChar = '\u000C';

        /// <summary>A column break in the paragraph text (a mandatory break, like the page break).</summary>
        internal const char ColumnBreakChar = '\u000B';

        internal static bool IsPageOrColumnBreak(char c) => c == PageBreakChar || c == ColumnBreakChar;
        internal const char ObjectChar = '\uFFFC';

        private sealed record Piece(string Text, CharacterFormat Format, SegmentKind Kind, double SizeScale, ImageInline? Image, FieldInline? Field, string? Uri, string? Anchor, PositionalTab? Positional = null)
        {
            public TextBoxInline? Box { get; init; }

            public FlowNote? Note { get; init; }
        }

        public static MeasuredParagraph Measure(FlowParagraph p, LayoutContext ctx, IFieldValues fields)
        {
            List<Piece> pieces = new List<Piece>();
            if (p.ListLabel is ListLabel label && label.Text.Length > 0)
            {
                AddText(pieces, label.Text, label.Format, null, null);
                if (label.Suffix == ListLabelSuffix.Tab)
                {
                    pieces.Add(new Piece("\t", label.Format, SegmentKind.Tab, 1, null, null, null, null));
                }
                else if (label.Suffix == ListLabelSuffix.Space)
                {
                    AddText(pieces, " ", label.Format, null, null);
                }
            }
            foreach (FlowInline inline in p.Inlines)
            {
                if (inline.Format.Hidden)
                {
                    continue;
                }
                switch (inline)
                {
                    case TextRun t:
                        AddText(pieces, t.Text, t.Format, t.HyperlinkUri, t.HyperlinkAnchor);
                        break;
                    case TabInline tab:
                        pieces.Add(new Piece("\t", tab.Format, SegmentKind.Tab, 1, null, null, null, null, tab.Positional));
                        break;
                    case BreakInline br:
                        pieces.Add(br.Kind switch
                        {
                            BreakKind.Line => new Piece(LineSeparator.ToString(), br.Format, SegmentKind.LineBreak, 1, null, null, null, null),
                            BreakKind.Column => new Piece(ColumnBreakChar.ToString(), br.Format, SegmentKind.PageBreak, 1, null, null, null, null),
                            _ => new Piece(PageBreakChar.ToString(), br.Format, SegmentKind.PageBreak, 1, null, null, null, null),
                        });
                        break;
                    case FieldInline f:
                        string value = fields.Evaluate(f);
                        if (value.Length > 0)
                        {
                            AddText(pieces, value, f.Format, null, null, f);
                        }
                        break;
                    case ImageInline img:
                        pieces.Add(new Piece(ObjectChar.ToString(), img.Format, SegmentKind.Image, 1, img, null, null, null));
                        break;
                    case TextBoxInline box:
                        pieces.Add(new Piece(ObjectChar.ToString(), box.Format, SegmentKind.Image, 1, null, null, null, null) { Box = box });
                        break;
                    case NoteReferenceInline note:
                        int before = pieces.Count;
                        if (note.Label.Length > 0)
                        {
                            AddText(pieces, note.Label, note.Format, null, null);
                        }
                        else
                        {
                            // A custom mark follows as ordinary text: the reference itself takes no room.
                            pieces.Add(new Piece("\u200B", note.Format, SegmentKind.Text, 1, null, null, null, null));
                        }
                        pieces[before] = pieces[before] with { Note = note.Note };
                        break;
                }
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (Piece piece in pieces)
            {
                sb.Append(piece.Text);
            }
            string text = sb.ToString();
            Dictionary<int, PositionalTab>? positionalTabs = null;
            List<(int Index, FlowNote Note)>? notes = null;
            int pieceAt = 0;
            foreach (Piece piece in pieces)
            {
                if (piece.Positional is PositionalTab ptab)
                {
                    (positionalTabs ??= new Dictionary<int, PositionalTab>())[pieceAt] = ptab;
                }
                if (piece.Note is FlowNote fn)
                {
                    (notes ??= new List<(int, FlowNote)>()).Add((pieceAt, fn));
                }
                pieceAt += piece.Text.Length;
            }
            LayoutLimits.Check(nameof(LayoutLimits.MaxCharactersPerBlock), ctx.Limits.MaxCharactersPerBlock, text.Length);

            int n = text.Length;
            double[] advance = new double[n];
            int[] segmentOf = new int[n];
            List<Segment> segments = new List<Segment>();
            Dictionary<int, (double, MGlyph)> softHyphens = new Dictionary<int, (double, MGlyph)>();
            char[] shapeChars = text.ToCharArray();

            // Pass 1: decide segments (face per cluster) without shaping.
            bool[] clusterStart = GraphemeClusters.GetBoundaries(text);
            List<(int Start, int End, Piece Piece, FontResolution? Font, double Size, UnicodeScript Script)> runs =
                new List<(int, int, Piece, FontResolution?, double, UnicodeScript)>();
            int offset = 0;
            // Slot resolution is the same for every cluster of a piece in one slot: remember the last one.
            Piece? memoPiece = null;
            FontSlot memoSlot = FontSlot.Ascii;
            FontResolution? memoRes = null;
            double memoSize = 0;
            foreach (Piece piece in pieces)
            {
                int start = offset;
                int end = offset + piece.Text.Length;
                offset = end;
                if (piece.Kind != SegmentKind.Text)
                {
                    FontResolution res = ResolveSlot(ctx, piece.Format, FontSlot.Ascii, out double size);
                    runs.Add((start, end, piece, res, size * piece.SizeScale, UnicodeScript.Common));
                    continue;
                }
                List<ScriptRun> scriptRuns = ScriptItemizer.Itemize(text, start, end - start);
                foreach (ScriptRun sr in scriptRuns)
                {
                    int i = sr.Start;
                    int srEnd = sr.Start + sr.Length;
                    while (i < srEnd)
                    {
                        int clusterEnd = i + 1;
                        while (clusterEnd < srEnd && !clusterStart[clusterEnd])
                        {
                            clusterEnd++;
                        }
                        int cp = GraphemeClusters.Decode(text, i, out _);
                        FontSlot slot = SlotOf(cp, piece.Format.ComplexScriptRun);
                        if (piece.Format.ComplexScriptRun is null && slot == FontSlot.Ascii && cp is ' ' or '\u00A0' && BetweenComplex(text, i))
                        {
                            // A space between two complex-script words takes the complex-script font and size, as the
                            // words around it do (reference corpus d04: LibreOffice spaces Tamil words by the Tamil font).
                            slot = FontSlot.Complex;
                        }
                        if (!ReferenceEquals(memoPiece, piece) || memoSlot != slot || memoRes is null)
                        {
                            memoRes = ResolveSlot(ctx, piece.Format, slot, out memoSize);
                            memoPiece = piece;
                            memoSlot = slot;
                        }
                        FontResolution primary = memoRes;
                        double size = memoSize;
                        // Most clusters are in the primary face; the fallback search runs only for the rest.
                        FontResolution res = IsSpaceLike(cp) || primary.Face.HasGlyph(cp) ? primary : ctx.Fonts.ResolveForCodepoint(primary, cp);
                        if (primary.Face.Origin == FontOrigin.Embedded && !IsSpaceLike(cp) && !primary.Face.HasGlyph(cp) && !FontResolver.IsDefaultIgnorable(cp))
                        {
                            WarnEmbeddedMissing(ctx, primary, res, cp);
                        }
                        size *= piece.SizeScale;
                        // Glyph substitutions for characters Word draws with another glyph when the face lacks them.
                        for (int k = i; k < clusterEnd; k++)
                        {
                            char ch = text[k];
                            if (ch == '\u00A0' && !res.Face.HasGlyph(0xA0))
                            {
                                shapeChars[k] = ' ';
                            }
                            else if (ch == '\u2011' && !res.Face.HasGlyph(0x2011))
                            {
                                shapeChars[k] = '-';
                            }
                        }
                        if (runs.Count > 0)
                        {
                            var last = runs[^1];
                            if (last.End == i && ReferenceEquals(last.Piece, piece) && ReferenceEquals(last.Font?.Face, res.Face)
                                && last.Font!.SyntheticBold == res.SyntheticBold && last.Font.SyntheticItalic == res.SyntheticItalic
                                && last.Size == size && last.Script == sr.Script)
                            {
                                runs[^1] = (last.Start, clusterEnd, piece, last.Font, size, sr.Script);
                                i = clusterEnd;
                                continue;
                            }
                        }
                        runs.Add((i, clusterEnd, piece, res, size, sr.Script));
                        i = clusterEnd;
                    }
                }
            }

            string shapeText = new string(shapeChars);
            foreach (var run in runs)
            {
                Piece piece = run.Piece;
                CharacterFormat f = piece.Format;
                FontFace face = run.Font!.Face;
                double size = run.Size;
                double scale = Math.Clamp(f.HorizontalScalePercent, 1, 600) / 100.0;
                double unitsToPt = size / face.UnitsPerEm;
                (double ascent, double descent, double baselineOffset) = Metrics(face, f, size);
                MGlyph[] glyphs = Array.Empty<MGlyph>();
                if (piece.Kind == SegmentKind.Text)
                {
                    bool kern = f.KerningMinimumSize > 0 && size >= f.KerningMinimumSize;
                    IShaper shaper = ctx.ShaperFor(face, run.Script);
                    IReadOnlyList<ShapedGlyph> shaped = shaper.Shape(face, shapeText, run.Start, run.End - run.Start,
                        new ShapingOptions { Script = run.Script, Kerning = kern, Language = f.Language });
                    glyphs = new MGlyph[shaped.Count];
                    for (int g = 0; g < shaped.Count; g++)
                    {
                        ShapedGlyph sg = shaped[g];
                        double adv = sg.XAdvance * unitsToPt * scale;
                        glyphs[g] = new MGlyph(sg.GlyphId, sg.Cluster, adv, sg.XOffset * unitsToPt * scale, -sg.YOffset * unitsToPt);
                        if (sg.Cluster >= run.Start && sg.Cluster < run.End)
                        {
                            advance[sg.Cluster] += adv;
                        }
                    }
                    if (f.CharacterSpacing != 0)
                    {
                        for (int k = run.Start; k < run.End; k++)
                        {
                            if (clusterStart[k])
                            {
                                advance[k] += f.CharacterSpacing;
                            }
                        }
                    }
                    for (int k = run.Start; k < run.End; k++)
                    {
                        if (text[k] == '\u00AD')
                        {
                            int hg = face.GetGlyphId('-');
                            double hw = face.GetAdvance(hg) * unitsToPt * scale;
                            softHyphens[k] = (hw, new MGlyph(hg, k, hw, 0, 0));
                        }
                    }
                }
                else if (piece.Kind == SegmentKind.Image && piece.Image is ImageInline img)
                {
                    advance[run.Start] = img.Width;
                    ascent = img.Height;
                    descent = 0;
                }
                else if (piece.Kind == SegmentKind.Image && piece.Box is TextBoxInline box)
                {
                    advance[run.Start] = box.Width;
                    ascent = box.Height;
                    descent = 0;
                }

                int segIndex = segments.Count;
                segments.Add(new Segment
                {
                    Start = run.Start,
                    End = run.End,
                    Kind = piece.Kind,
                    Format = f,
                    Font = run.Font,
                    FontSize = size,
                    HorizontalScale = scale,
                    Glyphs = glyphs,
                    Ascent = ascent,
                    Descent = descent,
                    BaselineOffset = baselineOffset,
                    Image = piece.Image,
                    TextBox = piece.Box,
                    Field = piece.Field,
                    HyperlinkUri = piece.Uri,
                    HyperlinkAnchor = piece.Anchor,
                });
                for (int k = run.Start; k < run.End; k++)
                {
                    segmentOf[k] = segIndex;
                }
            }

            double[] prefix = new double[n + 1];
            for (int k = 0; k < n; k++)
            {
                prefix[k + 1] = prefix[k] + advance[k];
            }

            LineBreakOpportunity[] breaks = LineBreaker.GetOpportunities(text);
            // Never break inside a grapheme cluster (UAX #14 already ensures this for conformant text; enforce for safety).
            for (int k = 1; k < n; k++)
            {
                if (!clusterStart[k])
                {
                    breaks[k] = LineBreakOpportunity.NoBreak;
                }
            }

            FontResolution markFont = ResolveSlot(ctx, p.MarkFormat, FontSlot.Ascii, out double markSize);
            (double markAscent, double markDescent, _) = Metrics(markFont.Face, p.MarkFormat, markSize);

            return new MeasuredParagraph
            {
                Source = p,
                Text = text,
                Advance = advance,
                Prefix = prefix,
                Breaks = breaks,
                ClusterStart = clusterStart,
                Segments = segments,
                SegmentOf = segmentOf,
                MarkAscent = markAscent,
                MarkDescent = markDescent,
                SoftHyphens = softHyphens,
                PositionalTabs = positionalTabs,
                Notes = notes,
            };
        }

        private static bool IsSpaceLike(int cp) => cp is ' ' or '\u00A0' or '\t';

        /// <summary>D-116: a character missing from an embedded (subset) font is never replaced silently. Once per family.</summary>
        private static void WarnEmbeddedMissing(LayoutContext ctx, FontResolution primary, FontResolution used, int cp)
        {
            string family = primary.Face.FamilyName;
            string where = used.Face.Origin switch
            {
                FontOrigin.System => "installed",
                FontOrigin.Supplied => "supplied",
                _ => "bundled",
            };
            string message;
            if (ReferenceEquals(used, primary) || !used.Face.HasGlyph(cp))
            {
                message = $"The font '{family}' embedded in the document has no glyph for some characters (first U+{cp:X4}), and no other font has them; they are drawn as missing glyphs.";
            }
            else if (string.Equals(used.Face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                message = $"The font '{family}' embedded in the document has no glyph for some characters (first U+{cp:X4}); the {where} '{used.Face.FamilyName}' is used for them.";
            }
            else
            {
                message = $"The font '{family}' embedded in the document has no glyph for some characters (first U+{cp:X4}), and no other source has them in '{family}'; '{used.Face.FamilyName}' is used for them.";
            }
            ctx.Warn(LayoutWarningCodes.EmbeddedFontMissingGlyphs, LayoutWarningSeverity.Fidelity, message, family);
        }

        /// <summary>Whether the spaces around index i have a complex-script character on both sides.</summary>
        private static bool BetweenComplex(string text, int i)
        {
            int a = i - 1;
            while (a >= 0 && text[a] is ' ' or '\u00A0')
            {
                a--;
            }
            int b = i + 1;
            while (b < text.Length && text[b] is ' ' or '\u00A0')
            {
                b++;
            }
            if (a < 0 || b >= text.Length)
            {
                return false;
            }
            if (char.IsLowSurrogate(text[a]) && a > 0)
            {
                a--;
            }
            return SlotOf(GraphemeClusters.Decode(text, a, out _)) == FontSlot.Complex
                && SlotOf(GraphemeClusters.Decode(text, b, out _)) == FontSlot.Complex;
        }

        private static void AddText(List<Piece> pieces, string text, CharacterFormat format, string? uri, string? anchor, FieldInline? field = null)
        {
            // Normalise line ends inside runs: CR LF, CR and LF become line breaks; tabs become tab pieces.
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                char c = i < text.Length ? text[i] : '\0';
                if (i == text.Length || c == '\t' || c == '\n' || c == '\r' || c == '\u000B' || c == '\u000C')
                {
                    if (i > start)
                    {
                        AddCased(pieces, text.Substring(start, i - start), format, uri, anchor, field);
                    }
                    if (i == text.Length)
                    {
                        break;
                    }
                    if (c == '\t')
                    {
                        pieces.Add(new Piece("\t", format, SegmentKind.Tab, 1, null, null, uri, anchor));
                    }
                    else if (c == '\u000C' || c == '\u000B')
                    {
                        pieces.Add(new Piece(PageBreakChar.ToString(), format, SegmentKind.PageBreak, 1, null, null, null, null));
                    }
                    else
                    {
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        {
                            i++;
                        }
                        pieces.Add(new Piece(LineSeparator.ToString(), format, SegmentKind.LineBreak, 1, null, null, null, null));
                    }
                    start = i + 1;
                }
            }
        }

        private static void AddCased(List<Piece> pieces, string text, CharacterFormat format, string? uri, string? anchor, FieldInline? field)
        {
            switch (format.Capitals)
            {
                case TextCapitals.AllCaps:
                    pieces.Add(new Piece(text.ToUpperInvariant(), format, SegmentKind.Text, 1, null, field, uri, anchor));
                    return;
                case TextCapitals.SmallCaps:
                    int s = 0;
                    while (s < text.Length)
                    {
                        bool lower = char.IsLower(text[s]);
                        int e = s + 1;
                        while (e < text.Length && char.IsLower(text[e]) == lower)
                        {
                            e++;
                        }
                        string part = text.Substring(s, e - s);
                        pieces.Add(lower
                            ? new Piece(part.ToUpperInvariant(), format, SegmentKind.Text, SmallCapsScale, null, field, uri, anchor)
                            : new Piece(part, format, SegmentKind.Text, 1, null, field, uri, anchor));
                        s = e;
                    }
                    return;
                default:
                    pieces.Add(new Piece(text, format, SegmentKind.Text, 1, null, field, uri, anchor));
                    return;
            }
        }

        internal enum FontSlot
        {
            Ascii,
            HighAnsi,
            EastAsian,
            Complex,
        }

        /// <summary>The font slot of a character under a run's <see cref="CharacterFormat.ComplexScriptRun"/> (R3).</summary>
        internal static FontSlot SlotOf(int cp, bool? complexRun)
        {
            if (complexRun == true)
            {
                return FontSlot.Complex;
            }
            FontSlot s0 = SlotOf(cp);
            if (complexRun == false && s0 == FontSlot.Complex)
            {
                UnicodeScript sc = UnicodeScripts.GetScript(cp);
                return sc is UnicodeScript.Hebrew or UnicodeScript.Arabic or UnicodeScript.Syriac or UnicodeScript.Thaana ? FontSlot.Ascii : FontSlot.HighAnsi;
            }
            return s0;
        }

        internal static FontSlot SlotOf(int cp)
        {
            UnicodeScript s = UnicodeScripts.GetScript(cp);
            if (UnicodeScripts.NeedsComplexShaping(s))
            {
                return FontSlot.Complex;
            }
            if (s is UnicodeScript.Han or UnicodeScript.Hiragana or UnicodeScript.Katakana or UnicodeScript.Hangul or UnicodeScript.Bopomofo
                || (cp >= 0x3000 && cp <= 0x303F) || (cp >= 0xFF00 && cp <= 0xFFEF))
            {
                return FontSlot.EastAsian;
            }
            return cp < 0x80 ? FontSlot.Ascii : FontSlot.HighAnsi;
        }

        internal static FontResolution ResolveSlot(LayoutContext ctx, CharacterFormat f, FontSlot slot, out double size)
        {
            string family = slot switch
            {
                FontSlot.HighAnsi => f.HighAnsiFontFamily ?? f.FontFamily,
                FontSlot.EastAsian => f.EastAsianFontFamily ?? f.FontFamily,
                FontSlot.Complex => f.ComplexScriptFontFamily ?? f.FontFamily,
                _ => f.FontFamily,
            };
            bool complex = slot == FontSlot.Complex;
            bool bold = complex ? f.ComplexScriptBold ?? f.Bold : f.Bold;
            bool italic = complex ? f.ComplexScriptItalic ?? f.Italic : f.Italic;
            size = complex ? f.ComplexScriptFontSize ?? f.FontSize : f.FontSize;
            if (f.VerticalAlignment != VerticalTextAlignment.Baseline)
            {
                size *= SuperSubScale;
            }
            FontResolution r = ctx.Fonts.Resolve(new FontRequest(family, bold ? 700 : 400, italic, f.GenericFamily));
            if (r.Kind == FontMatchKind.Substitute || r.Kind == FontMatchKind.GenericFallback || r.Kind == FontMatchKind.LastResort)
            {
                ctx.Warn(LayoutWarningCodes.NonMetricFontSubstitution, LayoutWarningSeverity.Fidelity,
                    $"'{family}' is not available; '{r.Face.FamilyName}' is used and is not metric-compatible, so line and page breaks may differ.", family);
            }
            else if (r.Kind == FontMatchKind.MetricCompatibleSubstitute)
            {
                ctx.Warn(LayoutWarningCodes.MetricFontSubstitution, LayoutWarningSeverity.Info,
                    $"'{family}' is not available; the metric-compatible '{r.Face.FamilyName}' is used (same advance widths and line metrics), so line and page breaks are kept.", family);
            }
            return r;
        }

        /// <summary>Ascent, descent (positive) and baseline offset (negative = raised) for a face at an effective size.</summary>
        internal static (double Ascent, double Descent, double BaselineOffset) Metrics(FontFace face, CharacterFormat f, double size)
        {
            FontLineMetrics m = face.WordLineMetrics;
            double ascent = m.Ascender * size / face.UnitsPerEm;
            double descent = -m.Descender * size / face.UnitsPerEm;
            double shift = f.BaselineShift;
            if (f.VerticalAlignment == VerticalTextAlignment.Superscript)
            {
                shift += f.FontSize * SuperscriptRise;
            }
            else if (f.VerticalAlignment == VerticalTextAlignment.Subscript)
            {
                shift -= f.FontSize * SubscriptDrop;
            }
            return (ascent + Math.Max(0, shift), descent + Math.Max(0, -shift), -shift);
        }
    }
}
