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
using Chuvadi.Docs.Model;

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

Every save to a path (`SaveTo(path)`, `SaveTo(path, EncryptionOptions)`, `DocxTemplate.Fill(inputPath, outputPath, …)`) writes beside the target and replaces it only when the save succeeded, so a failing save never leaves a truncated file or destroys the one being overwritten. An existing file keeps its permissions (Unix) or attributes and security settings (Windows), and a symbolic link stays a link (the file it points to is written). A missing folder, a read-only target and a folder at the path fail before anything is written, with the target's name in the error.

Paragraph styles: `Title`, `Heading1`–`Heading9`, `Quote`, `Normal`, plus lists, hyperlinks (`p.Hyperlink(text, url)`), page breaks (`doc.AddPageBreak()`), different first-page headers/footers (`doc.FirstPageHeader`), and landscape/Letter/Legal page setups.

## Character formatting, scripts and breaks

`TextFormat` covers the full run-property set: fonts per script slot, complex-script bold/italic/size, language, every underline style, caps/small caps, hidden, outline/shadow/emboss/engrave, superscript/subscript, baseline shift, character spacing and scaling, kerning, theme colors and arbitrary shading.

```csharp
// Indic text: FontForAllScripts sets every slot. Word uses a complex-script font only for runs marked
// complex script (w:cs); Docs marks a run you build when it has a complex-script font and complex-script
// text (see "Complex-script runs" below). Word's Font box and TextFormat.Font set the Latin slots only.
var tamil = new TextFormat { FontForAllScripts = "Nirmala UI", SizePt = 12, Bold = true, ComplexScriptLanguage = "ta-IN" };
doc.AddParagraph().Text("நோயாளி அறிக்கை — Patient report", tamil);

doc.AddParagraph().Text("H").Text("2", TextFormat.SubscriptText).Text("O, E = mc").Text("2", TextFormat.SuperscriptText);
doc.AddParagraph().Text("Ref:").Tab().Text("APL/2026/0042");
doc.Footer.Add(new Paragraph("Confidential").PositionalTab(PositionalTabAlignment.Right).PageNumber());   // right edge, whatever the tab stops
doc.AddParagraph("Example Clinic\n12 Sample Road, Testville");        // \n → line break
doc.AddParagraph().Text("Part 1").Break(BreakType.Page).Text("Part 2");
```

### Complex-script runs (2.2.0)

Word shows a run's complex-script font (`ComplexScriptFont`, `w:rFonts/@w:cs`) only when the run is marked complex script (`w:cs`); otherwise Tamil text shows in the Latin font (checked in Word on 11 Nov 2026). So when you save, a run **you built** gets `<w:cs/>` when all of these hold:
- `ComplexScriptFont` is set (directly or through `FontForAllScripts`);
- `ComplexScript` is left unset (`ComplexScript = false` writes no flag, `true` writes it as always);
- its text has at least one character in the blocks below.

The whole run is marked, so a run mixing Tamil and English uses the complex-script font, size and bold for all of it, as Word does for text typed that way; split the run to keep English in the Latin font. Documents you load are saved as they were: their formats have `MirrorToComplexScript` off, and nothing is added.

The blocks (fixed; inclusive code points): Hebrew 0590–05FF, Arabic 0600–06FF, Syriac 0700–074F, Arabic Supplement 0750–077F, Thaana 0780–07BF, NKo 07C0–07FF, Samaritan 0800–083F, Mandaic 0840–085F, Syriac Supplement 0860–086F, Arabic Extended-B 0870–089F, Arabic Extended-A 08A0–08FF, Devanagari 0900–097F, Bengali 0980–09FF, Gurmukhi 0A00–0A7F, Gujarati 0A80–0AFF, Oriya 0B00–0B7F, Tamil 0B80–0BFF, Telugu 0C00–0C7F, Kannada 0C80–0CFF, Malayalam 0D00–0D7F, Sinhala 0D80–0DFF, Thai 0E00–0E7F, Lao 0E80–0EFF, Tibetan 0F00–0FFF, Myanmar 1000–109F, Khmer 1780–17FF, Mongolian 1800–18AF, Khmer Symbols 19E0–19FF, Vedic Extensions 1CD0–1CFF, Devanagari Extended A8E0–A8FF, Myanmar Extended-B A9E0–A9FF, Myanmar Extended-A AA60–AA7F, Hebrew presentation forms FB1D–FB4F, Arabic Presentation Forms-A FB50–FDFF, Arabic Presentation Forms-B FE70–FEFF, Rumi Numeral Symbols 10E60–10E7F, Tamil Supplement 11FC0–11FFF, Arabic Mathematical Alphabetic Symbols 1EE00–1EEFF.

This applies to documents saved with `SaveTo`; `DocxTemplate.Fill` writes the value's formatting into the template's own run properties and does not add the flag.

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

### Theme fonts and colours

A run can name a theme font (`TextFormat.AsciiThemeFont` … `ComplexScriptThemeFont`, for example `ThemeFont.MinorHAnsi`) or a theme colour (`ThemeColor.Accent1` with a tint or shade) instead of a name or a hex value. `Document.Theme` reads the document's theme (`word/theme/theme1.xml`), and two methods turn the references into names and colours:

```csharp
var loaded = Document.Load("report.docx");
string? body = loaded.GetThemeFontName(ThemeFont.MinorHAnsi);          // "Calibri"
string? tamil = loaded.GetThemeFontName(ThemeFont.MinorBidi);          // "Latha" when the file's theme-font language is ta-IN
string accent = loaded.GetThemeColorHex(ThemeColor.Accent1, tint: 0x99); // "8FAADC"
IReadOnlyDictionary<string, string> scripts = loaded.Theme.MinorFonts.ScriptFonts; // script code → typeface, for example "Taml" → "Latha"
```

- **Fonts** follow ECMA-376 §17.15.1.88: the language for the slot comes from `Document.ThemeFontLanguages` (Latin for Ascii and HAnsi, EastAsia, ComplexScript for Bidi); if the theme has a script font for that language's script, that font, otherwise the scheme's Latin, East Asian or complex-script typeface. The East Asian and complex-script typefaces are often empty in Office themes, and then the result is null when no script font applies: use the run's own font. The language → script table is Chuvadi's (Word's is not published) and covers the scripts Office themes list. When the file states no theme-font languages, Word guesses from the Office install language and the keyboards; Chuvadi follows the standard and uses the scheme's fonts.
- **Colours** are six-digit hex. `Text1`, `Background1`, `Text2`, `Background2` and the accent and hyperlink colours go through `Document.ColorSchemeMapping` (Word's default mapping when the file states none); the tint and shade change the HSL lightness, as Word does.
- **Without a theme part** (new documents, many generated files), the Office 2013–2022 defaults apply: Calibri Light and Calibri, no East Asian or complex-script font, the Office colours. `Theme.IsBuiltInDefault` says so. Chuvadi.Layout uses the same defaults.

### Document settings and compatibility mode

`word/settings.xml` holds document-wide settings. Load reads them and Save writes them back:

```csharp
var doc = new Document();
// doc.Compatibility.Mode is 15 (Word 2013 and later): Word opens the file without "[Compatibility Mode]".
doc.Compatibility.NoColumnBalance = true;          // Word's "Don't balance columns" (w:compat/w:noColumnBalance)
doc.AutoHyphenation = true;
doc.HyphenationZonePt = 18;
doc.ThemeFontLanguages.ComplexScript = "ta-IN";
doc.DecimalSymbol = ",";
```

- **`Compatibility`:** the compatibility mode (`Mode`: 11 = Word 2003, 12 = Word 2007, 14 = Word 2010, 15 = Word 2013 and later), the 65 legacy layout options of `w:compat` as `bool` properties named after their elements (`NoColumnBalance`, `UseFELayout`, `DoNotExpandShiftReturn`, …), and every other `w:compatSetting` in `OtherSettings`, kept as found. A new document is in mode 15. A loaded document keeps the file's mode, or none when the file states none (Word then opens it in compatibility mode, as it did before).
- **Also:** `ThemeFontLanguages`, `ColorSchemeMapping`, `AutoHyphenation`, `ConsecutiveHyphenLimit`, `HyphenationZonePt`, `DoNotHyphenateCaps`, `CharacterSpacingControl`, `NoPunctuationKerning`, `BookFoldPrinting`, `BookFoldReversePrinting`, `BookFoldPrintingSheets`, `DisplayBackgroundShape`, `DecimalSymbol` and `ListSeparator`, next to the older `DifferentOddAndEvenPages`, `MirrorMargins`, `GutterAtTop`, `DefaultTabStopPt`, `UpdateFieldsOnOpen`, `TrackRevisions`, `EmbedTrueTypeFonts` and `SaveSubsetFonts`.
- **Not kept** through load → save: editor state (zoom, view, proofing state, revision IDs, document variables, mail merge, the attached template). `DocxTemplate` copies the template's settings.xml unchanged.

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
var note = doc.Comments.Add("Dr. Jane Example", "Please confirm the dose.");
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

foreach (var skipped in r.SkippedParts)                    // parts the reader could not use
    Console.WriteLine($"{skipped.PartName}: {skipped.Reason}");
```

A header or footer that is missing, cannot be opened or is not well-formed XML reads as blank, as in `Document.Load`; a picture whose part is missing or cannot be opened is left out of `Images()`. Nothing is dropped silently: each such part is listed once in `SkippedParts` (`PartName`, `Reason`: `Missing`, `Unreadable` or `NotWellFormed`, and the `Error`). The list grows as the lazy readers reach the parts. The main document part is never skipped: a problem there is a `DocxFormatException`. `GetActiveContent` and the template methods stay strict: a part they cannot read is an error.

### Formatted text without loading the whole document (2.2.0)

`FormattedParagraphs()` streams each paragraph with its runs and their **effective** formatting: document defaults, the table style (in table cells), the paragraph style chain, the character style chain and the run's own formatting, resolved exactly as `Document.GetEffectiveRunFormat` resolves them. Only one paragraph, or one top-level table, is held at a time, so a viewer can show bold, italic, sizes and colours without the memory of `Document.Load`.

```csharp
using var r = DocxReader.Open(attachment, password: null, DocxReaderOptions.ForUntrustedInput(), token);
foreach (var item in r.FormattedParagraphs())
{
    for (int i = 0; i < item.Paragraph.Runs.Count; i++)
    {
        var run = item.Paragraph.Runs[i];
        if (run.IsDeleted) continue;                         // tracked deletion
        TextFormat f = item.RunFormats[i];                   // effective: f.Bold, f.Italic, f.SizePt, f.ColorHex…
        string? font = f.AsciiThemeFont is ThemeFont t ? r.GetThemeFontName(t) : f.AsciiFont;
        Show(run.TextContent, f, font);
    }
    // item.ParagraphFormat (effective alignment, indents, spacing), item.Cell (table, row, grid column), item.Part
}
```

- **Order:** the body in document order, with the paragraphs of table cells (nested tables included) in place, then the headers, then the footers (`IncludeHeadersAndFooters`). Footnotes, endnotes and comments are not read, so their references are not among the runs.
- **What each item holds:** `Paragraph` (the same model `Document.Load` builds: text, direct formatting, hyperlinks, fields, bookmarks, symbols, pictures, text boxes, tracked changes), `RunFormats` (one effective `TextFormat` per run, in order), `ParagraphFormat` (effective, list indents included), `Part` and `Cell` (`TableCellRef`, null outside tables). The objects are a read snapshot: changing them changes nothing.
- **Theme references** stay references, as in `Document`; `GetThemeFontName(ThemeFont)` and `GetThemeColorHex(ThemeColor, tint, shade)` turn them into names and colours from the file's theme and settings.
- **Styles are read once:** the styles, numbering, theme and settings parts are read on the first call and kept for the reader's life. Their elements count towards that call's `MaxElements`; each call is one operation with its own count, as for the other read calls. Cancellation and errors follow the reader's rules.
- **A missing or broken styles, numbering, theme or settings part** is listed in `SkippedParts` and the formatting falls back to the defaults without it. (`Document.Load` is stricter: it rejects malformed styles or numbering.)
- **Measured** on 10–11 MB Word-like files (2 cores, .NET 10): about 130 MB of heap at the peak against 0.55–0.9 GB for `Document.Load`, in a little less time than `Load`.

`Document.Load` keeps the same kind of list in `Document.SkippedParts`, with the same types. It covers every optional part Load left out: a header, footer, picture, theme, styles, numbering, settings, font table or embedded font, footnotes, endnotes, comments or a document-properties part that is missing, cannot be opened or is not well-formed XML. Load itself behaves as it always has (a broken header loads blank, a broken settings part leaves the defaults); the list only says what happened. It describes the load: edits don't change it, and saving doesn't use it.

## Untrusted input (mail attachments, uploads)

For files from untrusted sources (uploads on a shared server, mail attachments), start from the preset:

```csharp
var options = DocxReaderOptions.ForUntrustedInput();      // adjust as needed; a new instance on every call
var doc = Document.Load(upload, password: null, options, cancellationToken);
```

Every open applies resource limits **before anything is decompressed**, and the same limits guard `Document.Load` and `DocxTemplate.Fill`:

| Limit (`DocxReaderOptions`) | Default | `ForUntrustedInput()` | Checked |
|---|---|---|---|
| `MaxPackageBytes` | 1 GiB (the input file/stream itself) | 50 MiB | at open |
| `MaxEntries` | 10,000 zip entries | 10,000 | at open |
| `MaxTotalUncompressedBytes` | 1 GiB (sum of declared sizes — a hard ceiling the runtime enforces) | 256 MiB | at open |
| `MaxCompressionRatio` | 1,000 : 1 for entries ≥ `CompressionRatioThresholdBytes` (1 MiB) | 200 : 1 | at open |
| `MaxSpinCount` | 500,000 (encrypted files; checked before key derivation, which the `CancellationToken` interrupts) | 500,000 | at open |
| `MaxXmlDepth` | 256 nested elements | 128 | while reading |
| `MaxPartBytes` | none | 64 MiB | while reading |
| `MaxElements` (2.1.0) | none | 1,000,000 XML elements | while reading, per operation |

**Why `MaxElements`:** a loaded document's memory follows its number of XML elements, not its size in bytes. Measured on .NET 10:
- **A Word-like document:** about 200 bytes per element. 20,000 paragraphs (about 800 pages, 860,000 elements) load in 225 MB.
- **Empty paragraphs:** up to about 520 bytes per element. An 18 KB file of two million empty paragraphs takes 1.1 GB to load, though it passes every byte limit.

`MaxElements` counts every element one operation reads, across all its parts:
- one `Document.Load`;
- one template call;
- each `DocxReader` read call (each enumeration of `Paragraphs()`/`Tables()`).

It stops the operation as soon as the count passes the limit, before most of the memory is used. With the preset, the worst case measured was about 600 MB and a few seconds.

`DocxTemplate.Fill` applies the same defaults; its options overload takes `DocxReaderOptions` and a `CancellationToken`. A breach throws `DocxSecurityLimitException` (a `DocxFormatException`) with `Limit`, `Actual`, `Allowed` and `PartName`. Set any limit to `null` to disable it, or start from `DocxReaderOptions.CreateUnrestricted()` for trusted input.

**Active content is detected, never executed or fetched:**

```csharp
using var r = DocxReader.Open(attachment, password: null, options, cancellationToken);
if (r.HasMacros) Warn("This document contains macros.");   // .docm/.dotm or vbaProject.bin
var a = r.GetActiveContent();
// a.HasActiveXControls, a.HasEmbeddedObjects, a.HasAltChunks, a.HasDdeFields,
// a.HasRemoteTemplate, a.HasExternalResources, a.ExternalReferences (verbatim targets)
```

**Cancellation:** `Open`, `Paragraphs`, `FormattedParagraphs`, `Tables`, `ExtractText`, `Images`, `SaveImages`, `GetActiveContent` and `Document.Load` all have `CancellationToken` overloads. The token is checked on every XML node and every decompression buffer, so cancellation lands within milliseconds as `OperationCanceledException`.

**The token given to `DocxReader.Open` also covers the reader's later reads** (since 2.1.0):
- the read methods without a token observe it;
- the overloads with their own token observe both.

So open with a token that lives as long as the reader, for example a request's token or a timeout for the whole job. A reader whose `Open` token was cancelled can't read any more.


`Document.Load(path)` instead builds an **editable** model — paragraphs with full run and paragraph formatting, styles (with the theme), lists with their full numbering definitions, pictures in place with their full positioning, and structured tables with their properties, merges, nested tables and table styles — which you can modify and save. Fields, footnotes and endnotes, bookmarks, text boxes, comments, tracked changes and document properties survive a load-save round-trip (since 1.6.0). Content the model can't represent yet is not preserved: charts are dropped, shapes other than text boxes and old-style VML pictures keep only their text, and content controls keep their content but lose the control itself. Use `DocxTemplate` when everything else in the file must stay as it is.

## Errors

Every reading entry point — `Document.Load`, `DocxReader.Open` and its readers (`Paragraphs`, `FormattedParagraphs`, `Tables`, `ExtractText`, `Images`, `SaveImages`, `GetActiveContent`, `ContainsVbaProject`, `GetThemeFontName`, `GetThemeColorHex`), and `DocxTemplate.Fill`, `GetPlaceholders` and `CheckTemplate` — reports a file it cannot read as one exception type:

| Exception | When |
|---|---|
| `DocxFormatException` | The input is not a readable docx package: not a zip, truncated, a part that is not well-formed XML, a missing or broken `[Content_Types].xml` or relationships part, a main part that is not a Word document, missing from the package or corrupt, or an encrypted container that is damaged (opened with a password). A broken header or footer is the exception for the readers and `Document.Load`: it reads as blank and both list it in their `SkippedParts` (see **Read documents**). The original error (`XmlException`, `InvalidDataException`, …) is the `InnerException`. A read failure of the caller's own stream is reported the same way, with the `IOException` inside. |
| `DocxSecurityLimitException` (a `DocxFormatException`) | A resource limit was exceeded (see **Untrusted input**). Thrown as itself, never wrapped. |
| `DocxPasswordRequiredException` | The file is encrypted and the password is missing or wrong. Without a password the container is not read, so a damaged encrypted file also asks for one. |
| `DocxTemplateException` | A fill problem: a strict fill with missing values, or a template limit (`MaxGeneratedItems`, …). Lists every problem. |
| `OperationCanceledException` | The `CancellationToken` was cancelled. |

Caller mistakes keep their own types: `ArgumentException` / `ArgumentNullException` for bad arguments, `FileNotFoundException` and other file-system errors when opening or writing a file by path (`SaveImages` writing a picture, for example), `ObjectDisposedException` for a disposed reader. `DocxReader` lazy readers (`Paragraphs()`, `Tables()`) throw while being enumerated, not when called.

Docs.Conversion's importers report an input limit as `DocxLimitExceededException` (in `Chuvadi.Docs.Model`), with `LimitName`, `Limit` and `Actual`. Like Chuvadi.Sheets' `XlsxLimitExceededException` it derives from `IOException` and is never wrapped.

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
doc.SaveTo(outputStream, new EncryptionOptions { Password = "S3cret!" });   // to a stream (left open)

// Decrypt: works with Word-encrypted files too.
var loaded = Document.Load("secret.docx", "S3cret!");
// Wrong/missing password -> DocxPasswordRequiredException.
```

Compatible both directions with Microsoft Word and with the `msoffcrypto-tool` reference implementation. Integrity (HMAC-SHA512) is verified on every decrypt.

By default, the plaintext package of an encrypted save never reaches the disk and is never held fully in memory. It is spooled to a temporary file that is itself encrypted (AES-256, a one-time key held only in memory), created readable and writable by the current user only, opened exclusively, and deleted when the save ends. On Windows the file is also deleted if the process dies; elsewhere a file left by a crash holds only ciphertext whose key is gone. Saving to a path replaces the target only when the save succeeded (see **Write a report**).

The temporary file goes to the system temp folder (`Path.GetTempPath()`), or to a folder you choose:

```csharp
doc.SaveTo(outputStream, new EncryptionOptions { Password = "S3cret!", TempDirectory = "/var/app/tmp" });
```

If that folder is missing or can't be written to, the save throws (the system's `DirectoryNotFoundException` or `IOException`, naming the temporary file) before anything is written. `DocxTemplate.Fill` with `OutputEncryption` encrypts in memory and writes no temporary file.

On a read-only file system, or when no temporary file is wanted at all, build the package in memory instead:

```csharp
doc.SaveTo(outputStream, new EncryptionOptions { Password = "S3cret!", TempStorage = TempStorage.Memory });
```

With `TempStorage.Memory` no file is created anywhere and `TempDirectory` is ignored. The in-memory copy is not encrypted (it holds the text the document already holds in memory, and is gone when the save returns). It costs about two and a half times the package size in memory at the peak (an 11.8 MB package: about 90 MB more than the file spool, at the same speed), and a package over about 2 GB fails with an `IOException`. The encrypted file is the same either way. Chuvadi.Sheets has the same `TempStorage` setting.

On Windows, real-time antivirus (Microsoft Defender) scans an encrypted Office file when it is opened, by trying the default Office password, which runs the full key derivation at the file's declared spin count. That happens inside the operating system's file-open call, before the library reads a byte, so neither `MaxSpinCount` nor the `CancellationToken` can shorten it: a 3,000,000-spin file took about 13 s to open the first time on a test machine, then milliseconds once the scan result was cached. Both limits apply in full to the library's own work, and to streams that are already open or held in memory.

There is also cooperative "restrict editing" protection — `doc.Protect("pass", DocumentProtectionMode.ReadOnly)` — which, like Excel's sheet protection, signals intent but does not encrypt; anyone can still read the content.

## Platforms and threads

- **Platforms:** pure managed code for plain `net10.0`, with no native code and no Windows-only API. It runs the same on Windows, macOS, Linux and Android, and in server processes. The package is marked trim- and NativeAOT-compatible, and the analyzers run on every build.
- **Streams everywhere:** every method that takes a path has a stream or byte-array version. Use those for Android content URIs, uploads and cloud storage.
- **The disk:** only two things write temporary files, and nothing is read from fixed locations:
  - an encrypted `SaveTo` spools to the temp folder or `EncryptionOptions.TempDirectory`, or to memory with `TempStorage.Memory` (see **Password protection**);
  - a save to a path writes beside the target, then replaces it.
- **Threads:** the library has no mutable static state, so any number of threads can work at once, each on its own documents. A single `Document` or `DocxReader` is not safe for concurrent use: give each request or thread its own.
- **Untrusted files:** use `DocxReaderOptions.ForUntrustedInput()` and pass a `CancellationToken` (see **Untrusted input**).
- **Fonts:** none are shipped or loaded. Only font names are written into documents; showing them is the job of the program that renders the document.

## Scope

Included:
- paragraphs and rich run formatting, styles, lists, tables, hyperlinks
- multiple sections with headers and footers, page setup
- **images** (inline and floating, read and write, in templates)
- fields, footnotes and endnotes, bookmarks, text boxes, embedded fonts
- comments and tracked changes, with accept and reject
- templates, encryption, and restrict-editing protection

Conversions to and from plain text, Markdown and HTML, plus a safe HTML preview, are in the companion package [Chuvadi.Docs.Conversion](../Chuvadi.Docs.Conversion/README.md). It depends on Chuvadi.Markup; Chuvadi.Docs itself stays dependency-free.

Deliberately not included yet:
- legacy `.doc` (the pre-2007 binary format)
- writing or running macros (`.docm` files are opened and flagged, never executed)
- table-level tracked changes (cell insertions and deletions, table and row property changes)

Page-to-image rendering is intentionally out of scope for a pure-BCL library. It is planned via a future Word→PDF pipeline; the reader already exposes the image bytes and geometry that pipeline needs.

## License

MIT.
