using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using Chuvadi.Markup.Markdown;

namespace Chuvadi.Layout.Markup
{
    /// <summary>Settings for <see cref="MarkdownImporter"/> and <see cref="MarkdownLayout"/>. Every member has a default.</summary>
    public sealed record MarkdownImportOptions
    {
        /// <summary>Markdown dialect and parser limits. Null: GFM (tables, task lists, strikethrough, autolinks) with limits derived from <see cref="LayoutOptions.Limits"/>.</summary>
        public MarkdownOptions? Parser { get; init; }

        /// <summary>Page size and margins. Null: the paper of <see cref="LayoutOptions.Culture"/>'s region (D-30) with 1-inch margins.</summary>
        public PageGeometry? Page { get; init; }

        /// <summary>Fonts, sizes, colours and spacing. Default <see cref="MarkdownStyleSheet.Default"/> (close to GitHub's rendering).</summary>
        public MarkdownStyleSheet Styles { get; init; } = MarkdownStyleSheet.Default;

        /// <summary>
        /// Raw HTML in the Markdown. <see cref="RawHtmlMode.Escape"/> (default) prints the tags literally, as Chuvadi.Markup's
        /// HTML renderer shows them by default; <see cref="RawHtmlMode.Strip"/> leaves them out. <see cref="RawHtmlMode.Passthrough"/>
        /// is not laid out in this version (it is stripped, with an UnsupportedFeature warning).
        /// </summary>
        public RawHtmlMode RawHtml { get; init; } = RawHtmlMode.Escape;

        /// <summary>
        /// Returns the encoded bytes of an image destination (after <c>data:</c> URIs, which are decoded here). Null or a
        /// null result prints the image's alternative text instead. Nothing is ever fetched by this library.
        /// </summary>
        public Func<string, byte[]?>? ImageResolver { get; init; }

        /// <summary>Decode <c>data:</c> image URIs (base64). Default true.</summary>
        public bool DataUriImages { get; init; } = true;

        /// <summary>Image resolution for images that do not state a size: pixels per inch. Default 96, as browsers.</summary>
        public double ImageDpi { get; init; } = 96;

        /// <summary>
        /// Maps a link destination to the URI carried by the laid-out link; null drops the link (the text stays).
        /// Default: absolute http, https and mailto destinations are kept; everything else is dropped.
        /// </summary>
        public Func<string, string?>? LinkResolver { get; init; }

        /// <summary>Print "Page N of M" in the footer. Default true.</summary>
        public bool PageNumbers { get; init; } = true;

        /// <summary>Footer text around the page numbers: "{page}" and "{pages}" are replaced. Default "Page {page} of {pages}".</summary>
        public string PageNumberText { get; init; } = "Page {page} of {pages}";

        /// <summary>Start every level-1 heading after the first on a new page. Default false.</summary>
        public bool PageBreakBeforeHeading1 { get; init; }

        /// <summary>Document title; null uses the first level-1 heading.</summary>
        public string? Title { get; init; }
    }

    /// <summary>
    /// The look of laid-out Markdown. The default is close to GitHub's rendering, set in Calibri-compatible fonts so it
    /// prints identically with the bundled font pack.
    /// </summary>
    public sealed record MarkdownStyleSheet
    {
        public static MarkdownStyleSheet Default { get; } = new MarkdownStyleSheet();

        public string BodyFont { get; init; } = "Calibri";

        public double BodySize { get; init; } = 11;

        public LayoutColor TextColor { get; init; } = new LayoutColor(0x1F, 0x23, 0x28);

        /// <summary>Line spacing as a multiple of single spacing.</summary>
        public double LineSpacing { get; init; } = 1.15;

        /// <summary>Space after paragraphs, block quotes, code blocks, tables and loose list items, in points.</summary>
        public double BlockSpacing { get; init; } = 8;

        /// <summary>Space after the items of a tight list, in points.</summary>
        public double TightItemSpacing { get; init; } = 2;

        public string HeadingFont { get; init; } = "Calibri";

        /// <summary>Sizes of headings 1 to 6, in points.</summary>
        public IReadOnlyList<double> HeadingSizes { get; init; } = new[] { 20.0, 16, 14, 12, 11, 11 };

        public LayoutColor HeadingColor { get; init; } = new LayoutColor(0x1F, 0x23, 0x28);

        /// <summary>Heading 6 is drawn in this colour (GitHub's muted heading).</summary>
        public LayoutColor MutedColor { get; init; } = new LayoutColor(0x59, 0x63, 0x6E);

        /// <summary>A thin rule under headings 1 and 2. Default true.</summary>
        public bool HeadingRules { get; init; } = true;

        public LayoutColor RuleColor { get; init; } = new LayoutColor(0xD1, 0xD9, 0xE0);

        /// <summary>Monospace font for code; any monospace substitute is used when it is not installed.</summary>
        public string CodeFont { get; init; } = "Consolas";

        /// <summary>Code size as a fraction of the surrounding text size.</summary>
        public double CodeScale { get; init; } = 0.9;

        public LayoutColor CodeBackground { get; init; } = new LayoutColor(0xF6, 0xF8, 0xFA);

        public LayoutColor LinkColor { get; init; } = new LayoutColor(0x09, 0x69, 0xDA);

        public bool UnderlineLinks { get; init; } = true;

        /// <summary>Indent per list level and per block quote level, in points.</summary>
        public double Indent { get; init; } = 18;

        public LayoutColor QuoteBarColor { get; init; } = new LayoutColor(0xD1, 0xD9, 0xE0);

        public LayoutColor QuoteTextColor { get; init; } = new LayoutColor(0x59, 0x63, 0x6E);

        /// <summary>Bullets by nesting level (repeats).</summary>
        public IReadOnlyList<string> Bullets { get; init; } = new[] { "\u2022", "\u25E6", "\u25AA" };

        public string TaskOpen { get; init; } = "\u25A1";

        public string TaskDone { get; init; } = "\u2713";

        public LayoutColor TableBorderColor { get; init; } = new LayoutColor(0xD1, 0xD9, 0xE0);

        public LayoutColor TableHeaderBackground { get; init; } = new LayoutColor(0xF6, 0xF8, 0xFA);

        public double TableCellPadding { get; init; } = 5;

        public double FooterSize { get; init; } = 9;

        internal CharacterFormat Body => new CharacterFormat { FontFamily = BodyFont, FontSize = BodySize, Color = TextColor };

        internal static CharacterFormat Code(CharacterFormat around, MarkdownStyleSheet s) => around with
        {
            FontFamily = s.CodeFont,
            HighAnsiFontFamily = null,
            GenericFamily = GenericFontFamily.Monospace,
            FontSize = Math.Round(around.FontSize * s.CodeScale * 2, MidpointRounding.AwayFromZero) / 2,
        };
    }
}
