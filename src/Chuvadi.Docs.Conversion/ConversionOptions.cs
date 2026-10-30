using System;
using System.Collections.Generic;
using Chuvadi.Docs.Word;
using Chuvadi.Markup.Markdown;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Docs.Conversion;

/// <summary>Which text of tracked changes an export shows.</summary>
public enum TrackedChangesView
{
    /// <summary>The text as if every change were accepted (deleted text left out). Default.</summary>
    Final,
    /// <summary>The text as if every change were rejected (inserted text left out, deleted text shown).</summary>
    Original,
    /// <summary>Both: insertions and deletions marked (HTML <c>ins</c>/<c>del</c>; Markdown strikethrough for deletions).</summary>
    Markup,
}

/// <summary>How pictures are written by the Markdown and HTML exporters.</summary>
public enum ImageExportMode
{
    /// <summary>As <c>data:</c> URIs (self-contained output). Default.</summary>
    DataUri,
    /// <summary>Left out (the alternative text is written instead, when there is one).</summary>
    Omit,
}

/// <summary>A picture an exporter is about to write, handed to <see cref="ExportOptions.ImageUrlResolver"/>.</summary>
/// <param name="Image">The picture (bytes, content type, size, alternative text).</param>
/// <param name="Index">1-based position among the pictures of this export.</param>
public sealed record ExportedImage(ImageSpec Image, int Index);

/// <summary>A picture an importer met that is not an inline <c>data:</c> URI, handed to <see cref="ImportOptions.ImageResolver"/>.</summary>
/// <param name="Url">The URL as written in the source.</param>
/// <param name="AltText">The alternative text, if any.</param>
public sealed record ImageRequest(string Url, string? AltText);

/// <summary>Options shared by the text, Markdown and HTML exporters.</summary>
public abstract class ExportOptions
{
    /// <summary>Write headers and footers (headers before the body, footers after it). Default false.</summary>
    public bool IncludeHeadersFooters { get; set; }

    /// <summary>Write footnote and endnote references and the notes after the body. Default true.</summary>
    public bool IncludeNotes { get; set; } = true;

    /// <summary>Write the content of text boxes after the paragraph that holds them. Default true.</summary>
    public bool IncludeTextBoxes { get; set; } = true;

    /// <summary>Write hidden text (w:vanish). Default false.</summary>
    public bool IncludeHiddenText { get; set; }

    /// <summary>Which text of tracked changes to show. Default <see cref="TrackedChangesView.Final"/>.</summary>
    public TrackedChangesView TrackedChanges { get; set; } = TrackedChangesView.Final;

    /// <summary>Paragraph style ids exported as code blocks. Default: Code, SourceCode, HTMLPreformatted, PlainText, CodeBlock.</summary>
    public HashSet<string> CodeStyleIds { get; } = ConversionDefaults.CodeStyleIds();

    /// <summary>Paragraph style ids exported as block quotes. Default: Quote, IntenseQuote, BlockText.</summary>
    public HashSet<string> QuoteStyleIds { get; } = ConversionDefaults.QuoteStyleIds();

    /// <summary>Fonts that mark text as code (inline code, or a code block when a whole paragraph uses them). Case-insensitive.</summary>
    public HashSet<string> MonospaceFonts { get; } = ConversionDefaults.MonospaceFonts();

    /// <summary>Pictures (Markdown and HTML). Default <see cref="ImageExportMode.DataUri"/>.</summary>
    public ImageExportMode Images { get; set; } = ImageExportMode.DataUri;

    /// <summary>
    /// Called for every picture before <see cref="Images"/> applies: return the URL to write (for
    /// example after saving the bytes next to the output), or null to fall back to <see cref="Images"/>.
    /// </summary>
    public Func<ExportedImage, string?>? ImageUrlResolver { get; set; }

    /// <summary>Pictures larger than this many bytes are left out (alternative text only). Null = no limit.</summary>
    public long? MaxImageBytes { get; set; }

    internal Internal.WalkOptions Walk(System.Threading.CancellationToken ct) => new()
    {
        IncludeHeadersFooters = IncludeHeadersFooters,
        IncludeNotes = IncludeNotes,
        IncludeTextBoxes = IncludeTextBoxes,
        IncludeHiddenText = IncludeHiddenText,
        TrackedChanges = TrackedChanges,
        CodeStyleIds = CodeStyleIds,
        QuoteStyleIds = QuoteStyleIds,
        MonospaceFonts = MonospaceFonts,
        Cancellation = ct,
    };
}

/// <summary>Options shared by the Markdown and HTML importers.</summary>
public abstract class ImportOptions
{
    /// <summary>
    /// Supplies the bytes of a picture that is not an inline <c>data:</c> URI (a file next to the
    /// source, a <c>cid:</c> mail part, a URL the host chooses to fetch). Null result or no resolver:
    /// the alternative text is written instead. The importer itself never reads files or the network.
    /// </summary>
    public Func<ImageRequest, byte[]?>? ImageResolver { get; set; }

    /// <summary>Picture types that are imported (others become their alternative text). Default: PNG, JPEG, GIF, BMP, TIFF.</summary>
    public HashSet<string> AllowedImageTypes { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/gif", "image/bmp", "image/tiff",
    };

    /// <summary>Wider pictures are scaled down to this width in points, keeping the aspect ratio. Default 451 pt (A4 text width). Null = actual size.</summary>
    public double? MaxImageWidthPt { get; set; } = 451;

    /// <summary>Pictures larger than this many bytes are not imported. Default 16 MiB.</summary>
    public long MaxImageBytes { get; set; } = 16L * 1024 * 1024;

    /// <summary>Paragraph style id for code blocks; created (monospace, no spacing) when the document lacks it. Default "Code".</summary>
    public string CodeStyleId { get; set; } = "Code";

    /// <summary>Paragraph style id for block quotes. Default "Quote" (built in).</summary>
    public string QuoteStyleId { get; set; } = "Quote";

    /// <summary>Font for inline code and new code styles. Default "Consolas".</summary>
    public string CodeFont { get; set; } = "Consolas";

    /// <summary>Left indent added per nested block-quote level beyond the first, in points. Default 36.</summary>
    public double NestedQuoteIndentPt { get; set; } = 36;

    /// <summary>Text written at the start of a checked task-list item. Default "☒ ".</summary>
    public string TaskCheckedMarker { get; set; } = "☒ ";

    /// <summary>Text written at the start of an unchecked task-list item. Default "☐ ".</summary>
    public string TaskUncheckedMarker { get; set; } = "☐ ";

    /// <summary>Create bookmarks from element ids / anchors so internal links keep working. Default true.</summary>
    public bool CreateBookmarks { get; set; } = true;

    /// <summary>Most table cells created in one import (all tables together); further rows are left out with a warning. Default 100 000.</summary>
    public int MaxTableCells { get; set; } = 100_000;

    /// <summary>Deepest table nesting kept; tables nested deeper become paragraphs (Word and the Docs loader limit XML depth). Default 16.</summary>
    public int MaxTableNesting { get; set; } = 16;
}

/// <summary>How the plain-text importer splits text into paragraphs.</summary>
public enum TextParagraphMode
{
    /// <summary>Every line is a paragraph (empty lines are empty paragraphs). Default.</summary>
    LinePerParagraph,
    /// <summary>Blank lines separate paragraphs; the lines of one paragraph are joined by line breaks.</summary>
    BlankLineSeparated,
    /// <summary>Blank lines separate paragraphs; the lines of one paragraph are joined by a space (re-flowed text).</summary>
    BlankLineSeparatedReflow,
}

/// <summary>Options for <see cref="TextConversion.Import(string, TextImportOptions?, System.Threading.CancellationToken)"/>.</summary>
public sealed class TextImportOptions
{
    /// <summary>How lines become paragraphs. Default <see cref="TextParagraphMode.LinePerParagraph"/>.</summary>
    public TextParagraphMode ParagraphMode { get; set; } = TextParagraphMode.LinePerParagraph;

    /// <summary>Paragraph style for the imported paragraphs. Default "Normal".</summary>
    public string StyleId { get; set; } = "Normal";

    /// <summary>Direct run formatting for the imported text (null = none).</summary>
    public TextFormat? Format { get; set; }

    /// <summary>Tab characters become tab runs (true, default) or stay in the text (false).</summary>
    public bool TabsAsTabRuns { get; set; } = true;

    /// <summary>A form feed (U+000C) becomes a page break. Default true.</summary>
    public bool FormFeedAsPageBreak { get; set; } = true;

    /// <summary>Control characters other than tab, line breaks and form feed are removed (XML cannot hold them). Default true; when false they become U+FFFD.</summary>
    public bool RemoveControlCharacters { get; set; } = true;

    /// <summary>Largest input accepted, in characters. Default 64 Mi.</summary>
    public int MaxInputChars { get; set; } = 64 * 1024 * 1024;
}

/// <summary>
/// Options for <see cref="TextConversion.Export(Document, TextExportOptions?, System.Threading.CancellationToken)"/>.
/// Plain text has no pictures: a picture is written as its alternative text in brackets, and the
/// inherited picture options (<see cref="ExportOptions.Images"/>, <see cref="ExportOptions.ImageUrlResolver"/>,
/// <see cref="ExportOptions.MaxImageBytes"/>) do not apply.
/// </summary>
public sealed class TextExportOptions : ExportOptions
{
    /// <summary>Line ending written. Default "\n".</summary>
    public string NewLine { get; set; } = "\n";

    /// <summary>An empty line between paragraphs. Default false.</summary>
    public bool BlankLineBetweenParagraphs { get; set; }

    /// <summary>Write list labels as Word shows them ("1.", "a)", "•"), followed by <see cref="ListLabelSeparator"/>. Default true.</summary>
    public bool ListLabels { get; set; } = true;

    /// <summary>Text after a list label. Default " ".</summary>
    public string ListLabelSeparator { get; set; } = " ";

    /// <summary>Indentation written per list level. Default two spaces.</summary>
    public string ListIndent { get; set; } = "  ";

    /// <summary>Between table cells. Default tab.</summary>
    public string TableCellSeparator { get; set; } = "\t";

    /// <summary>Within a table cell, between its paragraphs. Default " ".</summary>
    public string TableCellParagraphSeparator { get; set; } = " ";

    /// <summary>For a tab run. Default tab.</summary>
    public string Tab { get; set; } = "\t";

    /// <summary>For a page break (also for a paragraph that starts a new page). Default form feed; null = a plain line break.</summary>
    public string? PageBreak { get; set; } = "\f";

    /// <summary>Note reference format: {0} is the label Word shows. Default "[{0}]".</summary>
    public string NoteReferenceFormat { get; set; } = "[{0}]";

    /// <summary>Written on its own line before the notes. Default "----".</summary>
    public string? NotesSeparator { get; set; } = "----";
}

/// <summary>How raw HTML inside Markdown is imported.</summary>
public enum RawHtmlImport
{
    /// <summary>Sanitized and converted like HTML input (inline tags such as <c>b</c>, <c>i</c>, <c>u</c>, <c>sub</c>, <c>sup</c>, <c>br</c> become formatting). Default.</summary>
    Convert,
    /// <summary>Kept as literal text.</summary>
    Text,
    /// <summary>Left out.</summary>
    Drop,
}

/// <summary>How a Markdown soft line break is imported.</summary>
public enum SoftBreakImport
{
    /// <summary>As a space (the paragraph re-flows, as Markdown renders it). Default.</summary>
    Space,
    /// <summary>As a line break.</summary>
    LineBreak,
}

/// <summary>Options for <see cref="MarkdownConversion.Import(string, MarkdownImportOptions?, System.Threading.CancellationToken)"/>.</summary>
public sealed class MarkdownImportOptions : ImportOptions
{
    /// <summary>Parser options (GFM extensions, limits). Default <see cref="MarkdownOptions.Gfm"/>.</summary>
    public MarkdownOptions Parser { get; set; } = MarkdownOptions.Gfm();

    /// <summary>Raw HTML blocks and inline tags. Default <see cref="RawHtmlImport.Convert"/>.</summary>
    public RawHtmlImport RawHtml { get; set; } = RawHtmlImport.Convert;

    /// <summary>Options for converting raw HTML (its sanitizer policy, inline styles). Default <see cref="HtmlImportOptions"/> defaults.</summary>
    public HtmlImportOptions Html { get; set; } = new();

    /// <summary>Soft line breaks. Default <see cref="SoftBreakImport.Space"/>.</summary>
    public SoftBreakImport SoftBreaks { get; set; } = SoftBreakImport.Space;

    /// <summary>A thematic break (<c>---</c>) becomes an empty paragraph with a bottom border (true, default) or a page break (false).</summary>
    public bool ThematicBreakAsBorder { get; set; } = true;
}

/// <summary>Options for <see cref="MarkdownConversion.Export(Document, MarkdownExportOptions?, System.Threading.CancellationToken)"/>.</summary>
public sealed class MarkdownExportOptions : ExportOptions
{
    /// <summary>Writer options (bullet, emphasis and fence characters, heading style, table alignment). Default <see cref="MarkdownWriterOptions"/> defaults.</summary>
    public MarkdownWriterOptions Writer { get; set; } = new();

    /// <summary>Underline has no Markdown syntax: write it as raw HTML <c>&lt;u&gt;</c> (true) or leave it out (false, default).</summary>
    public bool UnderlineAsHtml { get; set; }

    /// <summary>Subscript and superscript as raw HTML <c>&lt;sub&gt;</c> / <c>&lt;sup&gt;</c> (true, default) or as plain text (false).</summary>
    public bool SubSuperscriptAsHtml { get; set; } = true;

    /// <summary>Write list labels as text instead of Markdown list markers (keeps Word's exact numbering). Default false.</summary>
    public bool ListLabelsAsText { get; set; }

    /// <summary>GFM table cells hold one line: paragraphs and line breaks in a cell are joined with this raw HTML. Default "&lt;br&gt;"; null joins with a space.</summary>
    public string? TableCellLineBreak { get; set; } = "<br>";

    /// <summary>Note reference format: {0} is the label Word shows. Default "[{0}]".</summary>
    public string NoteReferenceFormat { get; set; } = "[{0}]";

    /// <summary>
    /// Where Markdown delimiters cannot open or close (CommonMark's flanking rules), emphasis is written
    /// as raw HTML (default) or dropped. Raw HTML can therefore appear even with <see cref="UnderlineAsHtml"/> off.
    /// </summary>
    public MarkdownEmphasisFallback EmphasisFallback { get; set; } = MarkdownEmphasisFallback.Html;

    /// <summary>Tables whose first row is not a header row still get one (GFM requires it): the first row is used (true, default) or an empty header row is added (false).</summary>
    public bool FirstRowAsHeader { get; set; } = true;
}

/// <summary>What the Markdown exporter writes when CommonMark's delimiter rules cannot express bold, italic or strikethrough at a spot (for example "**Note:**text" or an emphasised part of a word with "_").</summary>
public enum MarkdownEmphasisFallback
{
    /// <summary>Raw HTML tags (<c>&lt;strong&gt;</c>, <c>&lt;em&gt;</c>, <c>&lt;del&gt;</c>); shown as formatting by renderers that pass raw HTML through. Default.</summary>
    Html,
    /// <summary>The text without that formatting (pure Markdown, nothing literal left behind).</summary>
    PlainText,
}

/// <summary>How much formatting the HTML exporter writes.</summary>
public enum HtmlFormatting
{
    /// <summary>Structure and semantic tags only (headings, lists, tables, strong, em, links…).</summary>
    Semantic,
    /// <summary>Also inline styles for colours, highlight, font size and family, alignment and indentation. Default.</summary>
    Inline,
}

/// <summary>How lists are written to HTML.</summary>
public enum HtmlListRendering
{
    /// <summary><c>ul</c> / <c>ol</c> with <c>li</c> (start numbers kept). Default.</summary>
    Semantic,
    /// <summary>Paragraphs that start with Word's exact list label (for "1.1.a)" style numbering).</summary>
    Labels,
}

/// <summary>Options for <see cref="HtmlConversion.Export(Document, HtmlExportOptions?, System.Threading.CancellationToken)"/>.</summary>
public sealed class HtmlExportOptions : ExportOptions
{
    /// <summary>A complete document (doctype, head with charset, title and style sheet) rather than a fragment. Default true.</summary>
    public bool FullDocument { get; set; } = true;

    /// <summary>Formatting written. Default <see cref="HtmlFormatting.Inline"/>.</summary>
    public HtmlFormatting Formatting { get; set; } = HtmlFormatting.Inline;

    /// <summary>A small style sheet in the head (tables, code, quotes) when <see cref="FullDocument"/>. Default true.</summary>
    public bool IncludeStyleSheet { get; set; } = true;

    /// <summary>The document title (head/title); null = the document's Title property.</summary>
    public string? Title { get; set; }

    /// <summary>The html lang attribute; null = the document's Language property.</summary>
    public string? Language { get; set; }

    /// <summary>Lists. Default <see cref="HtmlListRendering.Semantic"/>.</summary>
    public HtmlListRendering Lists { get; set; } = HtmlListRendering.Semantic;

    /// <summary>Prefix for generated ids (bookmarks, notes), so several exports can share a page. Default "".</summary>
    public string IdPrefix { get; set; } = "";

    /// <summary>Wrap a fragment in this element with <see cref="WrapperClass"/> (null = no wrapper). Default null.</summary>
    public string? WrapperElement { get; set; }

    /// <summary>Class of the wrapper element. Default "chuvadi-docx".</summary>
    public string WrapperClass { get; set; } = "chuvadi-docx";

    /// <summary>Stop after this many top-level blocks (a "…" paragraph marks the cut). Null = all.</summary>
    public int? MaxBlocks { get; set; }

    /// <summary>Line ending between block elements. Default "\n".</summary>
    public string NewLine { get; set; } = "\n";

    /// <summary>
    /// Settings for a quick, safe preview (for example a mail attachment in Anjal): a fragment
    /// wrapped in <c>div.chuvadi-docx</c>, inline formatting only (no style sheet), pictures as
    /// <c>data:</c> URIs up to 2 MiB, at most 2000 blocks, no headers or footers. The output contains
    /// no script, no event handler and no external reference: Chuvadi.Markup's
    /// <c>SanitizerPolicy.Anjal()</c> removes nothing from it (it only makes links inert and
    /// prefixes ids, as it does for every mail).
    /// </summary>
    public static HtmlExportOptions Preview() => new()
    {
        FullDocument = false,
        IncludeStyleSheet = false,
        WrapperElement = "div",
        MaxImageBytes = 2L * 1024 * 1024,
        MaxBlocks = 2000,
        IncludeHeadersFooters = false,
    };
}

/// <summary>Options for <see cref="HtmlConversion.Import(string, HtmlImportOptions?, System.Threading.CancellationToken)"/>.</summary>
public sealed class HtmlImportOptions : ImportOptions
{
    /// <summary>
    /// The sanitizer policy the input passes through before it is mapped to the document (scripts,
    /// event handlers, forms, frames and unsafe URLs never reach the model). Default <see cref="DefaultPolicy"/>.
    /// </summary>
    public SanitizerPolicy Policy { get; set; } = DefaultPolicy();

    /// <summary>Apply inline <c>style</c> declarations (colour, background, font size, family, weight, style, decoration, alignment, margin-left). Default true.</summary>
    public bool ApplyInlineStyles { get; set; } = true;

    /// <summary>
    /// The default policy: Chuvadi.Markup's text elements, links kept (http, https, mailto and
    /// fragments), images only as <c>data:</c> URIs (or through <see cref="ImportOptions.ImageResolver"/>),
    /// style sheets dropped, no SVG, MathML or form controls, ids kept unprefixed (they become bookmarks),
    /// no wrapper element, and Chuvadi.Markup's <c>MarkupLimits.Strict()</c>.
    /// </summary>
    public static SanitizerPolicy DefaultPolicy()
    {
        var p = SanitizerPolicy.Anjal();
        p.Links = LinkHandling.Keep;
        p.LinksOpenInNewWindow = false;
        p.AllowRelativeLinks = true;
        p.StyleSheets = StyleSheetHandling.Drop;
        p.ScopeClass = null;
        p.IdPrefix = null;
        p.TransferBodyStyle = false;
        // Anjal's Strict() limits stay: HTML input is untrusted (8 Mi characters, 500 000 nodes, depth 256).
        return p;
    }
}

/// <summary>The document an importer built, with what it could not bring across.</summary>
public sealed class ConversionResult
{
    internal ConversionResult(Document document, IReadOnlyList<string> warnings, IReadOnlyList<string> blockedResources)
    {
        Document = document;
        Warnings = warnings;
        BlockedResources = blockedResources;
    }

    /// <summary>The imported document (or the target it was appended to).</summary>
    public Document Document { get; }

    /// <summary>Content that was simplified or left out (pictures that could not be imported, unsupported elements).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>URLs the sanitizer removed (remote pictures and other resources), for a "show remote content" prompt.</summary>
    public IReadOnlyList<string> BlockedResources { get; }
}

internal static class ConversionDefaults
{
    public static HashSet<string> CodeStyleIds() => new(StringComparer.Ordinal) { "Code", "SourceCode", "HTMLPreformatted", "PlainText", "CodeBlock" };
    public static HashSet<string> QuoteStyleIds() => new(StringComparer.Ordinal) { "Quote", "IntenseQuote", "BlockText" };
    public static HashSet<string> MonospaceFonts() => new(StringComparer.OrdinalIgnoreCase)
    {
        "Consolas", "Courier New", "Courier", "Lucida Console", "Cascadia Code", "Cascadia Mono", "Menlo", "Monaco",
        "DejaVu Sans Mono", "Liberation Mono", "Source Code Pro", "Fira Code", "Fira Mono", "JetBrains Mono", "Roboto Mono",
        "Ubuntu Mono", "SF Mono", "monospace",
    };
}
