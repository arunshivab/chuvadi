using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Docs.Model;

/// <summary>
/// The document's compatibility settings (<c>w:settings/w:compat</c>): the compatibility mode Word lays the
/// document out in, the 65 legacy layout options of ECMA-376 <c>CT_Compat</c>, and any other
/// <c>w:compatSetting</c> entries (D-296). <see cref="Document.Load(string)"/> fills it from the file and
/// <see cref="Document.SaveTo(string)"/> writes it back, so a load → save keeps the layout rules the file
/// was made with.
/// <para>A new <see cref="Document"/> starts in compatibility mode 15 (<see cref="Mode"/>), Word 2013 and
/// later, so Word opens it without "[Compatibility Mode]". A loaded document keeps the file's mode, or none
/// when the file states none.</para>
/// <para>Each flag is named after its XML element, with the first letter in upper case.</para>
/// </summary>
public sealed class CompatibilityOptions
{
    /// <summary>The CT_Compat flag elements in schema order; the index is the flag's slot.</summary>
    internal static readonly string[] FlagElements =
    {
        "useSingleBorderforContiguousCells",
        "wpJustification",
        "noTabHangInd",
        "noLeading",
        "spaceForUL",
        "noColumnBalance",
        "balanceSingleByteDoubleByteWidth",
        "noExtraLineSpacing",
        "doNotLeaveBackslashAlone",
        "ulTrailSpace",
        "doNotExpandShiftReturn",
        "spacingInWholePoints",
        "lineWrapLikeWord6",
        "printBodyTextBeforeHeader",
        "printColBlack",
        "wpSpaceWidth",
        "showBreaksInFrames",
        "subFontBySize",
        "suppressBottomSpacing",
        "suppressTopSpacing",
        "suppressSpacingAtTopOfPage",
        "suppressTopSpacingWP",
        "suppressSpBfAfterPgBrk",
        "swapBordersFacingPages",
        "convMailMergeEsc",
        "truncateFontHeightsLikeWP6",
        "mwSmallCaps",
        "usePrinterMetrics",
        "doNotSuppressParagraphBorders",
        "wrapTrailSpaces",
        "footnoteLayoutLikeWW8",
        "shapeLayoutLikeWW8",
        "alignTablesRowByRow",
        "forgetLastTabAlignment",
        "adjustLineHeightInTable",
        "autoSpaceLikeWord95",
        "noSpaceRaiseLower",
        "doNotUseHTMLParagraphAutoSpacing",
        "layoutRawTableWidth",
        "layoutTableRowsApart",
        "useWord97LineBreakRules",
        "doNotBreakWrappedTables",
        "doNotSnapToGridInCell",
        "selectFldWithFirstOrLastChar",
        "applyBreakingRules",
        "doNotWrapTextWithPunct",
        "doNotUseEastAsianBreakRules",
        "useWord2002TableStyleRules",
        "growAutofit",
        "useFELayout",
        "useNormalStyleForList",
        "doNotUseIndentAsNumberingTabStop",
        "useAltKinsokuLineBreakRules",
        "allowSpaceOfSameStyleInTable",
        "doNotSuppressIndentation",
        "doNotAutofitConstrainedTables",
        "autofitToFirstFixedWidthCell",
        "underlineTabInNumList",
        "displayHangulFixedWidth",
        "splitPgBreakAndParaMark",
        "doNotVertAlignCellWithSp",
        "doNotBreakConstrainedForcedTable",
        "doNotVertAlignInTxbx",
        "useAnsiKerningPairs",
        "cachedColBalance",
    };

    /// <summary>The <c>w:uri</c> Word uses for its own compatibility settings.</summary>
    public const string WordSettingsUri = "http://schemas.microsoft.com/office/word";

    private readonly bool[] _flags = new bool[65];
    private int? _mode;

    /// <summary>
    /// The compatibility mode (<c>w:compatSetting w:name="compatibilityMode"</c>): 11 = Word 2003,
    /// 12 = Word 2007, 14 = Word 2010, 15 = Word 2013 and later. Null = not stated; Word then opens the
    /// file in "[Compatibility Mode]". 15 for a new document.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int? Mode
    {
        get => _mode;
        set
        {
            if (value is int m && m <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), m, "The compatibility mode must be a positive number (Word uses 11, 12, 14 and 15).");
            _mode = value;
        }
    }

    /// <summary>Use a single border for contiguous cells. (<c>w:compat/w:useSingleBorderforContiguousCells</c>)</summary>
    public bool UseSingleBorderforContiguousCells { get => _flags[0]; set => _flags[0] = value; }
    /// <summary>Emulate WordPerfect 6.x paragraph justification. (<c>w:compat/w:wpJustification</c>)</summary>
    public bool WpJustification { get => _flags[1]; set => _flags[1] = value; }
    /// <summary>Do not create a custom tab stop for a hanging indent. (<c>w:compat/w:noTabHangInd</c>)</summary>
    public bool NoTabHangInd { get => _flags[2]; set => _flags[2] = value; }
    /// <summary>Do not add leading (extra space) between lines of text. (<c>w:compat/w:noLeading</c>)</summary>
    public bool NoLeading { get => _flags[3]; set => _flags[3] = value; }
    /// <summary>Add additional space below the baseline for underlined East Asian text. (<c>w:compat/w:spaceForUL</c>)</summary>
    public bool SpaceForUL { get => _flags[4]; set => _flags[4] = value; }
    /// <summary>Do not balance text columns within a section (Word's "Don't balance columns"). (<c>w:compat/w:noColumnBalance</c>)</summary>
    public bool NoColumnBalance { get => _flags[5]; set => _flags[5] = value; }
    /// <summary>Balance single-byte and double-byte characters. (<c>w:compat/w:balanceSingleByteDoubleByteWidth</c>)</summary>
    public bool BalanceSingleByteDoubleByteWidth { get => _flags[6]; set => _flags[6] = value; }
    /// <summary>Do not center content on lines with exact line height. (<c>w:compat/w:noExtraLineSpacing</c>)</summary>
    public bool NoExtraLineSpacing { get => _flags[7]; set => _flags[7] = value; }
    /// <summary>Convert backslash characters into yen signs. (<c>w:compat/w:doNotLeaveBackslashAlone</c>)</summary>
    public bool DoNotLeaveBackslashAlone { get => _flags[8]; set => _flags[8] = value; }
    /// <summary>Underline all trailing spaces. (<c>w:compat/w:ulTrailSpace</c>)</summary>
    public bool UlTrailSpace { get => _flags[9]; set => _flags[9] = value; }
    /// <summary>Do not expand line-break characters in justified paragraphs. (<c>w:compat/w:doNotExpandShiftReturn</c>)</summary>
    public bool DoNotExpandShiftReturn { get => _flags[10]; set => _flags[10] = value; }
    /// <summary>Only expand or condense text by whole points. (<c>w:compat/w:spacingInWholePoints</c>)</summary>
    public bool SpacingInWholePoints { get => _flags[11]; set => _flags[11] = value; }
    /// <summary>Emulate Word 6.0 line wrapping for East Asian text. (<c>w:compat/w:lineWrapLikeWord6</c>)</summary>
    public bool LineWrapLikeWord6 { get => _flags[12]; set => _flags[12] = value; }
    /// <summary>Print body text before header and footer contents. (<c>w:compat/w:printBodyTextBeforeHeader</c>)</summary>
    public bool PrintBodyTextBeforeHeader { get => _flags[13]; set => _flags[13] = value; }
    /// <summary>Print colors as black and white without dithering. (<c>w:compat/w:printColBlack</c>)</summary>
    public bool PrintColBlack { get => _flags[14]; set => _flags[14] = value; }
    /// <summary>Space width like WordPerfect 5.x. (<c>w:compat/w:wpSpaceWidth</c>)</summary>
    public bool WpSpaceWidth { get => _flags[15]; set => _flags[15] = value; }
    /// <summary>Display page and column breaks present in frames. (<c>w:compat/w:showBreaksInFrames</c>)</summary>
    public bool ShowBreaksInFrames { get => _flags[16]; set => _flags[16] = value; }
    /// <summary>Increase the priority of font size during font substitution. (<c>w:compat/w:subFontBySize</c>)</summary>
    public bool SubFontBySize { get => _flags[17]; set => _flags[17] = value; }
    /// <summary>Ignore exact line height for the last line on a page. (<c>w:compat/w:suppressBottomSpacing</c>)</summary>
    public bool SuppressBottomSpacing { get => _flags[18]; set => _flags[18] = value; }
    /// <summary>Ignore minimum and exact line height for the first line on a page. (<c>w:compat/w:suppressTopSpacing</c>)</summary>
    public bool SuppressTopSpacing { get => _flags[19]; set => _flags[19] = value; }
    /// <summary>Ignore minimum line height for the first line on a page. (<c>w:compat/w:suppressSpacingAtTopOfPage</c>)</summary>
    public bool SuppressSpacingAtTopOfPage { get => _flags[20]; set => _flags[20] = value; }
    /// <summary>Emulate WordPerfect 5.x line spacing. (<c>w:compat/w:suppressTopSpacingWP</c>)</summary>
    public bool SuppressTopSpacingWP { get => _flags[21]; set => _flags[21] = value; }
    /// <summary>Do not use space before on the first line after a page break. (<c>w:compat/w:suppressSpBfAfterPgBrk</c>)</summary>
    public bool SuppressSpBfAfterPgBrk { get => _flags[22]; set => _flags[22] = value; }
    /// <summary>Swap paragraph borders on odd-numbered pages. (<c>w:compat/w:swapBordersFacingPages</c>)</summary>
    public bool SwapBordersFacingPages { get => _flags[23]; set => _flags[23] = value; }
    /// <summary>Treat backslash-quote as two quotes in mail merge data sources. (<c>w:compat/w:convMailMergeEsc</c>)</summary>
    public bool ConvMailMergeEsc { get => _flags[24]; set => _flags[24] = value; }
    /// <summary>Emulate WordPerfect 6.x font height calculation. (<c>w:compat/w:truncateFontHeightsLikeWP6</c>)</summary>
    public bool TruncateFontHeightsLikeWP6 { get => _flags[25]; set => _flags[25] = value; }
    /// <summary>Emulate Word 5.x for the Macintosh small caps formatting. (<c>w:compat/w:mwSmallCaps</c>)</summary>
    public bool MwSmallCaps { get => _flags[26]; set => _flags[26] = value; }
    /// <summary>Use printer metrics to display documents. (<c>w:compat/w:usePrinterMetrics</c>)</summary>
    public bool UsePrinterMetrics { get => _flags[27]; set => _flags[27] = value; }
    /// <summary>Do not suppress paragraph borders next to frames. (<c>w:compat/w:doNotSuppressParagraphBorders</c>)</summary>
    public bool DoNotSuppressParagraphBorders { get => _flags[28]; set => _flags[28] = value; }
    /// <summary>Line-wrap trailing spaces. (<c>w:compat/w:wrapTrailSpaces</c>)</summary>
    public bool WrapTrailSpaces { get => _flags[29]; set => _flags[29] = value; }
    /// <summary>Emulate Word 6.x/95/97 footnote placement. (<c>w:compat/w:footnoteLayoutLikeWW8</c>)</summary>
    public bool FootnoteLayoutLikeWW8 { get => _flags[30]; set => _flags[30] = value; }
    /// <summary>Emulate Word 97 text wrapping around floating objects. (<c>w:compat/w:shapeLayoutLikeWW8</c>)</summary>
    public bool ShapeLayoutLikeWW8 { get => _flags[31]; set => _flags[31] = value; }
    /// <summary>Align table rows independently. (<c>w:compat/w:alignTablesRowByRow</c>)</summary>
    public bool AlignTablesRowByRow { get => _flags[32]; set => _flags[32] = value; }
    /// <summary>Ignore width of the last tab stop when aligning a paragraph if it is not left aligned. (<c>w:compat/w:forgetLastTabAlignment</c>)</summary>
    public bool ForgetLastTabAlignment { get => _flags[33]; set => _flags[33] = value; }
    /// <summary>Add document grid spacing to lines in table cells. (<c>w:compat/w:adjustLineHeightInTable</c>)</summary>
    public bool AdjustLineHeightInTable { get => _flags[34]; set => _flags[34] = value; }
    /// <summary>Emulate Word 95 full-width character spacing. (<c>w:compat/w:autoSpaceLikeWord95</c>)</summary>
    public bool AutoSpaceLikeWord95 { get => _flags[35]; set => _flags[35] = value; }
    /// <summary>Do not increase line height for raised or lowered text. (<c>w:compat/w:noSpaceRaiseLower</c>)</summary>
    public bool NoSpaceRaiseLower { get => _flags[36]; set => _flags[36] = value; }
    /// <summary>Use fixed paragraph spacing for HTML auto setting. (<c>w:compat/w:doNotUseHTMLParagraphAutoSpacing</c>)</summary>
    public bool DoNotUseHTMLParagraphAutoSpacing { get => _flags[37]; set => _flags[37] = value; }
    /// <summary>Ignore space before a table when deciding if it wraps a floating object. (<c>w:compat/w:layoutRawTableWidth</c>)</summary>
    public bool LayoutRawTableWidth { get => _flags[38]; set => _flags[38] = value; }
    /// <summary>Allow table rows to wrap inline objects independently. (<c>w:compat/w:layoutTableRowsApart</c>)</summary>
    public bool LayoutTableRowsApart { get => _flags[39]; set => _flags[39] = value; }
    /// <summary>Emulate Word 97 East Asian line breaking. (<c>w:compat/w:useWord97LineBreakRules</c>)</summary>
    public bool UseWord97LineBreakRules { get => _flags[40]; set => _flags[40] = value; }
    /// <summary>Do not allow floating tables to break across pages. (<c>w:compat/w:doNotBreakWrappedTables</c>)</summary>
    public bool DoNotBreakWrappedTables { get => _flags[41]; set => _flags[41] = value; }
    /// <summary>Do not snap to the document grid in table cells with objects. (<c>w:compat/w:doNotSnapToGridInCell</c>)</summary>
    public bool DoNotSnapToGridInCell { get => _flags[42]; set => _flags[42] = value; }
    /// <summary>Select the whole field with its first or last character. (<c>w:compat/w:selectFldWithFirstOrLastChar</c>)</summary>
    public bool SelectFldWithFirstOrLastChar { get => _flags[43]; set => _flags[43] = value; }
    /// <summary>Use legacy Ethiopic and Amharic line-breaking rules. (<c>w:compat/w:applyBreakingRules</c>)</summary>
    public bool ApplyBreakingRules { get => _flags[44]; set => _flags[44] = value; }
    /// <summary>Do not allow hanging punctuation with the character grid. (<c>w:compat/w:doNotWrapTextWithPunct</c>)</summary>
    public bool DoNotWrapTextWithPunct { get => _flags[45]; set => _flags[45] = value; }
    /// <summary>Do not compress compressible characters with the character grid. (<c>w:compat/w:doNotUseEastAsianBreakRules</c>)</summary>
    public bool DoNotUseEastAsianBreakRules { get => _flags[46]; set => _flags[46] = value; }
    /// <summary>Emulate Word 2002 table style rules. (<c>w:compat/w:useWord2002TableStyleRules</c>)</summary>
    public bool UseWord2002TableStyleRules { get => _flags[47]; set => _flags[47] = value; }
    /// <summary>Allow tables to autofit into the page margins. (<c>w:compat/w:growAutofit</c>)</summary>
    public bool GrowAutofit { get => _flags[48]; set => _flags[48] = value; }
    /// <summary>Do not bypass East Asian and complex-script layout code. (<c>w:compat/w:useFELayout</c>)</summary>
    public bool UseFELayout { get => _flags[49]; set => _flags[49] = value; }
    /// <summary>Do not automatically apply the List Paragraph style to bulleted or numbered text. (<c>w:compat/w:useNormalStyleForList</c>)</summary>
    public bool UseNormalStyleForList { get => _flags[50]; set => _flags[50] = value; }
    /// <summary>Ignore the hanging indent when creating a tab stop after numbering. (<c>w:compat/w:doNotUseIndentAsNumberingTabStop</c>)</summary>
    public bool DoNotUseIndentAsNumberingTabStop { get => _flags[51]; set => _flags[51] = value; }
    /// <summary>Use alternate East Asian line-breaking rules. (<c>w:compat/w:useAltKinsokuLineBreakRules</c>)</summary>
    public bool UseAltKinsokuLineBreakRules { get => _flags[52]; set => _flags[52] = value; }
    /// <summary>Allow contextual spacing of paragraphs in tables. (<c>w:compat/w:allowSpaceOfSameStyleInTable</c>)</summary>
    public bool AllowSpaceOfSameStyleInTable { get => _flags[53]; set => _flags[53] = value; }
    /// <summary>Do not ignore floating objects when calculating paragraph indentation. (<c>w:compat/w:doNotSuppressIndentation</c>)</summary>
    public bool DoNotSuppressIndentation { get => _flags[54]; set => _flags[54] = value; }
    /// <summary>Do not autofit tables to fit next to wrapped objects. (<c>w:compat/w:doNotAutofitConstrainedTables</c>)</summary>
    public bool DoNotAutofitConstrainedTables { get => _flags[55]; set => _flags[55] = value; }
    /// <summary>Allow table columns to exceed preferred widths of constituent cells. (<c>w:compat/w:autofitToFirstFixedWidthCell</c>)</summary>
    public bool AutofitToFirstFixedWidthCell { get => _flags[56]; set => _flags[56] = value; }
    /// <summary>Underline the following character after a numbering tab. (<c>w:compat/w:underlineTabInNumList</c>)</summary>
    public bool UnderlineTabInNumList { get => _flags[57]; set => _flags[57] = value; }
    /// <summary>Always use fixed width for Hangul characters. (<c>w:compat/w:displayHangulFixedWidth</c>)</summary>
    public bool DisplayHangulFixedWidth { get => _flags[58]; set => _flags[58] = value; }
    /// <summary>Always move the paragraph mark to the page after a page break. (<c>w:compat/w:splitPgBreakAndParaMark</c>)</summary>
    public bool SplitPgBreakAndParaMark { get => _flags[59]; set => _flags[59] = value; }
    /// <summary>Do not vertically align cells containing floating objects. (<c>w:compat/w:doNotVertAlignCellWithSp</c>)</summary>
    public bool DoNotVertAlignCellWithSp { get => _flags[60]; set => _flags[60] = value; }
    /// <summary>Do not break table rows around floating tables. (<c>w:compat/w:doNotBreakConstrainedForcedTable</c>)</summary>
    public bool DoNotBreakConstrainedForcedTable { get => _flags[61]; set => _flags[61] = value; }
    /// <summary>Ignore vertical alignment in text boxes. (<c>w:compat/w:doNotVertAlignInTxbx</c>)</summary>
    public bool DoNotVertAlignInTxbx { get => _flags[62]; set => _flags[62] = value; }
    /// <summary>Use ANSI kerning pairs from fonts. (<c>w:compat/w:useAnsiKerningPairs</c>)</summary>
    public bool UseAnsiKerningPairs { get => _flags[63]; set => _flags[63] = value; }
    /// <summary>Use cached paragraph information for column balancing. (<c>w:compat/w:cachedColBalance</c>)</summary>
    public bool CachedColBalance { get => _flags[64]; set => _flags[64] = value; }

    /// <summary>
    /// The other <c>w:compatSetting</c> entries, in file order, kept as found (for example Word's
    /// <c>overrideTableStyleFontSizeAndJustification</c>, <c>enableOpenTypeFeatures</c> and
    /// <c>doNotFlipMirrorIndents</c>). The compatibility mode itself is <see cref="Mode"/>, not here.
    /// </summary>
    public IList<CompatibilitySetting> OtherSettings { get; } = new List<CompatibilitySetting>();

    internal bool GetFlag(int index) => _flags[index];
    internal void SetFlag(int index, bool value) => _flags[index] = value;

    internal bool IsEmpty
    {
        get
        {
            if (_mode is not null || OtherSettings.Count > 0) return false;
            foreach (var f in _flags) if (f) return false;
            return true;
        }
    }
}

/// <summary>One <c>w:compatSetting</c>: a named compatibility setting from the application named by <see cref="Uri"/>.</summary>
public sealed class CompatibilitySetting
{
    /// <summary>Creates a compatibility setting.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public CompatibilitySetting(string name, string uri, string value)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The setting's name (<c>w:name</c>).</summary>
    public string Name { get; }

    /// <summary>The namespace of the application that defines it (<c>w:uri</c>).</summary>
    public string Uri { get; }

    /// <summary>The setting's value (<c>w:val</c>).</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name}={Value}");
}

/// <summary>
/// The languages that pick a theme's script-specific fonts (<c>w:settings/w:themeFontLang</c>, ECMA-376
/// §17.15.1.88): see <see cref="Document.GetThemeFontName(ThemeFont)"/>. Each is a language tag such as
/// "en-US", "ja-JP" or "ta-IN"; null = not stated.
/// </summary>
public sealed class ThemeFontLanguages
{
    /// <summary>The language for the Latin theme fonts (<c>w:val</c>).</summary>
    public string? Latin { get; set; }

    /// <summary>The language for the East Asian theme fonts (<c>w:eastAsia</c>).</summary>
    public string? EastAsia { get; set; }

    /// <summary>The language for the complex-script theme fonts (<c>w:bidi</c>).</summary>
    public string? ComplexScript { get; set; }

    internal bool IsEmpty => Latin is null && EastAsia is null && ComplexScript is null;
}

/// <summary>Character compression for East Asian text (<c>w:settings/w:characterSpacingControl</c>).</summary>
public enum CharacterSpacingControl
{
    /// <summary>No compression (<c>doNotCompress</c>).</summary>
    DoNotCompress,

    /// <summary>Compress punctuation (<c>compressPunctuation</c>).</summary>
    CompressPunctuation,

    /// <summary>Compress punctuation and Japanese kana (<c>compressPunctuationAndJapaneseKana</c>).</summary>
    CompressPunctuationAndJapaneseKana,
}

/// <summary>A slot of the theme's colour scheme, the target of a <see cref="ColorSchemeMapping"/> entry (ST_ColorSchemeIndex).</summary>
public enum ColorSchemeIndex
{
    /// <summary><c>dark1</c></summary>
    Dark1,
    /// <summary><c>light1</c></summary>
    Light1,
    /// <summary><c>dark2</c></summary>
    Dark2,
    /// <summary><c>light2</c></summary>
    Light2,
    /// <summary><c>accent1</c></summary>
    Accent1,
    /// <summary><c>accent2</c></summary>
    Accent2,
    /// <summary><c>accent3</c></summary>
    Accent3,
    /// <summary><c>accent4</c></summary>
    Accent4,
    /// <summary><c>accent5</c></summary>
    Accent5,
    /// <summary><c>accent6</c></summary>
    Accent6,
    /// <summary><c>hyperlink</c></summary>
    Hyperlink,
    /// <summary><c>followedHyperlink</c></summary>
    FollowedHyperlink,
}

/// <summary>
/// Which theme colour-scheme slot each logical colour uses (<c>w:settings/w:clrSchemeMapping</c>). Null =
/// not stated; <see cref="Document.GetThemeColorHex(ThemeColor, byte?, byte?)"/> then uses Word's default
/// mapping (background 1 → light 1, text 1 → dark 1, background 2 → light 2, text 2 → dark 2, and each
/// accent and hyperlink colour to itself).
/// </summary>
public sealed class ColorSchemeMapping
{
    /// <summary><c>w:bg1</c></summary>
    public ColorSchemeIndex? Background1 { get; set; }
    /// <summary><c>w:t1</c></summary>
    public ColorSchemeIndex? Text1 { get; set; }
    /// <summary><c>w:bg2</c></summary>
    public ColorSchemeIndex? Background2 { get; set; }
    /// <summary><c>w:t2</c></summary>
    public ColorSchemeIndex? Text2 { get; set; }
    /// <summary><c>w:accent1</c></summary>
    public ColorSchemeIndex? Accent1 { get; set; }
    /// <summary><c>w:accent2</c></summary>
    public ColorSchemeIndex? Accent2 { get; set; }
    /// <summary><c>w:accent3</c></summary>
    public ColorSchemeIndex? Accent3 { get; set; }
    /// <summary><c>w:accent4</c></summary>
    public ColorSchemeIndex? Accent4 { get; set; }
    /// <summary><c>w:accent5</c></summary>
    public ColorSchemeIndex? Accent5 { get; set; }
    /// <summary><c>w:accent6</c></summary>
    public ColorSchemeIndex? Accent6 { get; set; }
    /// <summary><c>w:hyperlink</c></summary>
    public ColorSchemeIndex? Hyperlink { get; set; }
    /// <summary><c>w:followedHyperlink</c></summary>
    public ColorSchemeIndex? FollowedHyperlink { get; set; }

    /// <summary>The attribute names in schema order.</summary>
    internal static readonly string[] Attributes =
        { "bg1", "t1", "bg2", "t2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hyperlink", "followedHyperlink" };

    internal ColorSchemeIndex? Get(int i) => i switch
    {
        0 => Background1, 1 => Text1, 2 => Background2, 3 => Text2, 4 => Accent1, 5 => Accent2,
        6 => Accent3, 7 => Accent4, 8 => Accent5, 9 => Accent6, 10 => Hyperlink, _ => FollowedHyperlink,
    };

    internal void Set(int i, ColorSchemeIndex? v)
    {
        switch (i)
        {
            case 0: Background1 = v; break;
            case 1: Text1 = v; break;
            case 2: Background2 = v; break;
            case 3: Text2 = v; break;
            case 4: Accent1 = v; break;
            case 5: Accent2 = v; break;
            case 6: Accent3 = v; break;
            case 7: Accent4 = v; break;
            case 8: Accent5 = v; break;
            case 9: Accent6 = v; break;
            case 10: Hyperlink = v; break;
            default: FollowedHyperlink = v; break;
        }
    }

    internal bool IsEmpty
    {
        get
        {
            for (int i = 0; i < 12; i++) if (Get(i) is not null) return false;
            return true;
        }
    }
}
