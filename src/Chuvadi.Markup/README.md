# Chuvadi.Markup

Pure-BCL Markdown, HTML and CSS for .NET — zero NuGet dependencies, part of the [Chuvadi](https://github.com/arunshivab/chuvadi) family. Built for untrusted input: every entry point takes a `CancellationToken`, enforces `MarkupLimits`, and documents its exceptions.

What 1.1 gives you:

- **Markdown**: a parser written from the CommonMark 0.31.2 and GFM 0.29 specifications (CommonMark: all 652 spec examples; GFM: 651 of 672 spec examples and all 26 extension tests, footnotes excluded) with the GFM extensions — tables, task lists, strikethrough, extended autolinks — and the tag filter. A public, editable **AST** with source positions, an **HTML renderer** (the spec examples' HTML; raw HTML escape / strip / passthrough; unsafe URLs removed by default) and a **Markdown writer** that turns any AST back into Markdown.
- **HTML**: the WHATWG tokenizer and tree builder — the DOM a browser builds, including adoption agency, foster parenting, templates, SVG/MathML and fragment parsing (the full html5lib tokenizer and tree-construction suites pass, including `<selectedcontent>` mirroring). Charset sniffing from bytes (BOM, caller, `<meta>` prescan, windows-1252 fallback, all WHATWG labels .NET supports). A small editable DOM and a spec serializer.
- **CSS**: a CSS Syntax Level 3 tokenizer and parser (style sheets, declarations, nesting) and a safe serializer.
- **Sanitizer**: allowlist sanitizer. **`new SanitizerPolicy()` is the safest configuration** (links inert, images only as `data:` URIs, no forms, SVG, MathML, media or embedded fonts, strict limits, nothing ever fetched); each capability is a plain switch the application turns on in its own code (`Links`, `AllowRelativeLinks`, `AllowFormControls`, `AllowMedia`, `AllowSvg`, `AllowMathML`, `AllowSvgDataImages`, `AllowEmbeddedFonts`, `StyleSheets`, `ResourceResolver`, `Limits` and the allowlists). Style sheets are sanitized and scoped under a wrapper class, ids and document-global CSS names (fonts, keyframes, counter styles) are prefixed, `cid:` and relative URLs go through your resolver, blocked resources are reported, and the output is re-parsed until it is a fixed point (mutation-XSS safe).
- **Plain text**: text or `format=flowed` mail to safe HTML, with quote levels, autolinks (inert by default, like the sanitizer; or live or rewritten) and three layouts.

```csharp
using Chuvadi.Markup;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Markdown;
using Chuvadi.Markup.Sanitization;
using Chuvadi.Markup.Text;

// Markdown -> HTML (GFM by default; raw HTML escaped, unsafe URLs removed)
string html = MarkdownConverter.ToHtml(markdown);

// Full control: parse once, walk the AST, render or write Markdown back
MarkdownDocument doc = MarkdownParser.Parse(markdown, MarkdownOptions.Gfm(), ct);
foreach (var h in doc.Descendants().OfType<Heading>())
    Console.WriteLine($"{h.Level}: {h.ToPlainText()} at {doc.GetPosition(h.Span.Start)}");
string withIds = MarkdownHtmlRenderer.Render(doc, new MarkdownHtmlOptions { HeadingIds = true, SourcePositions = true }, ct);
string markdownAgain = MarkdownWriter.Write(doc);

// Untrusted mail HTML: the default policy is the strictest (network blocked, links inert)
var policy = new SanitizerPolicy();
policy.ResourceResolver = new DelegateResourceResolver(r =>
    r.Url.StartsWith("cid:") ? FindCidPartAsDataUri(r.Url.Substring(4)) : null);
SanitizeResult mail = HtmlSanitizer.Sanitize(mimeHtmlPartBytes, policy, encoding: null, ct);
Console.WriteLine(mail.Html);                      // <div class="chuvadi-content">...</div>
foreach (var b in mail.BlockedResources)           // e.g. offer "show images"
    Console.WriteLine($"{b.Kind} {b.Url}");

// A document viewer turns on what it needs; still nothing is fetched without a resolver
var viewer = new SanitizerPolicy
{
    Links = LinkHandling.Active,
    AllowRelativeLinks = true,
    AllowFormControls = true,   // inert: forms are always unwrapped
    AllowMedia = true,
    AllowSvg = true,
    AllowMathML = true,
    AllowEmbeddedFonts = true,  // families are namespaced so they cannot restyle the host
};
SanitizeResult page = HtmlSanitizer.Sanitize(untrustedHtml, viewer, ct);

// Plain-text mail -> safe HTML (links inert by default; Links = LinkHandling.Active for live links)
string body = PlainText.ToHtml(text, new TextToHtmlOptions { FormatFlowed = true }, ct);

// The HTML parser on its own: the DOM a browser would build
HtmlDocument dom = HtmlParser.Parse(untrustedHtml, new HtmlParserOptions { Limits = MarkupLimits.Strict() }, ct);
```

Show sanitizer output inside a page that also carries a Content-Security-Policy such as
`default-src 'none'; img-src data:; style-src 'unsafe-inline'` — defence in depth.

Exceptions: `ArgumentNullException` for null input, `ArgumentOutOfRangeException` for invalid limits, `MarkupLimitExceededException` (with `LimitName`, `Limit`, `Actual`) when a hard limit is exceeded, `OperationCanceledException` on cancellation. Malformed input never throws: it is parsed the way the HTML, CommonMark and GFM specifications describe (for HTML, the way a browser parses it).

MIT licensed, with no third-party code. Since 1.1.0 the Markdown parser and HTML renderer are a clean-room implementation written from the CommonMark and GFM specifications and checked by an independent similarity review; the record is in the repository under `docs/Chuvadi.Markup/cleanroom/` and `docs/Chuvadi.Markup/similarity/`. The test suite vendors the CommonMark spec (CC BY-SA 4.0), the GFM spec and html5lib-tests (MIT); none of it ships in the package.
