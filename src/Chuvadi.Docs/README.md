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

## Scope (v1)

Deliberately not included: legacy `.doc` (pre-2007 binary format), macros (`.docm`), images, comments, tracked changes, footnote authoring, and multi-section documents. Images and comments are the most likely v1.1 additions.

## License

MIT.
