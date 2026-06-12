# Chuvadi.Docs

A pure-BCL Word document (`.docx`) library for .NET 10. Write professional reports, read documents, fill designed templates, and protect files with real encryption — with **zero NuGet dependencies**. The shipped DLL is a single assembly built only on the .NET Base Class Library.

Part of the [Chuvadi](https://github.com/arunshivab/chuvadi) document library family, alongside `Chuvadi.Sheets` (xlsx + zip).

## Why

- **Zero dependencies.** Nothing to audit but this library and the .NET runtime. No transitive supply chain.
- **Real encryption.** Password protection uses OOXML Agile Encryption ([MS-OFFCRYPTO]): AES-256-CBC, iterated SHA-512 key derivation (100,000 rounds), and HMAC-SHA512 integrity that is **verified on read** — a tampered file is rejected, not silently opened.
- **Hardened reading.** Decompression caps (`MaxPartBytes`) for untrusted input, DTD processing prohibited, no XXE surface.
- **Template-first report generation.** Design the document in Word; fill `{{Placeholders}}` from code with everything — styles, images, themes — preserved.

## Write a report

```csharp
using Chuvadi.Docs.Word;

var doc = new Document();
doc.AddParagraph("Quarterly Report", ParagraphStyle.Title);
doc.AddHeading("Financial Summary", 1);
doc.AddParagraph()
   .Text("Revenue grew ")
   .Text("18%", new TextFormat { Bold = true, ColorHex = "C00000" })
   .Text(" year over year.");

doc.AddBullet("Strong margins in services");
doc.AddBullet("New regional expansion", level: 1);
doc.AddNumbered("Review contracts");
doc.AddNumbered("Approve budget");

var t = doc.AddTable(3);
t.ColumnWidthsPt = new double[] { 180, 90, 110 };
t.HeaderRow("Item", "Qty", "Amount").Shade("DDE7F5");
t.AddRow("Widgets", "4", "Rs 480.00");
var total = t.AddRow();
total.Cell(0).SetText("Total", TextFormat.BoldText);
total.Cell(0).ColumnSpan = 2;
total.Cell(2).SetText("Rs 480.00", TextFormat.BoldText);

doc.Header.SetText("Acme Pvt Ltd - Confidential");
doc.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Right }
    .PageNumber(includeTotal: true)); // "Page 1 of 4"

doc.Page.Size = PageSize.A4;
doc.Page.Orientation = PageOrientation.Portrait;

doc.SaveTo("report.docx");
```

Paragraph styles: `Title`, `Heading1`–`Heading3`, `Quote`, `Normal`, plus lists, hyperlinks (`p.Hyperlink(text, url)`), page breaks (`doc.AddPageBreak()`), different first-page headers/footers (`doc.FirstPageHeader`), and landscape/Letter/Legal page setups.

## Fill a template (the recommended way to generate reports)

Design `invoice-template.docx` in Word — logo, fonts, layout, everything — with `{{Placeholders}}` where values go:

```csharp
DocxTemplate.Fill("invoice-template.docx", "invoice-0042.docx", new Dictionary<string, string>
{
    ["CustomerName"] = "Acme Pvt Ltd",
    ["InvoiceNo"]    = "0042",
    ["Total"]        = "Rs 54,000.00",
});
```

Everything in the template is preserved byte-for-byte except the replaced text — images, styles, themes, tables. Placeholders are replaced in the body, headers, footers, footnotes, and endnotes. Word frequently splits typed text across runs; spanning placeholders are detected and consolidated automatically (such a paragraph takes its first run's formatting). Unmatched placeholders are left as-is so they're visible during review.

Encrypted templates and encrypted output both work:

```csharp
DocxTemplate.Fill(template, output, values,
    password: "template-pass",
    outputEncryption: new EncryptionOptions { Password = "out-pass" });
```

## Read documents

```csharp
using var r = DocxReader.Open("contract.docx");           // add password: "..." if encrypted
foreach (var p in r.Paragraphs())
    Console.WriteLine($"[{p.StyleId}] {p.Text}");

foreach (string[][] table in r.Tables())
    Console.WriteLine($"table: {table.Length} rows");

string everything = r.ExtractText();
```

For untrusted files, cap decompression: `DocxReader.Open(path, options: new DocxReaderOptions { MaxPartBytes = 50_000_000 })`.

`Document.Load(path)` instead builds an **editable** model — paragraphs with basic run formatting (bold/italic/underline/strikethrough, font, size, color, highlight), styles, alignment, lists, and tables as text — which you can modify and save. Content the model can't represent (images, fields, comments, tracked changes, content controls) is not preserved through a load-save round-trip; use `DocxTemplate` when preservation matters.

## Images

Insert images inline (in the text flow) or floating (absolutely positioned, with text wrap):

```csharp
var doc = new Document();

// Inline — auto-sized from the file's pixel dimensions and DPI:
doc.AddImage(ImageSpec.FromFile("chart.png"));

// Inline with an explicit display size, inside a paragraph:
var p = new Paragraph();
p.Text("Logo: ").Image(ImageSpec.Inline(File.ReadAllBytes("logo.png"), "image/png", 80, 40));
doc.AddBlock(p);

// Inside a table cell:
table.Rows[0].Cell(1).SetImage(ImageSpec.FromFile("seal.jpg", widthPt: 48, heightPt: 48));

// Floating watermark, centred on the page, behind the text:
doc.AddImage(ImageSpec.Float(
    File.ReadAllBytes("wm.png"), "image/png", widthPt: 300, heightPt: 150,
    new FloatingPosition
    {
        HorizontalAnchor = HorizontalAnchor.Page, HAlign = HorizontalAlignment.Center,
        VerticalAnchor   = VerticalAnchor.Page,   VAlign = VerticalAlignment.Center,
        Wrap = TextWrap.None, BehindText = true,
    }));
```

`ImageSpec` auto-sizes from PNG, JPEG, BMP, GIF, and TIFF (reading dimensions and DPI in pure BCL); use `Inline`/`Float` with explicit points for any format, including EMF/WMF. `ScaleToWidth`/`ScaleToHeight` rescale while preserving aspect ratio.

**Reading images back:**

```csharp
using var r = DocxReader.Open("report.docx");
foreach (var img in r.Images())
    Console.WriteLine($"{img.FileName} {img.ContentType} {img.WidthPt}x{img.HeightPt}pt " +
                      $"{img.Placement} table={img.TableIndex} r{img.TableRow} c{img.TableColumn}");

r.SaveImages("./extracted");   // writes image1.png, image2.jpeg, ...
```

Every `ImageInfo` carries the raw bytes, content type, display size, placement, floating position, host (body/header/footer), and table location — everything a downstream PDF renderer needs.

**Images in templates** — two ways, set up entirely in Word:

```csharp
DocxTemplate.Fill("template.docx", "out.docx",
    textValues:  new Dictionary<string,string> { ["Customer"] = "Acme Pvt Ltd" },
    imageValues: new Dictionary<string,ImageSpec>
    {
        // (a) text-to-image: a {{Logo}} placeholder becomes this image, in place.
        ["Logo"]  = ImageSpec.FromFile("acme-logo.png", widthPt: 90, heightPt: 30),
        // (b) replace-by-alt-text: an existing template image whose Alt Text is "Seal"
        //     has its bytes swapped, keeping the template's position and size.
        ["Seal"]  = ImageSpec.FromFile("seal-2026.png"),
    });
```

## Password protection

```csharp
// Encrypt: file cannot be opened without the password.
doc.SaveTo("secret.docx", new EncryptionOptions { Password = "S3cret!" });

// Decrypt: works with Word-encrypted files too.
var loaded = Document.Load("secret.docx", "S3cret!");
// Wrong/missing password -> DocxPasswordRequiredException.
```

Compatible both directions with Microsoft Word and with the `msoffcrypto-tool` reference implementation. Integrity (HMAC-SHA512) is verified on every decrypt.

There is also cooperative "restrict editing" protection — `doc.Protect("pass", DocumentProtectionMode.ReadOnly)` — which, like Excel's sheet protection, signals intent but does not encrypt; anyone can still read the content.

## Scope

Included: paragraphs and rich run formatting, styles, lists, tables, hyperlinks, headers/footers, page setup, **images (inline and floating, read and write, in templates)**, encryption, and restrict-editing protection.

Deliberately not included yet: legacy `.doc` (pre-2007 binary format), macros (`.docm`), comments, tracked changes, footnote authoring, and multi-section documents. Page-to-image rendering is intentionally out of scope for a pure-BCL library and is planned via a future Word→PDF pipeline (the reader already exposes all image bytes and geometry that pipeline needs).

## License

MIT.
