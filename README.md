# Chuvadi

**Pure-BCL document libraries for .NET — zero NuGet dependencies.**

*Chuvadi* (சுவடி) is the Tamil word for a palm-leaf manuscript bundle — documents, preserved carefully. This monorepo is a family of independent NuGet packages that read and write document formats using nothing but the .NET Base Class Library: a supply chain you can audit in one sitting.

| Package | Formats | Status |
|---|---|---|
| **[Chuvadi.Sheets](src/Chuvadi.Sheets/README.md)** | xlsx (read/write/encrypt), zip | v2.0.0 |
| **[Chuvadi.Docs](src/Chuvadi.Docs/README.md)** | docx (read/write/encrypt/templates) | v1.6.0 |
| **[Chuvadi.Docs.Conversion](src/Chuvadi.Docs.Conversion/README.md)** | Docs to and from plain text, Markdown (CommonMark + GFM) and HTML, plus a self-contained HTML preview (over Chuvadi.Markup; released with Docs) | v1.6.0 |
| **[Chuvadi.Fonts](src/Chuvadi.Fonts/README.md)** | the LiPi Sans fonts (Latin and nine Indic scripts) as static TrueType files | v1.0.0 |
| **[Chuvadi.OpenType](src/Chuvadi.OpenType/README.md)** | reads OpenType and TrueType fonts: tables, collections, cmap, metrics, glyph bounds, names | v0.1.0 (in progress) |
| **[Chuvadi.Text.Shaping](src/Chuvadi.Text.Shaping/README.md)** | OpenType text shaping for Indic scripts and Latin, with its own Unicode tables (over Chuvadi.OpenType) | v0.1.0 (in progress) |
| **[Chuvadi.Layout](src/Chuvadi.Layout/README.md)** | page layout engine: flow and grid pages, plain text, display list, SVG | v0.1.0 (preview) |
| **[Chuvadi.Layout.Docs](src/Chuvadi.Layout.Docs/README.md)** | Word documents (docx) laid out through Chuvadi.Docs | v0.1.0 (preview) |
| **[Chuvadi.Layout.Sheets](src/Chuvadi.Layout.Sheets/README.md)** | Excel workbooks (xlsx) laid out through Chuvadi.Sheets | v0.1.0 (preview) |
| **[Chuvadi.Layout.Documents](src/Chuvadi.Layout.Documents/README.md)** | one call for docx, xlsx (encrypted too) and text, format detected | v0.1.0 (preview) |
| **[Chuvadi.Layout.Fonts](src/Chuvadi.Layout.Fonts/README.md)** | offline OFL font pack for Chuvadi.Layout (Office metric-compatible substitutes, Gelasio, Selawik) | v0.1.0 (preview) |
| **[Chuvadi.Layout.Markup](src/Chuvadi.Layout.Markup/README.md)** | Markdown and sanitized HTML (mail, pages) layout through Chuvadi.Markup | v0.1.0 (preview) |
| **[Chuvadi.Markup](src/Chuvadi.Markup/README.md)** | Markdown (CommonMark + GFM), HTML5 parse/sanitize, CSS, text to HTML | v1.0.0 |
| **[Chuvadi.Print](src/Chuvadi.Print/README.md)** | printing core: print settings, job planning, spool envelope, PNG preview | v1.0.0-rc.1 (in progress) |
| **[Chuvadi.Print.Windows](src/Chuvadi.Print.Windows/README.md)** | Windows printing through the GDI spooler (needs Chuvadi.Print) | v1.0.0-rc.1 (in progress) |
| **[Chuvadi.Print.Ipp](src/Chuvadi.Print.Ipp/README.md)** | driverless IPP printing, PWG Raster (needs Chuvadi.Print) | v1.0.0-rc.1 (in progress) |
| **[Chuvadi.Print.Remote](src/Chuvadi.Print.Remote/README.md)** | remote printing: hub and agent (needs Chuvadi.Print) | v1.0.0-rc.1 (in progress) |
| **[Chuvadi.Zip](src/Chuvadi.Zip/README.md)** | zip (streaming read/write, Zip64, AES-256, zip-slip and bomb limits) | v1.0.0 |
| [Chuvadi.Pdf](https://github.com/arunshivab/chuvadi-pdf) | pdf | v2.7.0 (separate repo) |

## Shared foundation

Both OOXML packages source-link the same internals from [`shared/Chuvadi.Internal`](shared/Chuvadi.Internal): OPC packaging, and the full [MS-OFFCRYPTO] Agile Encryption stack — AES-256-CBC, iterated SHA-512 key derivation, **HMAC-SHA512 verified on every decrypt**, CFB container with DIFAT support, plus hardening utilities (decompression caps, bounded streams). A security fix lands once and ships in every package; each DLL remains a single, zero-dependency assembly.

## Quick taste

```csharp
// Excel
var wb = new Workbook();
wb.Sheet("Q2").Cell("A1").Value("Revenue").Bold();
wb.SaveTo("q2.xlsx", new EncryptionOptions { Password = "secret" });

// Word
var doc = new Document();
doc.AddParagraph("Quarterly Report", ParagraphStyle.Title);
doc.SaveTo("report.docx");

// Word templates: design in Word, fill from code
DocxTemplate.Fill("invoice-template.docx", "invoice-0042.docx",
    new Dictionary<string, string> { ["Total"] = "Rs 54,000.00" });

// Layout: paginate a Word or Excel file into pages (SVG here; PDF through Chuvadi.Layout.Pdf)
using var laidOut = AutoDetectLayout.Open(File.OpenRead("report.docx"));   // Chuvadi.Layout.Documents
for (int i = 0; i < laidOut.ResolvePageCount(); i++)
{
    using var svg = File.Create($"page-{i + 1}.svg");
    LayoutSvg.Write(laidOut.GetPage(i), svg);
}
```

## Verification

Every package ships with an xUnit suite plus an end-to-end manual verification suite, both run in CI on Ubuntu and Windows. Generated files are cross-validated against independent implementations (openpyxl, python-docx, msoffcrypto-tool). CI also greps the shipped projects to enforce the zero-dependency promise. See [SECURITY.md](SECURITY.md) and [AUDIT_REPORT.md](AUDIT_REPORT.md).

## Releases

Per-package tags (`docs-v*` releases Chuvadi.Docs and Chuvadi.Docs.Conversion together): `sheets-v2.0.0`, `docs-v*` (version pending), `zip-v1.0.0`, `markup-v1.0.0`, `fonts-v1.0.0`, `layout-v0.1.0`, `layout-docs-v0.1.0`, `layout-sheets-v0.1.0`, `layout-documents-v0.1.0`, `layout-fonts-v0.1.0`, `layout-markup-v0.1.0`, `opentype-v0.1.0`, `text-shaping-v0.1.0`. See [RELEASING.md](RELEASING.md).

## License

MIT.
