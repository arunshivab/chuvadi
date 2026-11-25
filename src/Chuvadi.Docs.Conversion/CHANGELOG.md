# Chuvadi.Docs.Conversion — Changelog

## 2.4.1 (2026-11-25)

Versioned with Chuvadi.Docs 2.4.1 (D-15, D-45; the `docs-v*` tag releases both at one version): depends on **Chuvadi.Docs 2.4.1** and Chuvadi.Markup 1.2.0. No Conversion code changed.
- **What it gets from Chuvadi.Docs 2.4.1:** the safe default reader limits (compression ratio 200:1, 2,000,000 XML elements, content nesting 64) apply when you load the `Document` you convert. Conversion itself never loads a .docx: imports build a new document and exports take the one you pass.
- **Every document the defaults admit converts on a 256 KB thread stack** (the stack of an IIS worker thread): new test group `DeepDocuments` (5 checks, with an xUnit fact) exports 64 nested tables, hyperlinks and insertions, 32 tables with 32 hyperlinks inside, and 16 nested text boxes to HTML, the HTML preview, Markdown and text.
- **No output change:** every file the Conversion verification suite writes is the same as with Docs 2.4.0, apart from docProps.
- **The new Docs 2.4.1 API** (`DocxReaderOptions.ForTrustedInput()`, `MaxNestingDepth`, `DocxLimit.NestingDepth`) is in Chuvadi.Docs. See its CHANGELOG.

## 2.4.0 (2026-11-15)

Versioned with Chuvadi.Docs 2.4.0 (D-15, D-45): depends on **Chuvadi.Docs 2.4.0 and Chuvadi.Markup 1.2.0** (1.1.0 before; Markup 1.2.0 was released on 15 Nov). No Conversion code changed.
- **No output change, measured.**
  - **Docs 2.4.0:** imports build body paragraphs only, and exports read loaded documents, so the header-paragraph rule (O1: an explicit Normal on a header paragraph keeps Normal) doesn't reach them. All Conversion test outputs are the same as with 2.3.0.
  - **Markup 1.2.0:** every input of Markup's gate data (10,423 inputs) was run through five Conversion modes (Markdown import with GFM, with CommonMark, with soft breaks as line breaks; HTML import; text import), and each whole result was compared: every docx part except docProps, the warnings and blocked resources, and the Markdown, HTML and text export. Docs 2.3.0 with Markup 1.1.0, Docs 2.3.0 with Markup 1.2.0 and Docs 2.4.0 with Markup 1.2.0 give the same result on all 52,115 runs.
  - **Markup's stricter `data:image/` rule (D-303)** changes nothing either: Conversion's own image rules already refused `data:image/pngx` and `data:image/svg+xml` and kept `data:image/png`, in HTML and Markdown import, with both Markup versions.
- **The new Docs 2.4.0 API** (`DocxReaderOptions.IncludeNotes`, `IncludeComments`, `IncludeTextBoxes`, `DocxFormattedParagraph.ListLabel`, `Note`, `Comment`, `TextBox`, `Host`, `Paragraph.IsStyleSet` and `ResetStyle()`) is in Chuvadi.Docs. See its CHANGELOG.

## 2.3.0 (2026-11-13)

Versioned with Chuvadi.Docs 2.3.0 (D-15, D-45): depends on Chuvadi.Docs 2.3.0 and Chuvadi.Markup 1.1.0. No Conversion code changed. What it gets from Chuvadi.Docs 2.3.0:
- **Imports into a new document get Word's paragraph spacing** (S1, D-371): Markdown, HTML and text imports without a `target` document create a new `Document`, whose defaults are now 8 pt after and 1.08 lines. Imported body paragraphs sit like a new Word document's; the code style keeps its own 0 pt and single lines. Imports into a `target` keep that document's defaults, so `target: new Document(DocumentPreset.Minimal)` imports with no default spacing, as before. All 37 Conversion test packages differ in `word/styles.xml`.
- **Header and footer paragraphs of a new document use Word's Header and Footer styles** (D-374): every new document's `styles.xml` gains the two styles. A Word-preset document's header or footer paragraphs in Normal are saved in them (one test package, `md-export-source.docx`, also differs in its header part). Exports with `IncludeHeadersFooters` read header paragraphs through the effective formats, so they see the Header style; the exported test outputs (Markdown, HTML, text) are unchanged. Imports write no headers.
- **The new Docs 2.3.0 API** (`DocumentPreset` and `new Document(DocumentPreset)`, `DocumentDefaults.StandardParagraphFormat`, the settable `ParagraphFormat`, `HeaderParagraphStyleId` and `FooterParagraphStyleId`) is in Chuvadi.Docs. See its CHANGELOG.

## 2.2.0 (2026-11-12)

Versioned with Chuvadi.Docs 2.2.0 (D-15, D-45): depends on Chuvadi.Docs 2.2.0 and Chuvadi.Markup 1.1.0. No Conversion code changed. What it gets from Chuvadi.Docs 2.2.0:
- **Faster exports of large loaded documents:** `Document.Load` is about twice as fast, with the same document as before (DOC-10).
- **Imported runs with a font and complex-script text are saved with `<w:cs/>`** (N4, D-343), so Word shows that font: the importers give a run a font (all four slots, `FontForAllScripts`) for inline code (`CodeFont`, Consolas by default) and for an HTML `font-family` or `<font face>`. Such a run whose text has complex-script characters (Tamil, Devanagari, Arabic… the Docs README lists the blocks) is now marked complex script. Runs without a font, and runs with only Latin, Greek, Cyrillic or CJK text, are written as before.
- **The new Docs 2.2.0 API** (`DocxReader.FormattedParagraphs`, the reader's theme lookups) is in Chuvadi.Docs. See its CHANGELOG.

## 2.1.0 (2026-11-10)

Versioned with Chuvadi.Docs 2.1.0 (D-15, D-45): depends on Chuvadi.Docs 2.1.0 and Chuvadi.Markup 1.1.0. No Conversion code changed. What it gets from Chuvadi.Docs 2.1.0:
- **The documents it creates are saved in compatibility mode 15** (Word 2013 and later). Their `word/settings.xml` holds one `w:compat` element, so Word no longer opens them as "[Compatibility Mode]" (D-299). Nothing else in the output changes; set `doc.Compatibility.Mode = null` on the returned `Document` before saving for the 2.0.0 output. Importing into a `Document` you pass keeps that document's own settings.
- **The new Docs 2.1.0 API works on the `Document` it returns:** settings, theme lookups, `TempStorage` and `SkippedParts`. See the Chuvadi.Docs CHANGELOG.

## 2.0.0 (2026-11-09)

The first release. It is versioned with Chuvadi.Docs 2.0.0 (D-15, D-45) and depends on Chuvadi.Docs 2.0.0 and Chuvadi.Markup 1.1.0. Everything listed under 1.6.0 below is part of it; 1.6.0 was never released (D-135).

Changes since the 1.6.0 notes:
- **Namespace:** the Docs types it takes and returns are in `Chuvadi.Docs.Model` (was `Chuvadi.Docs.Word`).
- **Limits are reported as `DocxLimitExceededException`** (an `IOException`, from Chuvadi.Docs, with `LimitName`, `Limit` and `Actual`), from all 16 public import methods.
  - Markdown and HTML: Markup's own limit values, with Markup's `MarkupLimitExceededException` as `InnerException`. It was Markup's exception before.
  - Text: `MaxInputChars`. It was an `InvalidDataException` before.
  - The call produces no partial result. Callers no longer need a reference to Chuvadi.Markup to catch it.
- **Documentation:** Markdown is described as read "as the CommonMark 0.31.2 and GFM 0.29 specifications describe". It used to say "as cmark-gfm reads it", naming another implementation (D-196).
- **Trim and NativeAOT:** the package is marked `IsTrimmable` and `IsAotCompatible`, and the analyzers run on every build (D-217).
- Documentation: the README names Markup's default `SanitizerPolicy` (the `Anjal()` method the old text referred to no longer exists), and has a platforms and threads note.

## 1.6.0 (never released; ships in 2.0.0)

Planned as the first release, versioned with Chuvadi.Docs 1.6.0 (decision D-15), on Chuvadi.Docs 1.6.0 and Chuvadi.Markup 1.0.0.

- **Plain text** (`TextConversion`):
  - **Import:**
    - One paragraph per line, or blank-line paragraphs joined with line breaks or re-flowed.
    - Tabs become tab runs and form feeds become page breaks.
    - BOM, CR / LF / CRLF / U+2028 line endings, control characters and size cap handled.
  - **Export:**
    - Word's exact list labels, tables as tab-separated rows and notes at the end.
    - Page breaks, headers and footers, and the tracked-changes view.
    - Configurable line endings.
- **Markdown** (`MarkdownConversion`, parsed and written with Chuvadi.Markup: CommonMark 0.31.2 plus GFM tables, task lists, strikethrough and autolinks):
  - **Import:**
    - Headings, emphasis, strong, strikethrough and code.
    - Lists: nested, start numbers and task markers.
    - Nested quotes and code blocks (a Code style is created when missing).
    - Tables with header row and alignment.
    - Links and fragment links, and pictures (data URIs or the host's resolver).
    - Breaks and thematic breaks.
    - Raw HTML is sanitized and converted, kept as text, or dropped.
  - **Export:**
    - The same constructs, plus `ToAst` for callers.
    - Formatting is written relative to the paragraph style.
    - Escaping and output are verified by re-parsing: a Markdown → docx → Markdown corpus renders the same HTML.
- **HTML** (`HtmlConversion`):
  - **Import:**
    - The input passes through Chuvadi.Markup's sanitizer (`HtmlImportOptions.DefaultPolicy()`) before it is mapped.
    - Blocks, headings, inline formatting and a safe subset of inline CSS.
    - Lists, quotes, `pre`, definition lists and rules.
    - Tables with header, colspan, rowspan, alignment, shading and caption (up to Word's 63 columns).
    - Ids become bookmarks for internal links; `data:` pictures; byte input with charset sniffing.
    - `ImportDetailed` reports warnings and blocked remote resources.
  - **Export:**
    - A full page or a fragment, with semantic or inline formatting and semantic or label lists.
    - Notes with back links, pictures, and a cut after a number of blocks.
    - Unsafe link schemes are never written.
  - **Preview:** `ExportPreview` / `HtmlExportOptions.Preview()` gives a self-contained fragment that Markup's default sanitizer policy removes nothing from.
