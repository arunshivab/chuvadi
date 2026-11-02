# Chuvadi.Layout.Markup

Markdown and HTML print and export for [Chuvadi.Layout](https://github.com/arunshivab/chuvadi).

- **Markdown:** Chuvadi.Markup's Markdown AST (CommonMark 0.31.2 plus GFM tables, task lists, strikethrough and
  autolinks) becomes Chuvadi.Layout's flow document.
- **HTML:** untrusted HTML (mail, pages) is sanitized with Chuvadi.Markup's sanitizer, and the sanitized DOM becomes
  the flow document (see "HTML" below).

Both paginate into the same page display list as docx and text, and render to SVG, PNG and PDF through
Chuvadi.Layout.Pdf. Pure BCL; requires **Chuvadi.Markup 1.0.0**.

```csharp
var options = new LayoutOptions { Culture = CultureInfo.CurrentCulture };   // Letter or A4 by region (D-30)
options.FontSources.Add(BundledFontSource.Instance);
using LaidOutDocument doc = MarkdownLayout.Open(File.ReadAllText("README.md"), new MarkdownImportOptions
{
    ImageResolver = url => LoadImageBytesOrNull(url),     // never fetched by the library
    LinkResolver = url => Uri.IsWellFormedUriString(url, UriKind.Absolute) ? url : null,
}, options);
string svg = doc.GetPage(0).ToSvg();                       // Chuvadi.Layout.Pdf
```

## What is laid out

| Markdown | Layout |
|---|---|
| Headings 1–6 | 20/16/14/12/11/11 pt bold, keep with next; a rule under 1 and 2 |
| Paragraphs, soft and hard breaks | body text, 1.15 line spacing, 8 pt after |
| Emphasis, strong, strikethrough | italic, bold, strike (nestable) |
| Code spans and blocks | monospace (Consolas, any monospace substitute) on a light background; code blocks keep their lines and indentation |
| Block quotes | indented, with a bar and muted text, nestable |
| Bullet, ordered and task lists | labels hang in the indent (•, ◦, ▪ by level; numbers keep the list's start and delimiter; □ and ✓ for tasks); tight and loose spacing |
| Tables | header row bold, shaded and repeated on every page; column alignment; widths in proportion to the longest cell |
| Thematic breaks | a rule |
| Links | coloured and underlined; the target is carried when `LinkResolver` (default: absolute http, https, mailto) allows it |
| Images | `data:` URIs are decoded; other destinations come from `ImageResolver`; size from the pixels at `ImageDpi` (96), scaled to fit; otherwise the alt text is printed |
| Raw HTML | printed literally (`RawHtmlMode.Escape`, the default, as Chuvadi.Markup's renderer) or left out (`Strip`) |

Every look-and-feel value (fonts, sizes, colours, spacing, bullets, task marks, table colours, footer size) is in
`MarkdownStyleSheet`. The page follows the culture's paper unless `MarkdownImportOptions.Page` says otherwise. A footer
"Page N of M" is on by default (`PageNumbers`, `PageNumberText`). `PageBreakBeforeHeading1` starts chapters on new pages.

## HTML

```csharp
// Mail: sanitize with the safest policy (the default), lay out, export.
using LaidOutDocument mail = HtmlLayout.Open(htmlBytes, new HtmlImportOptions { Encoding = mimeCharset }, options);

// The same sanitized result a web view shows (cid: parts already resolved by the policy's resolver):
using LaidOutDocument shown = HtmlLayout.Open(sanitizeResult, null, options);

// A web page: relative links kept for the app, local images through a resolver.
using LaidOutDocument page = HtmlLayout.Open(File.ReadAllBytes("page.html"),
    new HtmlImportOptions { Policy = new SanitizerPolicy { AllowRelativeLinks = true, Links = LinkHandling.Active }, ImageResolver = LocalImageOrNull }, options);
```

| HTML | Layout |
|---|---|
| `h1`–`h6`, `p`, `div` and the other block elements, `br`, `hr` | paragraphs with the browser's default sizes and margins (collapsed); headings keep with the next block |
| `b`/`strong`, `i`/`em`, `u`/`ins`, `s`/`strike`/`del`, `sub`, `sup`, `small`, `big`, `code`/`kbd`/`samp`/`tt`, `mark`, `q`, `font` | character formats (`q` adds the quotes of `HtmlStyleSheet.Quotes`) |
| `pre`, `white-space` | text kept as written, monospace |
| `a` | the link target (`href`, or the sanitizer's `data-chuvadi-href` for inert links) through `LinkResolver` |
| `blockquote`, `dl`/`dt`/`dd`, `figure` | indents (40 px), borders and padding from inline CSS |
| `ul`, `ol`, `li` | markers by type and level; `start`, `value`, `reversed` |
| `table` and its parts | thead (repeated), tbody, tfoot; colspan, rowspan; widths, cellpadding, cellspacing, border, bgcolor, align, valign; one-column layout tables become blocks so long mail splits across pages |
| `img` | `data:` URIs (or `ImageResolver`): width, height, aspect ratio, max-width, `display:block` with auto margins; otherwise the alt text (`MissingImages`) |
| `style` attribute | color, background, font (family, size, weight, style, variant, shorthand), text-decoration, text-transform, letter-spacing, text-align, text-indent, line-height, white-space, vertical-align, margin, padding, border (all forms), width, height, max-width, list-style, display, visibility, page-break / break-before/after/inside |
| Hidden content | `display:none`, `visibility:hidden`, `hidden`, `mso-hide:all`, `opacity:0`, `max-height:0` with `overflow:hidden`, `font-size:0` text |

`HtmlStyleSheet` holds the browser defaults (fonts for the CSS generic families, sizes, colours, indents, bullets,
quotes). `PrintBackgrounds` (default true) prints background colours.

Not applied yet: `<style>` sheets (warning), floats, positioning, flex/grid, background images, rounded corners and
shadows. SVG, MathML, form controls, audio and video are left out with a warning.

## Untrusted input

- Parser limits come from `LayoutOptions.Limits`: input size, node count, table cells and nesting. Deeper nesting is
  flattened by the parser, with its content kept, and indents stop at half the text width.
- Exceeded limits throw `LayoutLimitExceededException` (names prefixed `Markup` for parser limits). Cancellation
  throws `OperationCanceledException`.
- Nothing is fetched: images come only from `data:` URIs (within `MaxResourceBytes`) or your resolver.
- HTML is sanitized before layout unless you pass a DOM yourself. Sanitizer limits come from the policy
  (the default policy uses `MarkupLimits.Strict()`), and HTML input is also held to `MaxSourceBytes`.
  `MaxBlocks` caps the elements walked, and tables nested deeper than `MaxNestingDepth` become paragraphs.

## Why a separate package (D-32)

Chuvadi.Layout's core does not reference Chuvadi.Markup; this add-on does.

- **For:**
  - Apps that only show docx, xlsx and text don't carry the Markdown/HTML/CSS parser and sanitizer.
  - Markup is in preview, and its releases don't force a core Layout release.
  - The core's dependency list stays the formats it prints natively.
- **Against:**
  - There is one more package and tag to version.
  - `DocumentLayout.Open(stream, Auto)` cannot sniff Markdown (it is text), so callers choose `MarkdownLayout.Open`
    by file type. `DocumentLayout.Open(stream, LayoutSourceFormat.Markdown)` says so.

The HTML importer lives here for the same reasons. `DocumentLayout.Open(stream, LayoutSourceFormat.Html)` points to
`HtmlLayout.Open`.

## Build

This project is not in `Chuvadi.slnx` yet (the integrator adds it). It needs `src/Chuvadi.Markup` (1.0.0).

```
dotnet build tests\Chuvadi.Layout.Markup.Tests -c Release
dotnet test tests\Chuvadi.Layout.Markup.Tests -c Release --no-build
```
