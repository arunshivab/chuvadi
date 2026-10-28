# Chuvadi.Markup

Pure-BCL Markdown, HTML and CSS for .NET — zero NuGet dependencies, part of the [Chuvadi](https://github.com/arunshivab/chuvadi) family. Built for untrusted input: every entry point takes a `CancellationToken`, enforces `MarkupLimits`, and documents its exceptions.

What 1.0 gives you:

- **Markdown**: a CommonMark 0.31.2 parser (all 652 spec examples) with the GFM extensions — tables, task lists, strikethrough, extended autolinks — and the tag filter. A public, editable **AST** with source positions, an **HTML renderer** (cmark-identical output; raw HTML escape / strip / passthrough; unsafe URLs removed by default) and a **Markdown writer** that turns any AST back into Markdown.
- **HTML**: the WHATWG tokenizer and tree builder — the DOM a browser builds, including adoption agency, foster parenting, templates, SVG/MathML and fragment parsing (the full html5lib tokenizer and tree-construction suites pass, including `<selectedcontent>` mirroring). Charset sniffing from bytes (BOM, caller, `<meta>` prescan, windows-1252 fallback, all WHATWG labels .NET supports). A small editable DOM and a spec serializer.
- **CSS**: a CSS Syntax Level 3 tokenizer and parser (style sheets, declarations, nesting) and a safe serializer.
- **Sanitizer**: allowlist sanitizer with two ready policies — **`SanitizerPolicy.Anjal()`** (strict, for mail: no forms, only `data:` images, links inert, all network blocked) and **`SanitizerPolicy.Reader()`** (standard viewer; still no network) — plus `Empty()` to build your own. Style sheets are sanitized and scoped under a wrapper class, ids and document-global CSS names (fonts, keyframes, counter styles) are prefixed, `cid:` and relative URLs go through your resolver, blocked resources are reported, and the output is re-parsed until it is a fixed point (mutation-XSS safe).
- **Plain text**: text or `format=flowed` mail to safe HTML, with quote levels, autolinks (live, inert or rewritten) and three layouts.

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

// Untrusted mail HTML (Anjal): strict policy, network blocked, links inert
var policy = SanitizerPolicy.Anjal();
policy.ResourceResolver = new DelegateResourceResolver(r =>
    r.Url.StartsWith("cid:") ? FindCidPartAsDataUri(r.Url.Substring(4)) : null);
SanitizeResult mail = HtmlSanitizer.Sanitize(mimeHtmlPartBytes, policy, encoding: null, ct);
Console.WriteLine(mail.Html);                      // <div class="chuvadi-content">...</div>
foreach (var b in mail.BlockedResources)           // e.g. offer "show images"
    Console.WriteLine($"{b.Kind} {b.Url}");

// Documents in Reader: standard policy
SanitizeResult page = HtmlSanitizer.Sanitize(untrustedHtml, SanitizerPolicy.Reader(), ct);

// Plain-text mail -> safe HTML
string body = PlainText.ToHtml(text, new TextToHtmlOptions { FormatFlowed = true, Links = LinkHandling.Inert }, ct);

// The HTML parser on its own: the DOM a browser would build
HtmlDocument dom = HtmlParser.Parse(untrustedHtml, new HtmlParserOptions { Limits = MarkupLimits.Strict() }, ct);
```

Show sanitizer output inside a page that also carries a Content-Security-Policy such as
`default-src 'none'; img-src data:; style-src 'unsafe-inline'` — defence in depth.

Exceptions: `ArgumentNullException` for null input, `ArgumentOutOfRangeException` for invalid limits, `MarkupLimitExceededException` (with `LimitName`, `Limit`, `Actual`) when a hard limit is exceeded, `OperationCanceledException` on cancellation. Malformed input never throws: it is parsed the way a browser (or cmark) would parse it.

MIT licensed. The test suite vendors the CommonMark spec (CC BY-SA 4.0), the GFM spec and html5lib-tests (MIT); none of it ships in the package.
