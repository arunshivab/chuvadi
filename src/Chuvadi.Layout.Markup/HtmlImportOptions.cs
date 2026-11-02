using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Layout.Markup
{
    /// <summary>What to print for an image that has no data (blocked by the sanitizer, unresolved, or undecodable).</summary>
    public enum MissingImageHandling
    {
        /// <summary>Print the image's alternative text, as a browser does for a broken image; nothing when it has none. Default.</summary>
        AltText,

        /// <summary>Print nothing.</summary>
        Omit,
    }

    /// <summary>Settings for <see cref="HtmlImporter"/> and <see cref="HtmlLayout"/>. Every member has a default.</summary>
    public sealed record HtmlImportOptions
    {
        /// <summary>
        /// The sanitizer policy used when the importer is given HTML text or bytes. Null: a new <see cref="SanitizerPolicy"/>,
        /// Chuvadi.Markup's safest policy (nothing reaches the network; resources come only through its
        /// <c>ResourceResolver</c>). Pass your own policy with its switches set (links, relative links, form controls,
        /// SVG, MathML, media, embedded fonts, a resolver for <c>cid:</c> parts) for other content. Not used by the overloads that take a <see cref="SanitizeResult"/> or a DOM node.
        /// </summary>
        public SanitizerPolicy? Policy { get; init; }

        /// <summary>For byte input: the encoding to use (for example the MIME charset). Null: byte-order mark, <c>&lt;meta&gt;</c>, then windows-1252, as a browser.</summary>
        public Encoding? Encoding { get; init; }

        /// <summary>Page size and margins. Null: the paper of <see cref="LayoutOptions.Culture"/>'s region (D-30) with 1-inch margins.</summary>
        public PageGeometry? Page { get; init; }

        /// <summary>The browser defaults: fonts, sizes, colours and spacing. Default <see cref="HtmlStyleSheet.Default"/> (a browser's built-in style sheet).</summary>
        public HtmlStyleSheet Styles { get; init; } = HtmlStyleSheet.Default;

        /// <summary>
        /// Returns the encoded bytes of an image whose source is not a <c>data:</c> URI (normally only reachable through the
        /// DOM overload, since sanitizer policies block other sources). Null or a null result: see <see cref="MissingImages"/>.
        /// Nothing is ever fetched by this library.
        /// </summary>
        public Func<string, byte[]?>? ImageResolver { get; init; }

        /// <summary>Decode <c>data:</c> image URIs (base64). Default true.</summary>
        public bool DataUriImages { get; init; } = true;

        /// <summary>Image resolution for images without a stated size: pixels per inch. Default 96, as browsers.</summary>
        public double ImageDpi { get; init; } = 96;

        /// <summary>What to print for images that have no data. Default <see cref="MissingImageHandling.AltText"/>.</summary>
        public MissingImageHandling MissingImages { get; init; } = MissingImageHandling.AltText;

        /// <summary>
        /// Maps a link destination to the URI carried by the laid-out link; null drops the link (the text stays).
        /// Default: absolute http, https and mailto destinations are kept; everything else is dropped.
        /// </summary>
        public Func<string, string?>? LinkResolver { get; init; }

        /// <summary>
        /// The attribute where the sanitizer keeps the destination of an inert link (<see cref="SanitizerPolicy.InertLinkAttribute"/>),
        /// read when a link has no <c>href</c>. Default "data-chuvadi-href". Null: inert links stay plain text.
        /// </summary>
        public string? InertLinkAttribute { get; init; } = "data-chuvadi-href";

        /// <summary>
        /// Print background colours (CSS <c>background-color</c>, <c>bgcolor</c>, <c>&lt;mark&gt;</c>). Default true, so
        /// mail prints as it is shown; browsers leave them out unless "Background graphics" is ticked.
        /// </summary>
        public bool PrintBackgrounds { get; init; } = true;

        /// <summary>Print "Page N of M" in the footer. Default true.</summary>
        public bool PageNumbers { get; init; } = true;

        /// <summary>Footer text around the page numbers: "{page}" and "{pages}" are replaced. Default "Page {page} of {pages}".</summary>
        public string PageNumberText { get; init; } = "Page {page} of {pages}";

        /// <summary>Document title; null uses the HTML <c>&lt;title&gt;</c>, else the first <c>&lt;h1&gt;</c>.</summary>
        public string? Title { get; init; }
    }

    /// <summary>
    /// The built-in look of HTML: the defaults of a browser's user-agent style sheet (16 px Times New Roman, blue
    /// underlined links, headings at 2em to 0.67em, 40 px list and quote indents). Author styles in the HTML override them.
    /// </summary>
    public sealed record HtmlStyleSheet
    {
        public static HtmlStyleSheet Default { get; } = new HtmlStyleSheet();

        /// <summary>The default font (a browser's "standard font").</summary>
        public string BodyFont { get; init; } = "Times New Roman";

        /// <summary>The generic class of <see cref="BodyFont"/>, used when it is not installed.</summary>
        public GenericFontFamily BodyGeneric { get; init; } = GenericFontFamily.Serif;

        /// <summary>The default font size in points (16 px).</summary>
        public double BodySize { get; init; } = 12;

        /// <summary>The font for the CSS generic family <c>serif</c>.</summary>
        public string SerifFont { get; init; } = "Times New Roman";

        /// <summary>The font for the CSS generic family <c>sans-serif</c> (and <c>system-ui</c>).</summary>
        public string SansSerifFont { get; init; } = "Arial";

        /// <summary>The font for the CSS generic family <c>monospace</c>, and for code, pre, kbd, samp and tt.</summary>
        public string MonospaceFont { get; init; } = "Courier New";

        /// <summary>Monospace text size relative to the surrounding text (browsers use 13 px against 16 px).</summary>
        public double MonospaceScale { get; init; } = 13.0 / 16;

        public LayoutColor TextColor { get; init; } = LayoutColor.Black;

        public LayoutColor LinkColor { get; init; } = new LayoutColor(0x00, 0x00, 0xEE);

        public bool UnderlineLinks { get; init; } = true;

        /// <summary>Background of <c>&lt;mark&gt;</c>.</summary>
        public LayoutColor MarkBackground { get; init; } = new LayoutColor(0xFF, 0xFF, 0x00);

        /// <summary>Heading sizes relative to the surrounding text, h1 to h6.</summary>
        public IReadOnlyList<double> HeadingScales { get; init; } = new[] { 2.0, 1.5, 1.17, 1.0, 0.83, 0.67 };

        /// <summary>Heading top and bottom margins in em of the heading's own size, h1 to h6.</summary>
        public IReadOnlyList<double> HeadingMargins { get; init; } = new[] { 0.67, 0.83, 1.0, 1.33, 1.67, 2.33 };

        /// <summary>Indent of lists, block quotes, <c>dd</c> and figures, in points (40 px).</summary>
        public double Indent { get; init; } = 30;

        /// <summary>Bullets by nesting level for unordered lists without a stated type: disc, circle, square.</summary>
        public IReadOnlyList<string> Bullets { get; init; } = new[] { "\u2022", "\u25E6", "\u25AA" };

        /// <summary>Opening and closing quotes for <c>&lt;q&gt;</c>, outer level then inner.</summary>
        public IReadOnlyList<string> Quotes { get; init; } = new[] { "\u201C", "\u201D", "\u2018", "\u2019" };

        /// <summary>Colour of <c>&lt;hr&gt;</c> and of the cell borders of <c>&lt;table border&gt;</c>.</summary>
        public LayoutColor RuleColor { get; init; } = new LayoutColor(0x80, 0x80, 0x80);

        /// <summary>Colour of the alternative text printed for missing images, and of the page footer.</summary>
        public LayoutColor MutedColor { get; init; } = new LayoutColor(0x59, 0x63, 0x6E);

        public double FooterSize { get; init; } = 9;
    }
}
