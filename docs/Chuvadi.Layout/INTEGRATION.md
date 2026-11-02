# Integrating Chuvadi.Layout

This guide covers the two kinds of host that show documents with Chuvadi.Layout:
- a **desktop viewer**, an app that shows pages in a web view (for example WebView2);
- a **server-side preview service**, which turns uploaded attachments into page images.

The working reference for both is `staging/samples/Chuvadi.Layout.Viewer`. It opens a .docx, .xlsx or .txt and writes each page as
SVG and/or PNG, together with a lazy `index.html` and a report. It supports cancellation, timeouts and untrusted-input
limits.

```
dotnet run -c Release --project staging\samples\Chuvadi.Layout.Viewer -- report.xlsx --out pages --format both --dpi 150
dotnet run -c Release --project staging\samples\Chuvadi.Layout.Viewer -- upload.docx --untrusted --timeout 20 --format png
```

## The pieces

| Package | What it gives | Used by |
|---|---|---|
| Chuvadi.Layout | `DocumentLayout.Open*`, `LaidOutDocument`, display lists, **`LayoutSvg`** (SVG writer, pure BCL) | both |
| Chuvadi.Layout.Fonts | `BundledFontSource`: OFL metric-compatible faces and Indic faces | both |
| Chuvadi.Layout.Markup | `MarkdownLayout` and `HtmlLayout`: Markdown, and sanitized HTML (mail, pages), into the same `LaidOutDocument` | both |
| Chuvadi.Layout.Pdf | `PdfShaper` (Indic shaping), `ToPng`, `ToPdf`, `ToSvg` (delegates to `LayoutSvg`). Source in `staging/src/Chuvadi.Layout.Pdf` (D-37): it takes chuvadi-pdf as pinned packages, so it is built outside `Chuvadi.slnx` and CI until chuvadi-pdf joins the monorepo | both |

Opening a document does almost no work. `DocumentLayout.Open` reads the source and lays out pages only when they are
asked for. Each `GetPage(i)` lays out as far as page *i* and no further. Laid-out pages are kept in a small cache
(`LayoutOptions.PageCacheSize`, default 8), and everything else can be rebuilt. A `LaidOutDocument` is safe to use from
several threads: one thread can run `ResolvePageCount` while another calls `GetPage`.

```csharp
var options = new LayoutOptions
{
    Culture = CultureInfo.CurrentCulture,   // decides Letter or A4 when the file states no paper (D-30)
    Shaper = new PdfShaper(),               // Tamil, Devanagari and other complex scripts
};
options.FontSources.Add(BundledFontSource.Instance);
using LaidOutDocument doc = DocumentLayout.Open(stream, LayoutSourceFormat.Auto, options, ct);
LaidOutPage page = doc.GetPage(0, ct);
```

Indic text: `PdfShaper` runs on Chuvadi.Pdf 3.26, whose shaping matches HarfBuzz 15.0.0 glyph for glyph on all nine
LiPi Indic scripts (19,641 syllables in the Pdf library's conformance corpus). Indic pages are no longer provisional.

The format is detected from the content, not from the file name:
- an OOXML package whose main part is `word/document.xml` is a docx, and one with `xl/workbook.xml` is an xlsx;
- an encrypted OLE file with `Password` set is decrypted and opened as a workbook if it holds one, else as a document;
- anything else is text.

Fixed-layout formats (PDF, scans) do not go through Layout.

### Errors and warnings

| What | How it arrives |
|---|---|
| Unreadable or unsupported file, wrong password | `LayoutFormatException` (a `LayoutException`) |
| A `LayoutLimits` value exceeded | `LayoutLimitExceededException` (`LimitName`, `Limit`, `Actual`) |
| Cancelled or timed out | `OperationCanceledException` |
| Fidelity notes (font substituted, chart drawn as a placeholder, …) | `doc.Warnings`, `doc.FontSubstitutions`; output notes through `LayoutPdfOptions.Warning` / `LayoutSvgOptions.Warning` |

Warnings never stop layout. Their codes are stable, and DESIGN.md lists them.

---

## Desktop viewer: a web view, one SVG per page, lazy

### Why SVG

`page.ToSvg()` writes Layout's own SVG, using `Chuvadi.Layout.Drawing.LayoutSvg`:
- **Glyphs** are drawn from their TrueType outlines. Each distinct glyph is one `<path>`, placed with `<use>`. The page
  looks identical whether or not the font is installed, and no font file is exposed to the web view.
- **An invisible text layer** carries the source text of every run, stretched over it. Ctrl+F in WebView2, text
  selection and copy all work when the SVG is inline in the page or opened directly.
- **Vector output** stays sharp at any zoom, so there is no re-render when the user zooms.
- **Links** are emitted as `<a>` elements, with only http, https and mailto allowed by default. Internal links go to
  `#page-N`, which the app can redirect.
- The output contains no script, no event attributes and no external references.

A typical text page is 20 to 60 KB, and repeated glyphs share one definition. Writing it takes a few milliseconds once
the page is laid out.

### Recommended flow

1. **Open on a background thread.** `DocumentLayout.Open(stream, Auto, options, ct)`:
   - give it the document's file stream, or bytes the app already holds;
   - set `AllowSystemFonts = true` so the user's installed fonts are used, and keep `BundledFontSource` as the fallback;
   - set `UseEmbeddedFonts = true` (the default).
2. **Show the first page at once.**
   - Call `GetPage(0)`, then `ToSvg()`, and navigate the WebView2 to the result.
   - Use `doc.PageCount` with `doc.IsPageCountFinal == false` as a provisional page count: "Page 1 of ~12".
3. **Resolve the count in the background.**
   - `Task.Run(() => doc.ResolvePageCount(ct))` updates the scroll bar when it finishes.
   - It shares the document lock page by page, so it never blocks `GetPage` for long.
4. **Virtualise.**
   - Use one placeholder `<div>` per page, sized from `WidthPt`/`HeightPt`: every page of a section or sheet has the
     same size.
   - Lay out a page and fetch its SVG only when its placeholder nears the viewport (an `IntersectionObserver`, or a
     `loading="lazy"` `<img>` served from a virtual host), and drop SVGs that scroll far away.
   - `LaidOutDocument` keeps its own cache, so scrolling back costs only the SVG write.
5. **Serve pages from a virtual host.**
   - Use `CoreWebView2.SetVirtualHostNameToFolderMapping`, or `WebResourceRequested` returning the SVG string as
     `image/svg+xml`.
   - Give each page a distinct `LayoutSvgOptions.IdPrefix` (for example "p12-") if pages are inlined into one HTML
     document rather than loaded as images. The viewer sample does this.
   - Inline SVG is what makes the text layer selectable. An `<img>` shows the page but gives no selection.
6. **Large images.**
   - By default images are embedded as data URIs.
   - To keep the DOM small, set `LayoutSvgOptions.ImageHref` to return a virtual-host URL and serve
     `LayoutImage.Data` from it.
7. **Provisional pages.**
   - A page with a NUMPAGES field may be built before the count is final; `page.IsProvisional` is then true.
   - Re-fetch such pages after `ResolvePageCount` completes.
8. **Status bar.**
   - Show `doc.Warnings` (ContentLoss first) and `doc.FontSubstitutions`.
   - A `NonMetricFontSubstitution` warning means line breaks may differ from Word or Excel.
9. **Print and export.** `doc.ToPdf(stream)` writes the whole document (Chuvadi.Pdf). The app's print path can take that
   PDF, or Chuvadi.Print.

```csharp
var svgOptions = new LayoutPdfOptions { Title = doc.Source.Title };
svgOptions.LayoutSvg.IdPrefix = $"p{index + 1}-";
svgOptions.LayoutSvg.PageLinkHref = i => $"#page-{i + 1}";
string svg = doc.GetPage(index, ct).ToSvg(svgOptions, ct);
```

> Only `PageLinkHref` and `AnchorHref` are called with the app's own code. External links are always filtered by
> `AllowedUriSchemes`. The app should still route navigation from the web view through `NavigationStarting` and open
> external links in the default browser.

### Cancellation

Every call takes a `CancellationToken`. Cancel the open, or a page that scrolled out of view, when the user closes the
tab or jumps away. A cancelled `GetPage` leaves the document usable; the next call continues from where layout stopped.

---

## Server-side previews: PNG, memory-only, untrusted input

Attachments are hostile until proven otherwise. The settings below bound time, memory and output size. Nothing is
written to disk: input, layout and PNG all stay in memory.

```csharp
public static async Task<IReadOnlyList<byte[]>> RenderAsync(byte[] upload, int maxPages, CancellationToken requestAborted)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
    cts.CancelAfter(TimeSpan.FromSeconds(20));                     // wall-clock budget per attachment
    var options = new LayoutOptions
    {
        Limits = LayoutLimits.ForUntrustedInput(),                 // see the table below
        AllowSystemFonts = false,                                  // same output on every server, no font probing
        Culture = CultureInfo.GetCultureInfo("en-IN"),             // or the mailbox owner's culture
        Shaper = new PdfShaper(),
        Now = DateTimeOffset.UtcNow,                               // or the message date, for DATE fields
    };
    options.FontSources.Add(BundledFontSource.Instance);

    using var input = new MemoryStream(upload, writable: false);
    using LaidOutDocument doc = DocumentLayout.Open(input, LayoutSourceFormat.Auto, options, cts.Token);
    var pngs = new List<byte[]>();
    foreach (LaidOutPage page in doc.GetPages(cts.Token))
    {
        if (pngs.Count == maxPages) break;                         // previews need the first few pages only
        pngs.Add(page.ToPng(dpi: 110, cancellationToken: cts.Token));
    }
    return pngs;
}
```

### Limits

`LayoutLimits.ForUntrustedInput()` gives:

| Limit | Untrusted | Default |
|---|---|---|
| MaxSourceBytes | 50 MiB | 256 MiB |
| MaxPages | 2,000 | 10,000 |
| MaxBlocks | 200,000 | 2,000,000 |
| MaxCellsPerSheet | 1,000,000 | 5,000,000 |
| MaxCharactersPerBlock | 200,000 | 1,000,000 |
| MaxNestingDepth | 8 | 32 |
| MaxResourceBytes (one image or font) | 16 MiB | 64 MiB |
| MaxLayoutBytes (estimated working set) | 256 MiB | 512 MiB |

Tighten them further with `Clone()` and set the values you need. Compressed packages are bounded as they are read:
- for xlsx, each decompressed part is capped at `MaxSourceBytes` (passed to Chuvadi.Sheets as `MaxPartBytes`), and
  each picture at `MaxResourceBytes`;
- for docx, Chuvadi.Docs gets `MaxSourceBytes` as its package and part caps and `MaxLayoutBytes` as its total
  uncompressed cap; its own zip-bomb, XML-depth and spin-count checks also apply.

### Rules for a preview service

- **One attachment per worker, with a budget.**
  - Run each render on its own task, under a linked `CancellationTokenSource` with a timeout.
  - Cap concurrency with a `SemaphoreSlim`, sized so that concurrency × MaxLayoutBytes fits the container.
- **Render only what is shown.**
  - Stop after the preview pages. `GetPages` is lazy, so pages that are not enumerated are never laid out.
  - Do not call `ResolvePageCount` for a preview. `PageCount` with `IsPageCountFinal == false` is enough for
    "and ~N more pages".
- **Cap the DPI.**
  - `ToPng` accepts up to 2400 dpi, but 96 to 150 suits previews.
  - A 150-dpi A4 page is 1240 × 1754 pixels, about 8.7 MB as RGBA while it is rendered; size the concurrency for it.
- **No system fonts** (`AllowSystemFonts = false`).
  - Output is then reproducible across servers, and the server's font folders are never read.
  - Fonts embedded in a docx (Chuvadi.Docs 1.6.0) are used while `UseEmbeddedFonts` is on (the default); set
    `UseEmbeddedFonts = false` to keep to the bundled faces.
- **Map errors to user-facing states:**
  - `LayoutFormatException`: "can't preview this file";
  - `LayoutLimitExceededException`: "too large to preview";
  - `OperationCanceledException` raised by the timeout: "took too long". Log `LimitName` and the elapsed time.
- **Show no warnings to the recipient.** Log codes with ContentLoss severity for triage.
- **Memory only.**
  - Pass a `MemoryStream`. The library never creates temporary files, and `ToPng` and `ToPdf` return bytes or write
    to the stream you give them.
  - Dispose the `LaidOutDocument` as soon as the PNGs are made.
- **Passwords.** An encrypted docx or xlsx needs `LayoutOptions.Password`. Without one it fails fast with
  `LayoutFormatException`; show "protected file".
- **Read the file before the clock starts (D-44).** On Windows, Microsoft Defender scans an encrypted docx/xlsx when
  it is opened from disk by trying the default password, which can take seconds inside the OS before any library code
  runs. Read the attachment into a `MemoryStream` first, then start the timeout, so "took too long" measures the
  layout and not the scan. The Viewer and the acceptance harness do this.
- **SVG for web previews.**
  - If the service's web client shows SVG instead of PNG, `LayoutSvg` output is safe to serve as `image/svg+xml`: it has no
    script, and links are limited to http, https and mailto.
  - Serve it with `Content-Security-Policy: default-src 'none'; img-src data:; style-src 'unsafe-inline'`, or show it
    through `<img>`, which disables links entirely.

---

## Documents (docx) in short

`DocumentLayout.Open` sniffs a docx; `DocumentLayout.OpenDocx(stream, DocxImportOptions, LayoutOptions)` takes options,
and `DocumentLayout.Open(Chuvadi.Docs.Word.Document, ...)` lays out a document already loaded or built.

- `PageCount` starts from the page count Word recorded (docProps/app.xml) and becomes exact when the forward pass ends.
- Watermarks, headers and footers (with Word's linking), list numbers, tables and floating pictures are laid out.
- Pictures are placed exactly where Word has them (Chuvadi.Docs 6b image runs). Fields other than PAGE, NUMPAGES and
  SECTIONPAGES show the result Word saved until Chuvadi.Docs models fields (Phase 8).

## Markdown in short

On-screen Markdown in a viewer app is Chuvadi.Markup's HTML (D-09). Printing it, or exporting it to PDF, goes through
`Chuvadi.Layout.Markup`: `MarkdownLayout.Open(text or stream, MarkdownImportOptions, LayoutOptions)` returns the same
lazy `LaidOutDocument`. Give it an `ImageResolver` for local images; it never fetches anything.

## HTML in short (mail and pages)

`HtmlLayout.Open` (in `Chuvadi.Layout.Markup`) prints HTML: it returns the same lazy `LaidOutDocument`, so pages go to
PDF, PNG or SVG exactly like a docx.

Printing a mail: lay out the same sanitized result the web view shows, so the printout matches the screen and
`cid:` images are already resolved to `data:` URIs.

```csharp
var policy = new SanitizerPolicy();                         // the safest policy: links inert, no network
policy.ResourceResolver = new DelegateResourceResolver(r =>
    r.Url.StartsWith("cid:") ? FindCidPartAsDataUri(r.Url.Substring(4)) : null);
SanitizeResult mail = HtmlSanitizer.Sanitize(htmlPartBytes, policy, mimeCharsetEncoding, ct);

var options = new LayoutOptions { Culture = userCulture, Limits = LayoutLimits.ForUntrustedInput() };
options.FontSources.Add(BundledFontSource.Instance);
options.Shaper = new PdfShaper();                          // Indic mail
using LaidOutDocument doc = HtmlLayout.Open(mail, new HtmlImportOptions { Title = subject }, options, ct);
using var pdf = new MemoryStream();
doc.ToPdf(pdf, null, ct);                                  // Chuvadi.Layout.Pdf
```

Printing a web page: `HtmlLayout.Open(bytes, new HtmlImportOptions { Policy = new SanitizerPolicy { AllowRelativeLinks = true, Links = LinkHandling.Active },
ImageResolver = LocalImageOrNull }, options)`. Bytes are decoded as a browser does (byte-order mark, `<meta>`
charset, then windows-1252); set `HtmlImportOptions.Encoding` when the charset is known from elsewhere.

- Remote images are never fetched: they print their alt text, and an ImagePlaceholder warning says how many were
  blocked.
- Links keep their targets, including inert links (read from `data-chuvadi-href`), filtered
  by `LinkResolver` (default: absolute http, https and mailto).
- Backgrounds print by default (`PrintBackgrounds`), so mail looks as on screen. A "Page N of M" footer is on by
  default.
- Inline styles and the presentational attributes of mail HTML are applied; `<style>` sheets are not yet (warning).

## Fonts in short

The font used for each run is chosen in this order:
1. fonts embedded in the document;
2. `FontSources` in order;
3. system fonts, if allowed;
4. the metric-compatible substitution table (Calibri → Carlito, Arial → Liberation Sans, …);
5. script fallback (LiPi faces for Indic text).

`doc.FontSubstitutions` reports every substitution, and whether it keeps the original metrics.

## Paper size when the file states none (D-30)

The default follows the region of `LayoutOptions.Culture`, the way Windows printer defaults do:
- **Letter** for US, CA, MX, PH, PR, BZ, CL, CO, CR, GT, NI, PA, SV and VE;
- **A4** everywhere else, including the invariant culture.

This applies to:
- xlsx sheets with no paper size;
- plain text;
- docx sections without `w:pgSz` (Chuvadi.Docs reports it as `PageSetup.SizeStated == false`, since 1.5.0).

To override it, use `XlsxImportOptions.DefaultPaperSizeCode` for xlsx or `TextImportOptions.Page` for text.
