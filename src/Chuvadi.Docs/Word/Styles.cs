using System;
using System.Collections;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>The kind of a <see cref="StyleDefinition"/> (ST_StyleType).</summary>
public enum StyleType
{
    /// <summary>Applies to whole paragraphs (paragraph and run properties).</summary>
    Paragraph,
    /// <summary>Applies to runs of text (run properties only).</summary>
    Character,
    /// <summary>Applies to tables.</summary>
    Table,
    /// <summary>A numbering (list) style.</summary>
    Numbering,
}

/// <summary>
/// A named style (w:style in styles.xml): paragraph, character, table or numbering. Paragraph
/// and table styles carry a <see cref="ParagraphFormat"/> and a run format; character styles a
/// run format. Styles inherit through <see cref="BasedOn"/>.
///
/// <code>
/// var note = doc.Styles.AddParagraphStyle("ClinicalNote", "Clinical Note");
/// note.ParagraphFormat.SpaceAfterPt = 4;
/// note.ParagraphFormat.IndentLeftPt = 18;
/// note.RunFormat = new TextFormat { Italic = true, ColorHex = "404040" };
/// doc.AddParagraph("Patient tolerated the procedure well.").StyleId = "ClinicalNote";
///
/// var drug = doc.Styles.AddCharacterStyle("DrugName", "Drug Name");
/// drug.RunFormat = new TextFormat { Bold = true, ColorHex = "1F3864" };
/// doc.AddParagraph().Text("Give ").StyledText("Clopidogrel", "DrugName").Text(" 75 mg daily.");
/// </code>
/// </summary>
public sealed class StyleDefinition
{
    /// <summary>Creates a style. <paramref name="styleId"/> is the identifier documents refer to (no spaces by convention).</summary>
    public StyleDefinition(string styleId, StyleType type)
    {
        if (string.IsNullOrWhiteSpace(styleId)) throw new ArgumentException("Style id required.", nameof(styleId));
        StyleId = styleId;
        Type = type;
    }

    /// <summary>The identifier paragraphs and runs refer to (w:styleId).</summary>
    public string StyleId { get; }

    /// <summary>Paragraph, character, table or numbering.</summary>
    public StyleType Type { get; }

    /// <summary>Display name shown in Word's style gallery (w:name). Null = the id.</summary>
    public string? Name { get; set; }

    /// <summary>Comma-separated alternative names (w:aliases).</summary>
    public string? Aliases { get; set; }

    /// <summary>The style this one inherits from (w:basedOn). Null = none (inherits document defaults).</summary>
    public string? BasedOn { get; set; }

    /// <summary>Style Word applies to the next paragraph after Enter (w:next).</summary>
    public string? Next { get; set; }

    /// <summary>Linked paragraph ↔ character style pair (w:link).</summary>
    public string? Link { get; set; }

    /// <summary>The default style of its type, e.g. Normal for paragraphs (w:default).</summary>
    public bool IsDefault { get; set; }

    /// <summary>A user-defined (not built-in) style (w:customStyle).</summary>
    public bool IsCustom { get; set; }

    /// <summary>Show in the Quick Styles gallery (w:qFormat).</summary>
    public bool QuickFormat { get; set; }

    /// <summary>Sort order in Word's style lists; lower first (w:uiPriority).</summary>
    public int? UiPriority { get; set; }

    /// <summary>Hidden from the user interface (w:hidden).</summary>
    public bool Hidden { get; set; }

    /// <summary>Hidden until used (w:semiHidden).</summary>
    public bool SemiHidden { get; set; }

    /// <summary>Un-hide when first used (w:unhideWhenUsed).</summary>
    public bool UnhideWhenUsed { get; set; }

    /// <summary>Cannot be applied when formatting restrictions are on (w:locked).</summary>
    public bool Locked { get; set; }

    /// <summary>Redefine the style from direct formatting automatically (w:autoRedefine).</summary>
    public bool AutoRedefine { get; set; }

    /// <summary>Paragraph properties (paragraph and table styles; ignored for character styles).</summary>
    public ParagraphFormat ParagraphFormat { get; } = new();

    /// <summary>Run properties applied by this style. Null = none.</summary>
    public TextFormat? RunFormat { get; set; }

    /// <summary>A style's numbering reference (w:pPr/w:numPr) carried verbatim from a loaded file until lists are modelled.</summary>
    internal string? RawNumberingXml { get; set; }

    // ---- Table styles ---------------------------------------------------------------------

    /// <summary>Table-level formatting of a table style (w:tblPr): borders, cell margins, alignment, indent, banding sizes…</summary>
    public TableFormat TableFormat { get; } = new();

    /// <summary>Row formatting of a table style (w:trPr).</summary>
    public TableRowFormat RowFormat { get; } = new();

    /// <summary>Cell formatting of a table style (w:tcPr): shading, borders, margins, vertical alignment…</summary>
    public TableCellFormat CellFormat { get; } = new();

    private readonly SortedDictionary<TableRegion, TableConditionalFormat> _conditional = new();

    /// <summary>Region-specific formatting of a table style (w:tblStylePr), applied in <see cref="TableRegion"/> order.</summary>
    public IReadOnlyDictionary<TableRegion, TableConditionalFormat> ConditionalFormats => _conditional;

    /// <summary>The conditional format for <paramref name="region"/>, created when absent.</summary>
    public TableConditionalFormat Conditional(TableRegion region)
    {
        if (!_conditional.TryGetValue(region, out var f)) _conditional[region] = f = new TableConditionalFormat();
        return f;
    }

    /// <summary>Removes a region's conditional format.</summary>
    public bool RemoveConditional(TableRegion region) => _conditional.Remove(region);

    /// <summary>A deep copy under a new id.</summary>
    public StyleDefinition CloneAs(string newStyleId)
    {
        var c = new StyleDefinition(newStyleId, Type)
        {
            Name = Name, Aliases = Aliases, BasedOn = BasedOn, Next = Next, Link = Link,
            IsDefault = false, IsCustom = true, QuickFormat = QuickFormat, UiPriority = UiPriority,
            Hidden = Hidden, SemiHidden = SemiHidden, UnhideWhenUsed = UnhideWhenUsed,
            Locked = Locked, AutoRedefine = AutoRedefine, RunFormat = RunFormat,
        };
        c.ParagraphFormat.ApplyFrom(ParagraphFormat);
        c.RawNumberingXml = RawNumberingXml;
        Internal.TableXml.CopyTableFormat(TableFormat, c.TableFormat);
        Internal.TableXml.CopyRowFormat(RowFormat, c.RowFormat);
        c.CellFormat.ApplyFrom(CellFormat);
        foreach (var (region, cf) in _conditional)
        {
            var d = c.Conditional(region);
            d.ParagraphFormat.ApplyFrom(cf.ParagraphFormat);
            d.RunFormat = cf.RunFormat;
            Internal.TableXml.CopyTableFormat(cf.TableFormat, d.TableFormat);
            Internal.TableXml.CopyRowFormat(cf.RowFormat, d.RowFormat);
            d.CellFormat.ApplyFrom(cf.CellFormat);
        }
        return c;
    }

    public override string ToString() => $"{Type} '{StyleId}'";
}

/// <summary>
/// The styles of a document, keyed by style id (ordinal, case-sensitive, as Word compares
/// them). A new <see cref="Document"/> starts with the built-in set (Normal, Title,
/// Heading 1–9, Quote, List Paragraph, Hyperlink), which can be modified or replaced.
/// <see cref="Document.Load(string)"/> fills it from the file's styles.xml.
/// </summary>
public sealed class StyleCollection : IEnumerable<StyleDefinition>
{
    private readonly List<StyleDefinition> _order = new();
    private readonly Dictionary<string, StyleDefinition> _byId = new(StringComparer.Ordinal);

    /// <summary>Number of styles.</summary>
    public int Count => _order.Count;

    /// <summary>The style with <paramref name="styleId"/>; throws <see cref="KeyNotFoundException"/> when absent.</summary>
    public StyleDefinition this[string styleId] => _byId[styleId];

    /// <summary>True when a style with this id exists.</summary>
    public bool Contains(string styleId) => _byId.ContainsKey(styleId);

    /// <summary>Looks up a style by id.</summary>
    public bool TryGet(string styleId, out StyleDefinition style)
    {
        if (styleId is not null && _byId.TryGetValue(styleId, out var s)) { style = s; return true; }
        style = null!;
        return false;
    }

    /// <summary>Adds a style; throws when the id is already used.</summary>
    public StyleDefinition Add(StyleDefinition style)
    {
        if (style is null) throw new ArgumentNullException(nameof(style));
        if (_byId.ContainsKey(style.StyleId))
            throw new ArgumentException($"A style with id '{style.StyleId}' already exists.", nameof(style));
        _byId[style.StyleId] = style;
        _order.Add(style);
        return style;
    }

    /// <summary>Adds the style, or replaces the existing one with the same id (keeping its position).</summary>
    public StyleDefinition AddOrReplace(StyleDefinition style)
    {
        if (style is null) throw new ArgumentNullException(nameof(style));
        if (_byId.TryGetValue(style.StyleId, out var existing))
            _order[_order.IndexOf(existing)] = style;
        else
            _order.Add(style);
        _byId[style.StyleId] = style;
        return style;
    }

    /// <summary>Removes a style. Paragraphs still referring to it fall back to the default style in Word.</summary>
    public bool Remove(string styleId)
    {
        if (!_byId.Remove(styleId, out var s)) return false;
        _order.Remove(s);
        return true;
    }

    /// <summary>Adds a custom paragraph style based on <paramref name="basedOn"/> (default "Normal"), with Next = itself.</summary>
    public StyleDefinition AddParagraphStyle(string styleId, string? name = null, string? basedOn = "Normal")
        => Add(new StyleDefinition(styleId, StyleType.Paragraph)
        {
            Name = name ?? styleId, BasedOn = basedOn, Next = styleId, IsCustom = true, QuickFormat = true,
        });

    /// <summary>Adds a custom table style (based on <paramref name="basedOn"/>, none by default). Configure <see cref="StyleDefinition.TableFormat"/>, <see cref="StyleDefinition.CellFormat"/> and <see cref="StyleDefinition.Conditional"/>.</summary>
    public StyleDefinition AddTableStyle(string styleId, string? name = null, string? basedOn = null)
        => Add(new StyleDefinition(styleId, StyleType.Table)
        {
            Name = name ?? styleId, BasedOn = basedOn, IsCustom = true, QuickFormat = true,
        });

    /// <summary>Adds a custom character style (based on Default Paragraph Font semantics: no base).</summary>
    public StyleDefinition AddCharacterStyle(string styleId, string? name = null, string? basedOn = null)
        => Add(new StyleDefinition(styleId, StyleType.Character)
        {
            Name = name ?? styleId, BasedOn = basedOn, IsCustom = true, QuickFormat = true,
        });

    /// <summary>The default style of a type (w:default="1"), if any.</summary>
    public StyleDefinition? GetDefault(StyleType type)
    {
        foreach (var s in _order) if (s.Type == type && s.IsDefault) return s;
        return null;
    }

    /// <summary>Removes every style (e.g. before loading a foreign style set).</summary>
    public void Clear()
    {
        _order.Clear();
        _byId.Clear();
    }

    public IEnumerator<StyleDefinition> GetEnumerator() => _order.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Document-wide defaults (w:docDefaults): the base run and paragraph formatting every style
/// inherits. A new document defaults to Calibri 11pt (all scripts' size) with no paragraph
/// spacing.
///
/// <code>
/// doc.Defaults.RunFormat = new TextFormat { FontForAllScripts = "Nirmala UI", SizePt = 10.5, Language = "en-IN" };
/// doc.Defaults.ParagraphFormat.SpaceAfterPt = 6;
/// doc.Defaults.ParagraphFormat.LineSpacing = LineSpacing.Multiple(1.08);
/// </code>
/// </summary>
public sealed class DocumentDefaults
{
    /// <summary>The Calibri 11pt default a new document starts with.</summary>
    public static TextFormat StandardRunFormat => new()
    {
        AsciiFont = "Calibri", HighAnsiFont = "Calibri", ComplexScriptFont = "Calibri", SizePt = 11,
    };

    /// <summary>Default run formatting (w:rPrDefault). Null = none (Word's own fallback: Times New Roman 10pt).</summary>
    public TextFormat? RunFormat { get; set; } = StandardRunFormat;

    /// <summary>Default paragraph formatting (w:pPrDefault).</summary>
    public ParagraphFormat ParagraphFormat { get; } = new();
}
