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

Paragraph styles: `Title`, `Heading1`–`Heading9`, `Quote`, `Normal`, plus lists, hyperlinks (`p.Hyperlink(text, url)`), page breaks (`doc.AddPageBreak()`), different first-page headers/footers (`doc.FirstPageHeader`), and landscape/Letter/Legal page setups.

## Character formatting, scripts and breaks

`TextFormat` covers the full run-property set: fonts per script slot, complex-script bold/italic/size, language, every underline style, caps/small caps, hidden, outline/shadow/emboss/engrave, superscript/subscript, baseline shift, character spacing and scaling, kerning, theme colors and arbitrary shading.

```csharp
// Indic text: set the complex-script slot — Word's Font box (and TextFormat.Font) only sets Latin.
var tamil = new TextFormat { FontForAllScripts = "Nirmala UI", SizePt = 12, Bold = true, ComplexScriptLanguage = "ta-IN" };
doc.AddParagraph().Text("நோயாளி அறிக்கை — Patient report", tamil);

doc.AddParagraph().Text("H").Text("2", TextFormat.SubscriptText).Text("O, E = mc").Text("2", TextFormat.SuperscriptText);
doc.AddParagraph().Text("Ref:").Tab().Text("APL/2026/0042");
doc.Footer.Add(new Paragraph("Confidential").PositionalTab(PositionalTabAlignment.Right).PageNumber());   // right edge, whatever the tab stops
doc.AddParagraph("Apulki Medical Center\nBaner, Pune");        // \n → line break
doc.AddParagraph().Text("Part 1").Break(BreakType.Page).Text("Part 2");
```

Toggles added in 1.2 are tri-state (`bool?`): `null` inherits, `false` switches an inherited effect off.

## Paragraph formatting

Every paragraph owns a `ParagraphFormat`: spacing, line spacing, indents, pagination control, borders, shading, tab stops, outline level, bidi and East Asian typography.

```csharp
doc.AddParagraph("The patient was admitted with chest pain …").Configure(f =>
{
    f.Alignment = ParagraphAlignment.Justify;
    f.FirstLineIndentPt = 24;
    f.LineSpacing = LineSpacing.Multiple(1.5);
    f.SpaceAfterPt = 8;
});

// A boxed, shaded call-out:
doc.AddParagraph("Continue dual antiplatelet therapy for 12 months.").Configure(f =>
{
    f.Borders = ParagraphBorders.Box(new BorderSpec { WidthPt = 1.5, ColorHex = "C00000", SpacePt = 4 });
    f.Shading = Shading.Fill("FDE9E7");
});

// Invoice lines with a right-aligned, dot-leader tab:
doc.AddParagraph().Text("Angioplasty package").Tab().Text("₹ 1,85,000.00")
   .Configure(f => f.TabStops.Add(new TabStop(430, TabAlignment.Right, TabLeader.Dot)));

// Reuse settings across paragraphs (copies, never shares):
var body = new ParagraphFormat { SpaceAfterPt = 6, LineSpacing = LineSpacing.Multiple(1.15) };
doc.AddParagraph("…").ApplyFormat(body);
```

Headings run from `AddHeading(text, 1)` to `AddHeading(text, 9)`.

## Styles and effective formatting

```csharp
var doc = new Document();
doc.Defaults.RunFormat = new TextFormat { FontForAllScripts = "Nirmala UI", SizePt = 10.5 };

var note = doc.Styles.AddParagraphStyle("ClinicalNote", "Clinical Note");   // based on Normal
note.ParagraphFormat.IndentLeftPt = 18;
note.RunFormat = new TextFormat { Italic = true, ColorHex = "404040" };

var drug = doc.Styles.AddCharacterStyle("DrugName", "Drug Name");
drug.RunFormat = new TextFormat { Bold = true };

doc.AddParagraph("Patient tolerated the procedure well.").StyleId = "ClinicalNote";
doc.AddParagraph().Text("Give ").StyledText("Clopidogrel", "DrugName").Text(" 75 mg daily.");
```

To render or measure text exactly as Word would style it, resolve the effective formatting — defaults, then the paragraph style chain, then the character style chain, then direct formatting, with Word's exclusive-or rule for bold/italic/caps toggles:

```csharp
var loaded = Document.Load("report.docx");
foreach (var p in loaded.Blocks.OfType<Paragraph>())
{
    ParagraphFormat pf = loaded.GetEffectiveParagraphFormat(p);
    foreach (var run in p.Runs)
    {
        TextFormat f = loaded.GetEffectiveRunFormat(p, run);   // f.SizePt, f.Bold, f.ComplexScriptFont …
    }
}
```

## Tables

```csharp
var t = doc.AddTable(4);
t.ColumnWidthsPt = new double[] { 150, 70, 70, 110 };
t.Format.Borders = TableBorders.Horizontal(BorderSpec.Single(0.75, "7F7F7F"));
t.Format.CellMargins = new CellMargins { LeftPt = 5.4, RightPt = 5.4 };
t.Format.Caption = "Lab results";                       // read by screen readers

var head = t.HeaderRow("Test", "Result", "Unit", "Reference");   // repeats on every page
head.CantSplit = true;

var hb = t.AddRow("Haemoglobin", "13.2", "g/dL", "13.0–17.0");
hb.Cell(0).VerticalMerge = VerticalMerge.Restart;        // merge down
t.AddRow("", "4.8", "×10⁶/µL", "4.5–5.5").Cell(0).VerticalMerge = VerticalMerge.Continue;
hb.Cell(1).Shading = Shading.Fill("FFC7CE");              // flag an abnormal value
hb.Cell(2).VerticalAlignment = CellVerticalAlignment.Center;

var notes = t.AddRow();
notes.Cell(0).SetText("Remarks");
notes.Cell(0).ColumnSpan = 3;
var inner = notes.Cell(3).AddTable(2);                   // nested table
```

Table styles carry conditional formats for the header row, total row, first/last column, banding and corner cells:

```csharp
var st = doc.Styles.AddTableStyle("LabGrid", "Lab Grid");
st.TableFormat.Borders = TableBorders.Horizontal(BorderSpec.Single(0.5, "BFBFBF"));
st.Conditional(TableRegion.FirstRow).CellFormat.Shading = Shading.Fill("1F3864");
st.Conditional(TableRegion.FirstRow).RunFormat = new TextFormat { Bold = true, ColorHex = "FFFFFF" };
st.Conditional(TableRegion.OddRowBand).CellFormat.Shading = Shading.Fill("DEEAF6");
t.Format.StyleId = "LabGrid";
t.Format.Look = TableLook.FirstRow | TableLook.NoVerticalBand;

// Renderers: resolve what Word would draw for a cell, and the text inside it.
TableCellFormat cell = doc.GetEffectiveCellFormat(new TableCellRef(t, Row: 2, Column: 1));
TextFormat text = doc.GetEffectiveRunFormat(paragraph, run, new TableCellRef(t, 2, 1));
```

Regions apply in the order Word uses (whole table, column bands, row bands, first/last row, first/last column, corner cells); with no `TableLook` set anywhere, Word's default look (header row, first column, banded rows) applies. Loaded tables are capped at Word's 63 columns.

## Lists

`AddBullet` / `AddNumbered` use built-in default lists. For anything else, create lists in `doc.Numbering`:

```csharp
var steps = doc.Numbering.AddNumberedList(NumberFormat.LowerRoman, "({0})");    // (i) (ii) (iii)
doc.AddListItem("Collect the sample", steps);
doc.AddListItem("Label it", steps);

var again = doc.Numbering.Restart(steps);                 // Word's "Restart at 1"
var from10 = doc.Numbering.Restart(steps, startAt: 10);

var legal = doc.Numbering.AddList(NumberingDefinition.PerLevel(
    (NumberFormat.UpperRoman, "%1."), (NumberFormat.Decimal, "%1.%2")));
legal.Definition.Level(1).IsLegal = true;                 // I.  then 1.1 (decimals under roman)

var tamil = NumberingDefinition.Numbered(NumberFormat.Custom, "{0}.");
tamil.Level(0).CustomFormat = "௧, ௨, ௩, ...";             // Tamil digits

// Numbered headings through styles: 1. / 1.1. / 1.2. …
var outline = doc.Numbering.AddOutlineList();
outline.Definition.Level(0).ParagraphStyleId = "Heading1";
doc.Styles["Heading1"].ListInstance = outline;

// Renderers: the label Word shows for each list paragraph.
foreach (var (paragraph, label) in doc.GetListLabels())
    Console.WriteLine($"{label.Text}\t{paragraph.GetText()}");
```

Every list option is in the model: all 63 number formats, custom formats, start values, restart rules, legal numbering, suffix (tab/space/nothing), label alignment, level indents and tabs, label font, linked styles, `numStyleLink`, and per-list level overrides. Labels are computed the way Word counts them (verified against LibreOffice); formats without a built-in rendering (East Asian counting systems, Hebrew, Arabic and Thai letters, text in other languages) fall back to decimal digits in `GetListLabels`, but are still written and read exactly.

## Sections, columns, headers and watermarks

```csharp
doc.Section.PageNumbering.Format = NumberFormat.LowerRoman;   // i, ii, iii for the front matter
doc.Header.SetText("Lab Report");
doc.AddParagraph("Preface …");

var results = doc.AddSectionBreak(SectionStart.NextPage);      // new section; headers linked to the previous
results.Page.Orientation = PageOrientation.Landscape;
results.PageNumbering.Format = NumberFormat.Decimal;
results.PageNumbering.Start = 1;
results.Columns.Count = 2;
results.Columns.Separator = true;
results.EvenPageHeader.SetText("Results — even pages");        // turns on odd/even headers
results.Watermark = new TextWatermark("DRAFT");                 // Word's WordArt watermark
results.Page.SetCustomSize(288, 432);                           // or A3, A5, B5, Executive, Tabloid…
```

Every section property is in the model: start type, page size/orientation/margins/gutter/header distance, columns (equal or unequal, separator line), default/first/even headers and footers (linked or blank, with tables), page numbering (format, restart, chapter numbers), line numbers, page borders, vertical alignment, text direction, right-to-left layout and the document grid. `Document.Load` reads all of it back, headers and footers included.

## Fields, notes, bookmarks and text boxes

```csharp
doc.AddTableOfContents(1, 3);                                   // TOC field; Word fills it in on open
doc.AddHeading("Results", 1).Bookmarked("Results", "");
doc.AddParagraph("See ").InternalLink("the results", "Results").Text(" on page ").PageRef("Results");
doc.AddParagraph("Total: ").Field("REF Total \\h", "₹ 1,85,000");    // any field, with its cached result
doc.AddParagraph("Dose as per protocol").Footnote(doc, "NCCN guideline, 2026.");
doc.AddParagraph("Printed on ").Date("dd/MM/yyyy");
var box = new TextBox { WidthPt = 200, HeightPt = 80, FillHex = "FFF2CC" };
box.Paragraphs.Add(new Paragraph("Fasting from midnight."));
doc.AddParagraph("Instructions").TextBox(box);
doc.Properties.Title = "Discharge summary";
doc.Properties.SetCustom("PatientId", "P-0042");
doc.Append(Document.Load("annexure.docx"), new AppendOptions { StartSection = SectionStart.NextPage });
```

Loaded documents keep their footnotes, endnotes, bookmarks, fields, text boxes, floating tables, embedded fonts and properties through load → save.

### Comments and tracked changes

```csharp
var note = doc.Comments.Add("Dr. Arun Shiva", "Please confirm the dose.");
doc.AddParagraph("The dose is ").Commented("2 Gy per fraction", note);
doc.Comments.Reply(note, "Physicist", "Confirmed against the plan.").Done = true;

doc.TrackRevisions = true;
doc.AddParagraph("Dose: ").TrackedDelete("2 Gy", "Reviewer").TrackedInsert("1.8 Gy", "Reviewer");
foreach (var r in doc.GetRevisions()) Console.WriteLine($"{r.Mark.Type} by {r.Mark.Author}");
doc.AcceptAllRevisions();                                       // or RejectAllRevisions()
```

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

Everything in the template is preserved byte-for-byte except the replaced text — images, styles, themes, tables. Line feeds and tabs inside a value become line breaks and tabs in the same run. Placeholders are replaced in the body, headers, footers, footnotes, and endnotes. Word frequently splits typed text across runs; a split placeholder is gathered into its first run and the surrounding runs keep their own formatting. Unmatched placeholders are left as-is so they're visible during review.

### Repeating rows, blocks and conditions

```
| Test             | Result      | Unit              |        ← header row, untouched
| {{#Tests}}{{Name}} | {{Result}} | {{Unit}}{{/Tests}} |      ← repeated once per test

{{#Groups}}                    ← marker paragraphs: the paragraphs/tables between repeat
Heading: {{Title}}
  …a table with its own {{#Tests}} rows (nested repeat, item scope)…
{{/Groups}}

{{?Urgent}}URGENT{{/Urgent}}{{^Urgent}}routine{{/Urgent}}     ← inline conditions
Tags: {{#Tags}}[{{.}}]{{/Tags}}                               ← inline repeat of plain values
{{Customer.Name}}                                             ← nested object
```

```csharp
var data = new TemplateData()
    .Set("Patient", "R. Kumar")
    .SetFlag("Urgent", true)
    .Set("Flag", TemplateValue.Formatted("HIGH", new TextFormat { Bold = true, ColorHex = "C00000" }))
    .SetImage("Logo", ImageSpec.FromFile("logo.png", widthPt: 80, heightPt: 40));
foreach (var t in tests)
    data.AddItem("Tests").Set("Name", t.Name).Set("Result", t.Value).Set("Unit", t.Unit);

DocxTemplate.Fill("report-template.docx", "report.docx", data,
    new TemplateOptions { Strict = true });                   // throws DocxTemplateException listing every missing value

DocxTemplate.Fill(templateStream, outputStream, data);         // streams (non-seekable input is fine)
var placeholders = DocxTemplate.GetPlaceholders("report-template.docx");   // names, kinds, nesting, part
var problems = DocxTemplate.CheckTemplate("report-template.docx");        // unpaired / misplaced markers
```

A repeat expands table rows when its markers are in table rows (one row, a group of rows, rows inside content controls, or a one-cell row holding the whole block), paragraphs and tables when the markers stand in their own paragraphs, and run text when both markers are in one paragraph. Lookups follow the template's nesting outwards, so outer values work inside items. Filled values are never read as template syntax, so user text containing `{{…}}` stays as typed. Formatted values are layered over the placeholder's own formatting; images can be set per item, and a picture in the template whose alt text equals an image key gets its picture swapped.

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

## Untrusted input (mail attachments, uploads)

Every open applies resource limits **before anything is decompressed**, and the same limits guard `Document.Load` and `DocxTemplate.Fill`:

| Limit (`DocxReaderOptions`) | Default | Checked |
|---|---|---|
| `MaxPackageBytes` | 1 GiB (the input file/stream itself) | at open |
| `MaxEntries` | 10,000 zip entries | at open |
| `MaxTotalUncompressedBytes` | 1 GiB (sum of declared sizes — a hard ceiling the runtime enforces) | at open |
| `MaxCompressionRatio` | 1,000 : 1 for entries ≥ `CompressionRatioThresholdBytes` (1 MiB) | at open |
| `MaxSpinCount` | 500,000 (encrypted files; checked before key derivation, which the `CancellationToken` interrupts) | at open |
| `MaxXmlDepth` | 256 nested elements | while reading |
| `MaxPartBytes` | none | while reading |

`DocxTemplate.Fill` applies the same defaults; its options overload takes `DocxReaderOptions` and a `CancellationToken`. A breach throws `DocxSecurityLimitException` (a `DocxFormatException`) with `Limit`, `Actual`, `Allowed` and `PartName`. Set any limit to `null` to disable it, or start from `DocxReaderOptions.CreateUnrestricted()` for trusted input.

**Active content is detected, never executed or fetched:**

```csharp
using var r = DocxReader.Open(attachment, password: null, options, cancellationToken);
if (r.HasMacros) Warn("This document contains macros.");   // .docm/.dotm or vbaProject.bin
var a = r.GetActiveContent();
// a.HasActiveXControls, a.HasEmbeddedObjects, a.HasAltChunks, a.HasDdeFields,
// a.HasRemoteTemplate, a.HasExternalResources, a.ExternalReferences (verbatim targets)
```

**Cancellation:** `Open`, `Paragraphs`, `Tables`, `ExtractText`, `Images`, `SaveImages`, `GetActiveContent` and `Document.Load` all have `CancellationToken` overloads. The token is checked on every XML node and every decompression buffer, so cancellation lands within milliseconds as `OperationCanceledException`; the reader stays usable afterwards.


`Document.Load(path)` instead builds an **editable** model — paragraphs with full run and paragraph formatting, styles (with the theme), lists with their full numbering definitions, pictures in place with their full positioning, and structured tables with their properties, merges, nested tables and table styles — which you can modify and save. Content the model can't represent yet (charts and shapes, most fields, comments, tracked changes, content controls) is not preserved through a load-save round-trip; use `DocxTemplate` when preservation matters.

## Images

Insert images inline (in the text flow) or floating (absolutely positioned, with text wrap):

```csharp
var doc = new Document();

// Inline — auto-sized from the file's pixel dimensions and DPI:
doc.AddImage(ImageSpec.FromFile("chart.png"));

// Inline with an explicit display size, inside a paragraph:
var p = new Paragraph();
p.Text("Logo: ").Image(ImageSpec.Inline(File.ReadAllBytes("logo.png"), "image/png", 80, 40));
doc.AddParagraph(p);

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

On Windows, real-time antivirus (Microsoft Defender) scans an encrypted Office file when it is opened, by trying the default Office password, which runs the full key derivation at the file's declared spin count. That happens inside the operating system's file-open call, before the library reads a byte, so neither `MaxSpinCount` nor the `CancellationToken` can shorten it: a 3,000,000-spin file took about 13 s to open the first time on a test machine, then milliseconds once the scan result was cached. Both limits apply in full to the library's own work, and to streams that are already open or held in memory.

There is also cooperative "restrict editing" protection — `doc.Protect("pass", DocumentProtectionMode.ReadOnly)` — which, like Excel's sheet protection, signals intent but does not encrypt; anyone can still read the content.

## Scope

Included: paragraphs and rich run formatting, styles, lists, tables, hyperlinks, headers/footers, page setup, **images (inline and floating, read and write, in templates)**, encryption, and restrict-editing protection.

Deliberately not included yet: legacy `.doc` (pre-2007 binary format), writing or running macros (`.docm` files are opened and flagged, never executed), comments, tracked changes, footnote authoring, and multi-section documents. Page-to-image rendering is intentionally out of scope for a pure-BCL library and is planned via a future Word→PDF pipeline (the reader already exposes all image bytes and geometry that pipeline needs).

## License

MIT.
