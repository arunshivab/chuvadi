using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Resolves EFFECTIVE (fully inherited) formatting through the style hierarchy, per
/// ECMA-376 Part 1 §17.7.2 (style hierarchy) and §17.7.3 (toggle properties):
/// <list type="number">
/// <item>document defaults (w:docDefaults);</item>
/// <item>the paragraph style and its basedOn chain (root first, each level overriding);</item>
/// <item>the character style and its basedOn chain;</item>
/// <item>direct formatting, which always wins.</item>
/// </list>
/// Toggle properties (bold, italic, caps, small caps, strike, double strike, outline, shadow,
/// emboss, imprint, hidden, and the complex-script bold/italic) set by BOTH the paragraph
/// style and the character style combine by exclusive-or; set by one of them, that value
/// applies; direct formatting overrides absolutely.
/// <para>Design choice: the combined style-level value REPLACES a toggle set in the document
/// defaults (the spec does not pin docDefaults' interaction with toggles down; Word documents
/// do not set toggles in docDefaults in practice).</para>
/// </summary>
internal static class FormatResolver
{
    private const int MaxChainDepth = 32;

    public static TextFormat ResolveRun(Document doc, Paragraph p, Run r, TableCellRef? cell = null)
    {
        var bag = RunBag.From(doc.Defaults.RunFormat);

        // Table style level (lowest style level, §17.7.2): whole-table run format, then the
        // conditional regions that apply to the cell, root style first.
        var tableStyle = new RunBag();
        if (cell is TableCellRef c)
        {
            var chain = Chain(doc, c.Table.Format.StyleId, StyleType.Table);
            foreach (var s in chain) tableStyle.Overlay(RunBag.From(s.RunFormat));
            foreach (var region in ApplicableRegions(doc, c))
                foreach (var s in chain)
                    if (s.ConditionalFormats.TryGetValue(region, out var cf)) tableStyle.Overlay(RunBag.From(cf.RunFormat));
        }

        var paragraphStyle = ChainRunBag(doc, ParagraphStyleId(doc, p), StyleType.Paragraph);
        var characterStyleId = r.EffectiveCharacterStyleId;
        var characterStyle = ChainRunBag(doc, characterStyleId, StyleType.Character);
        // Toggles XOR across all style levels (table, paragraph, character); others: later wins.
        bag.Overlay(RunBag.CombineStyleLevels(RunBag.CombineStyleLevels(tableStyle, paragraphStyle), characterStyle));
        bag.Overlay(RunBag.From(r.Format));
        return bag.ToFormat();
    }

    /// <summary>A list label's run formatting: the paragraph mark's effective formatting (with the level's character style), then the level's run formatting.</summary>
    public static TextFormat ResolveListLabelFormat(Document doc, Paragraph p, TextFormat? levelFormat, string? levelStyleId, TableCellRef? cell)
    {
        var mark = new Run("", p.Format.MarkFormat ?? TextFormat.None) { CharacterStyleId = levelStyleId };
        var bag = RunBag.From(ResolveRun(doc, p, mark, cell));
        bag.Overlay(RunBag.From(levelFormat));
        return bag.ToFormat();
    }

    public static ParagraphFormat ResolveParagraph(Document doc, Paragraph p, TableCellRef? cell = null)
    {
        var levels = new List<ParagraphFormat> { doc.Defaults.ParagraphFormat };
        if (cell is TableCellRef c)
        {
            var chain = Chain(doc, c.Table.Format.StyleId, StyleType.Table);
            foreach (var s in chain) levels.Add(s.ParagraphFormat);
            foreach (var region in ApplicableRegions(doc, c))
                foreach (var s in chain)
                    if (s.ConditionalFormats.TryGetValue(region, out var cf)) levels.Add(cf.ParagraphFormat);
        }
        // Numbering level formatting (indents, number tab). Numbering applied through the paragraph
        // style sits below the style (ECMA-376 §17.7.2); numbering applied directly to the paragraph
        // overrides the style's indents, as Word renders it (e.g. List Paragraph's 36pt indent does
        // not flatten deeper list levels).
        var list = ListNumbering.Resolve(doc, p);
        bool direct = list is not null && (p.ListInstance is not null || p.LegacyListKind != ListKind.None);
        if (list is { } fromStyle && !direct) levels.Add(fromStyle.LevelAt(fromStyle.Level).ParagraphFormat);
        foreach (var s in Chain(doc, ParagraphStyleId(doc, p), StyleType.Paragraph)) levels.Add(s.ParagraphFormat);
        if (list is { } directList && direct) levels.Add(directList.LevelAt(directList.Level).ParagraphFormat);
        levels.Add(p.Format);

        var result = new ParagraphFormat();
        var tabs = new SortedDictionary<double, TabStop>();
        ParagraphBorders? borders = null;
        foreach (var level in levels)
        {
            var keepBorders = borders;
            // Mutually exclusive pairs: a level that sets one side replaces an inherited other side.
            if (level.FirstLineIndentPt is not null && level.HangingIndentPt is null) result.HangingIndentPt = null;
            if (level.HangingIndentPt is not null && level.FirstLineIndentPt is null) result.FirstLineIndentPt = null;
            if (level.SpaceBeforePt is not null && level.SpaceBeforeAuto is null) result.SpaceBeforeAuto = null;
            if (level.SpaceAfterPt is not null && level.SpaceAfterAuto is null) result.SpaceAfterAuto = null;
            result.ApplyFrom(level);

            // Tab stops accumulate down the hierarchy; a Clear stop removes the inherited one.
            foreach (var t in level.TabStops)
            {
                if (t.Alignment == TabAlignment.Clear) tabs.Remove(t.PositionPt);
                else tabs[t.PositionPt] = t;
            }
            // Borders merge per side.
            if (level.Borders is { } lb)
            {
                borders = keepBorders?.Clone() ?? new ParagraphBorders();
                borders.Top = lb.Top ?? borders.Top;
                borders.Left = lb.Left ?? borders.Left;
                borders.Bottom = lb.Bottom ?? borders.Bottom;
                borders.Right = lb.Right ?? borders.Right;
                borders.Between = lb.Between ?? borders.Between;
                borders.Bar = lb.Bar ?? borders.Bar;
            }
        }
        result.TabStops.Clear();
        result.TabStops.AddRange(tabs.Values);
        result.Borders = borders;
        return result;
    }

    // ---- Tables ------------------------------------------------------------------------------

    /// <summary>Effective table format: table style chain (root first), then the table's direct format. Borders/margins merge per side.</summary>
    public static TableFormat ResolveTable(Document doc, DocTable t)
    {
        var result = new TableFormat();
        TableBorders? borders = null;
        CellMargins? margins = null;
        void Apply(TableFormat f)
        {
            result.Look = f.Look ?? result.Look; result.Width = f.Width ?? result.Width;
            result.Alignment = f.Alignment ?? result.Alignment; result.IndentPt = f.IndentPt ?? result.IndentPt;
            result.Layout = f.Layout ?? result.Layout; result.CellSpacingPt = f.CellSpacingPt ?? result.CellSpacingPt;
            result.Shading = f.Shading ?? result.Shading; result.RightToLeft = f.RightToLeft ?? result.RightToLeft;
            result.RowBandSize = f.RowBandSize ?? result.RowBandSize; result.ColumnBandSize = f.ColumnBandSize ?? result.ColumnBandSize;
            result.Caption = f.Caption ?? result.Caption; result.Description = f.Description ?? result.Description;
            if (f.Borders is { } b) borders = MergeBorders(borders, b);
            if (f.CellMargins is { } m) margins = MergeMargins(margins, m);
        }
        foreach (var s in Chain(doc, t.Format.StyleId, StyleType.Table)) Apply(s.TableFormat);
        // The legacy default grid (DocTable.Borders, no explicit Format.Borders) is direct formatting too.
        var direct = t.Format;
        Apply(direct);
        if (direct.Borders is null && t.Borders) borders = MergeBorders(borders, TableBorders.Grid(BorderSpec.Single(0.5)));
        result.StyleId = t.Format.StyleId;
        result.Borders = borders;
        result.CellMargins = margins;
        return result;
    }

    /// <summary>
    /// Effective cell format: table-level default margins and outer/inside borders mapped onto
    /// the cell's four edges, then the table style's cell format, then each applicable
    /// conditional region (its table borders mapped onto the region, its cell format), then the
    /// cell's direct formatting. Every edge of the result is resolved (null = no border).
    /// </summary>
    public static TableCellFormat ResolveCell(Document doc, TableCellRef c)
    {
        Validate(c);
        var t = c.Table;
        var cell = t.Rows[c.Row].Cells[c.Column];
        int span = Math.Clamp(cell.ColumnSpan, 1, t.Columns - c.Column);
        int lastRow = t.Rows.Count - 1, lastCol = t.Columns - 1;

        var tableFormat = ResolveTable(doc, t);
        var result = new TableCellFormat { Margins = tableFormat.CellMargins };
        // One set of edges, overwritten level by level in priority order, so a later level's
        // table borders beat an earlier level's cell borders and vice versa.
        var edges = new CellBorders();
        ApplyRegionBorders(edges, tableFormat.Borders, c.Row, c.Column, span, 0, lastRow, 0, lastCol);

        var chain = Chain(doc, t.Format.StyleId, StyleType.Table);
        foreach (var s in chain) ApplyCellLevel(result, edges, s.CellFormat);
        foreach (var region in ApplicableRegions(doc, c))
        {
            var (r0, r1, c0, c1) = RegionBounds(region, c, lastRow, lastCol);
            foreach (var s in chain)
            {
                if (!s.ConditionalFormats.TryGetValue(region, out var cf)) continue;
                ApplyRegionBorders(edges, cf.TableFormat.Borders, c.Row, c.Column, span, r0, r1, c0, c1);
                if (cf.TableFormat.CellMargins is { } m) result.Margins = MergeMargins(result.Margins, m);
                // Region-level table shading (tblStylePr/tblPr/shd) shades the region's cells.
                if (cf.TableFormat.Shading is { } sh) result.Shading = sh;
                ApplyCellLevel(result, edges, cf.CellFormat);
            }
        }
        ApplyCellLevel(result, edges, TableCellFormat.FromCell(cell));

        result.Borders = new CellBorders
        {
            Top = edges.Top, Left = edges.Left, Bottom = edges.Bottom, Right = edges.Right,
            DiagonalDown = edges.DiagonalDown, DiagonalUp = edges.DiagonalUp,
        };
        return result;
    }

    /// <summary>Applies one level's cell format: its edges overwrite the running edges, everything else merges.</summary>
    private static void ApplyCellLevel(TableCellFormat result, CellBorders edges, TableCellFormat level)
    {
        if (level.Borders is { } b)
        {
            edges.Top = b.Top ?? edges.Top; edges.Left = b.Left ?? edges.Left;
            edges.Bottom = b.Bottom ?? edges.Bottom; edges.Right = b.Right ?? edges.Right;
            edges.DiagonalDown = b.DiagonalDown ?? edges.DiagonalDown; edges.DiagonalUp = b.DiagonalUp ?? edges.DiagonalUp;
        }
        result.ApplyFrom(level);
    }

    /// <summary>Throws when <paramref name="c"/> does not address a cell of its table.</summary>
    internal static void Validate(TableCellRef c)
    {
        if (c.Table is null) throw new ArgumentException("TableCellRef.Table is null.", nameof(c));
        if (c.Row < 0 || c.Row >= c.Table.Rows.Count) throw new ArgumentOutOfRangeException(nameof(c), $"Row {c.Row} is outside the table (0..{c.Table.Rows.Count - 1}).");
        if (c.Column < 0 || c.Column >= c.Table.Columns) throw new ArgumentOutOfRangeException(nameof(c), $"Column {c.Column} is outside the table (0..{c.Table.Columns - 1}).");
    }

    /// <summary>
    /// The conditional regions that apply to a cell, in the order ECMA-376 §17.7.6 applies them
    /// (later wins): whole table, column bands, row bands, first/last row, first/last column,
    /// then the corner cells. With no tblLook anywhere, Word's default look (0x04A0: header row,
    /// first column, row banding) applies.
    /// </summary>
    internal static List<TableRegion> ApplicableRegions(Document doc, TableCellRef c)
    {
        Validate(c);
        var t = c.Table;
        var tf = ResolveTable(doc, t);
        var look = tf.Look ?? TableLook.Default;
        int rows = t.Rows.Count, cols = t.Columns;
        bool firstRow = look.HasFlag(TableLook.FirstRow) && c.Row == 0;
        bool lastRow = look.HasFlag(TableLook.LastRow) && c.Row == rows - 1;
        bool firstCol = look.HasFlag(TableLook.FirstColumn) && c.Column == 0;
        bool lastCol = look.HasFlag(TableLook.LastColumn) && c.Column + Math.Max(1, t.Rows[c.Row].Cells[c.Column].ColumnSpan) - 1 >= cols - 1;

        var list = new List<TableRegion> { TableRegion.WholeTable };
        if (!look.HasFlag(TableLook.NoVerticalBand) && !firstCol && !lastCol)
        {
            int size = Math.Max(1, tf.ColumnBandSize ?? 1);
            int idx = (c.Column - (look.HasFlag(TableLook.FirstColumn) ? 1 : 0)) / size;
            list.Add(idx % 2 == 0 ? TableRegion.OddColumnBand : TableRegion.EvenColumnBand);
        }
        if (!look.HasFlag(TableLook.NoHorizontalBand) && !firstRow && !lastRow)
        {
            int size = Math.Max(1, tf.RowBandSize ?? 1);
            int idx = (c.Row - (look.HasFlag(TableLook.FirstRow) ? 1 : 0)) / size;
            list.Add(idx % 2 == 0 ? TableRegion.OddRowBand : TableRegion.EvenRowBand);
        }
        if (firstRow) list.Add(TableRegion.FirstRow);
        if (lastRow) list.Add(TableRegion.LastRow);
        if (firstCol) list.Add(TableRegion.FirstColumn);
        if (lastCol) list.Add(TableRegion.LastColumn);
        if (firstRow && firstCol) list.Add(TableRegion.TopLeftCell);
        if (firstRow && lastCol) list.Add(TableRegion.TopRightCell);
        if (lastRow && firstCol) list.Add(TableRegion.BottomLeftCell);
        if (lastRow && lastCol) list.Add(TableRegion.BottomRightCell);
        return list;
    }

    /// <summary>Row/column bounds of the region containing the cell, for mapping a region's borders onto its edges.</summary>
    private static (int R0, int R1, int C0, int C1) RegionBounds(TableRegion region, TableCellRef c, int lastRow, int lastCol) => region switch
    {
        TableRegion.WholeTable => (0, lastRow, 0, lastCol),
        TableRegion.FirstRow or TableRegion.LastRow or TableRegion.OddRowBand or TableRegion.EvenRowBand => (c.Row, c.Row, 0, lastCol),
        TableRegion.FirstColumn or TableRegion.LastColumn or TableRegion.OddColumnBand or TableRegion.EvenColumnBand => (0, lastRow, c.Column, c.Column),
        _ => (c.Row, c.Row, c.Column, c.Column), // corner cells
    };

    /// <summary>Maps a region's table-style borders onto one cell's edges: outer edges on the region boundary, inside lines elsewhere.</summary>
    private static void ApplyRegionBorders(CellBorders edges, TableBorders? b, int row, int col, int span, int r0, int r1, int c0, int c1)
    {
        if (b is null) return;
        int lastColOfCell = col + span - 1;
        var top = row <= r0 ? b.Top : b.InsideHorizontal;
        var bottom = row >= r1 ? b.Bottom : b.InsideHorizontal;
        var left = col <= c0 ? b.Left : b.InsideVertical;
        var right = lastColOfCell >= c1 ? b.Right : b.InsideVertical;
        edges.Top = top ?? edges.Top;
        edges.Bottom = bottom ?? edges.Bottom;
        edges.Left = left ?? edges.Left;
        edges.Right = right ?? edges.Right;
    }

    private static TableBorders MergeBorders(TableBorders? under, TableBorders over) => new()
    {
        Top = over.Top ?? under?.Top, Left = over.Left ?? under?.Left, Bottom = over.Bottom ?? under?.Bottom,
        Right = over.Right ?? under?.Right, InsideHorizontal = over.InsideHorizontal ?? under?.InsideHorizontal,
        InsideVertical = over.InsideVertical ?? under?.InsideVertical,
    };

    private static CellMargins MergeMargins(CellMargins? under, CellMargins over) => new()
    {
        TopPt = over.TopPt ?? under?.TopPt, LeftPt = over.LeftPt ?? under?.LeftPt,
        BottomPt = over.BottomPt ?? under?.BottomPt, RightPt = over.RightPt ?? under?.RightPt,
    };

    /// <summary>The paragraph's style id, falling back to the default paragraph style when it does not exist.</summary>
    internal static string? ParagraphStyleId(Document doc, Paragraph p)
    {
        var id = p.StyleId;
        if (doc.Styles.TryGet(id, out var s) && s.Type == StyleType.Paragraph) return id;
        return doc.Styles.GetDefault(StyleType.Paragraph)?.StyleId;
    }

    /// <summary>The style and its basedOn ancestors, ROOT FIRST. Stops at missing styles, cycles and a depth cap.</summary>
    internal static List<StyleDefinition> Chain(Document doc, string? styleId, StyleType type)
    {
        var chain = new List<StyleDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var id = styleId;
        while (id is not null && chain.Count < MaxChainDepth && seen.Add(id) && doc.Styles.TryGet(id, out var s))
        {
            if (s.Type != type) break;
            chain.Add(s);
            id = s.BasedOn;
        }
        chain.Reverse();
        return chain;
    }

    private static RunBag ChainRunBag(Document doc, string? styleId, StyleType type)
    {
        var bag = new RunBag();
        foreach (var s in Chain(doc, styleId, type)) bag.Overlay(RunBag.From(s.RunFormat));
        return bag;
    }

    /// <summary>Every run property as "unset (null) or value", so levels can be overlaid.</summary>
    internal sealed class RunBag
    {
        // Toggles (§17.7.3).
        public bool? Bold, BoldCs, Italic, ItalicCs, Caps, SmallCaps, Strike, DStrike, Outline, Shadow, Emboss, Imprint, Hidden;
        // Everything else overrides.
        public string? Ascii, HAnsi, EastAsia, Cs, Color, Highlight, UnderlineColor, Lang, LangEa, LangBidi;
        public ThemeFont? AsciiTheme, HAnsiTheme, EaTheme, CsTheme;
        public FontHint? Hint;
        public double? Size, SizeCs, Spacing, Kern, Position;
        public bool? CsFlag, Rtl, NoProof;
        public UnderlineStyle? Underline;
        public VerticalTextPosition? VertAlign;
        public int? Scale;
        public ThemeColor? ThemeColor;
        public byte? Tint, Shade;
        public Shading? Shading;

        /// <summary>A level's values, with the writer's own complex-script mirroring applied so what is resolved is what Word reads.</summary>
        public static RunBag From(TextFormat? f)
        {
            var b = new RunBag();
            if (f is null) return b;
            bool mirror = f.MirrorToComplexScript;
            var off = f.ExplicitlyOff;
            b.Bold = f.Bold ? true : off.HasFlag(RunToggles.Bold) ? false : null;
            b.BoldCs = f.BoldComplexScript ?? (mirror && f.Bold ? true : null);
            b.Italic = f.Italic ? true : off.HasFlag(RunToggles.Italic) ? false : null;
            b.ItalicCs = f.ItalicComplexScript ?? (mirror && f.Italic ? true : null);
            b.Strike = f.Strikethrough ? true : off.HasFlag(RunToggles.Strikethrough) ? false : null;
            b.Caps = f.Caps; b.SmallCaps = f.SmallCaps; b.DStrike = f.DoubleStrikethrough;
            b.Outline = f.Outline; b.Shadow = f.Shadow; b.Emboss = f.Emboss; b.Imprint = f.Imprint; b.Hidden = f.Hidden;
            b.Ascii = f.AsciiFont; b.HAnsi = f.HighAnsiFont; b.EastAsia = f.EastAsiaFont; b.Cs = f.ComplexScriptFont;
            b.AsciiTheme = f.AsciiThemeFont; b.HAnsiTheme = f.HighAnsiThemeFont; b.EaTheme = f.EastAsiaThemeFont; b.CsTheme = f.ComplexScriptThemeFont;
            b.Hint = f.FontHint;
            b.Size = f.SizePt > 0 ? f.SizePt : null;
            b.SizeCs = f.SizeComplexScriptPt > 0 ? f.SizeComplexScriptPt : (mirror && f.SizePt > 0 ? f.SizePt : null);
            b.Color = f.ColorHex; b.ThemeColor = f.ThemeColor; b.Tint = f.ThemeColorTint; b.Shade = f.ThemeColorShade;
            b.Highlight = f.Highlight;
            b.Underline = f.EffectiveUnderline; b.UnderlineColor = f.UnderlineColorHex;
            b.Shading = f.Shading ?? (f.ShadingHex is { } hex ? Word.Shading.Fill(hex) : null);
            b.VertAlign = f.VerticalPosition; b.Position = f.BaselineShiftPt; b.Spacing = f.CharacterSpacingPt;
            b.Scale = f.HorizontalScalePercent; b.Kern = f.KerningMinSizePt;
            b.CsFlag = f.ComplexScript; b.Rtl = f.RightToLeft; b.NoProof = f.NoProof;
            b.Lang = f.Language; b.LangEa = f.EastAsiaLanguage; b.LangBidi = f.ComplexScriptLanguage;
            return b;
        }

        /// <summary>Overlays every SET value of <paramref name="top"/> onto this bag.</summary>
        public void Overlay(RunBag top)
        {
            Bold = top.Bold ?? Bold; BoldCs = top.BoldCs ?? BoldCs; Italic = top.Italic ?? Italic; ItalicCs = top.ItalicCs ?? ItalicCs;
            Caps = top.Caps ?? Caps; SmallCaps = top.SmallCaps ?? SmallCaps; Strike = top.Strike ?? Strike; DStrike = top.DStrike ?? DStrike;
            Outline = top.Outline ?? Outline; Shadow = top.Shadow ?? Shadow; Emboss = top.Emboss ?? Emboss; Imprint = top.Imprint ?? Imprint;
            Hidden = top.Hidden ?? Hidden;
            OverlayNonToggles(top);
        }

        private void OverlayNonToggles(RunBag top)
        {
            Ascii = top.Ascii ?? Ascii; HAnsi = top.HAnsi ?? HAnsi; EastAsia = top.EastAsia ?? EastAsia; Cs = top.Cs ?? Cs;
            AsciiTheme = top.AsciiTheme ?? AsciiTheme; HAnsiTheme = top.HAnsiTheme ?? HAnsiTheme;
            EaTheme = top.EaTheme ?? EaTheme; CsTheme = top.CsTheme ?? CsTheme; Hint = top.Hint ?? Hint;
            Size = top.Size ?? Size; SizeCs = top.SizeCs ?? SizeCs;
            Color = top.Color ?? Color; ThemeColor = top.ThemeColor ?? ThemeColor; Tint = top.Tint ?? Tint; Shade = top.Shade ?? Shade;
            Highlight = top.Highlight ?? Highlight; Underline = top.Underline ?? Underline; UnderlineColor = top.UnderlineColor ?? UnderlineColor;
            Shading = top.Shading ?? Shading; VertAlign = top.VertAlign ?? VertAlign; Position = top.Position ?? Position;
            Spacing = top.Spacing ?? Spacing; Scale = top.Scale ?? Scale; Kern = top.Kern ?? Kern;
            CsFlag = top.CsFlag ?? CsFlag; Rtl = top.Rtl ?? Rtl; NoProof = top.NoProof ?? NoProof;
            Lang = top.Lang ?? Lang; LangEa = top.LangEa ?? LangEa; LangBidi = top.LangBidi ?? LangBidi;
        }

        /// <summary>Paragraph-style level ⊕ character-style level: toggles XOR when both are set; other properties: character style wins.</summary>
        public static RunBag CombineStyleLevels(RunBag paragraphStyle, RunBag characterStyle)
        {
            static bool? X(bool? a, bool? b) => a is null ? b : b is null ? a : a.Value ^ b.Value;
            var r = new RunBag
            {
                Bold = X(paragraphStyle.Bold, characterStyle.Bold),
                BoldCs = X(paragraphStyle.BoldCs, characterStyle.BoldCs),
                Italic = X(paragraphStyle.Italic, characterStyle.Italic),
                ItalicCs = X(paragraphStyle.ItalicCs, characterStyle.ItalicCs),
                Caps = X(paragraphStyle.Caps, characterStyle.Caps),
                SmallCaps = X(paragraphStyle.SmallCaps, characterStyle.SmallCaps),
                Strike = X(paragraphStyle.Strike, characterStyle.Strike),
                DStrike = X(paragraphStyle.DStrike, characterStyle.DStrike),
                Outline = X(paragraphStyle.Outline, characterStyle.Outline),
                Shadow = X(paragraphStyle.Shadow, characterStyle.Shadow),
                Emboss = X(paragraphStyle.Emboss, characterStyle.Emboss),
                Imprint = X(paragraphStyle.Imprint, characterStyle.Imprint),
                Hidden = X(paragraphStyle.Hidden, characterStyle.Hidden),
            };
            r.OverlayNonToggles(paragraphStyle);
            r.OverlayNonToggles(characterStyle);
            return r;
        }

        public TextFormat ToFormat()
        {
            var off = RunToggles.None;
            if (Bold == false) off |= RunToggles.Bold;
            if (Italic == false) off |= RunToggles.Italic;
            if (Strike == false) off |= RunToggles.Strikethrough;
            bool plainFill = Shading is { } sh && FormattingXml.IsPlainFill(sh);
            return new TextFormat
            {
                MirrorToComplexScript = false,
                Bold = Bold == true, Italic = Italic == true, Strikethrough = Strike == true,
                Underline = Underline == UnderlineStyle.Single && UnderlineColor is null,
                UnderlineStyle = Underline == UnderlineStyle.Single && UnderlineColor is null ? null : Underline,
                UnderlineColorHex = UnderlineColor,
                ExplicitlyOff = off,
                BoldComplexScript = BoldCs, ItalicComplexScript = ItalicCs,
                Caps = Caps, SmallCaps = SmallCaps, DoubleStrikethrough = DStrike,
                Outline = Outline, Shadow = Shadow, Emboss = Emboss, Imprint = Imprint, Hidden = Hidden,
                AsciiFont = Ascii, HighAnsiFont = HAnsi, EastAsiaFont = EastAsia, ComplexScriptFont = Cs,
                AsciiThemeFont = AsciiTheme, HighAnsiThemeFont = HAnsiTheme, EastAsiaThemeFont = EaTheme, ComplexScriptThemeFont = CsTheme,
                FontHint = Hint,
                SizePt = Size ?? 0, SizeComplexScriptPt = SizeCs ?? 0,
                ColorHex = Color, ThemeColor = ThemeColor, ThemeColorTint = Tint, ThemeColorShade = Shade,
                Highlight = Highlight,
                ShadingHex = plainFill ? Shading!.FillHex : null,
                Shading = plainFill ? null : Shading,
                VerticalPosition = VertAlign, BaselineShiftPt = Position, CharacterSpacingPt = Spacing,
                HorizontalScalePercent = Scale, KerningMinSizePt = Kern,
                ComplexScript = CsFlag, RightToLeft = Rtl, NoProof = NoProof,
                Language = Lang, EastAsiaLanguage = LangEa, ComplexScriptLanguage = LangBidi,
            };
        }
    }
}
