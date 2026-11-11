# Chuvadi.Layout.Docs — Changelog

## Unreleased

Chuvadi.Docs 2.1.0 wiring (D-317; rule 5 list claude/layout-0.2.0-rule5.md). Requires Chuvadi.Docs 2.1.0.

- **Skipped parts are reported (B11, W1).** Each part in `Document.SkippedParts` is a `DocxPartSkipped` warning, with the
  part name as its location: ContentLoss for a header, footer, notes or picture, Fidelity for styles, numbering, theme,
  settings or fonts. Comments and document properties, which Layout never draws, are not reported. Before, a broken
  header printed as an empty one without a word.
- **`w:compat/w:noColumnBalance` is honoured (N3, W2).** When the file sets it, no section balances its columns.
- **Theme fonts and colours come from Chuvadi.Docs (W3, T1).** Layout's own theme1.xml parser is gone. Colours now follow
  the document's `w:clrSchemeMapping`, which Layout ignored. The theme's per-script fonts are still not used (D-191
  R1). No change on the 60 repository documents.
- **Element cap and untrusted preset (D-282 (b), W4 E1, W5 U1).** Chuvadi.Docs reads at most five XML elements per
  `LayoutLimits.MaxBlocks` (10,000,000 by default, 1,000,000 with `ForUntrustedInput()`); beyond it,
  `LayoutLimitExceededException` "DocxElements". With `ForUntrustedInput()` the reader starts from Chuvadi.Docs' own
  untrusted preset (compression ratio 200:1), with Layout's limits on top.

- An encrypted document opened without a password, or with a wrong one, is `LayoutPasswordRequiredException` (a
  `LayoutFormatException`; D-253), with `PasswordGiven` false or true. Messages: "The document is encrypted: set
  LayoutOptions.Password. …" and "The password for this document is wrong: …", each followed by the Chuvadi.Docs
  message. Before, a plain `LayoutFormatException` ending "Set LayoutOptions.Password.".
- A legacy .doc (or any OLE file without the encryption streams) is never this exception: with the shared decryptor of
  D-293 it is "The document could not be read: …", naming the old format, with or without a password.

## 0.1.0 (2026-11-09)

- Trim- and NativeAOT-compatible: `IsTrimmable` and `IsAotCompatible` are set, so the analyzers check every build (D-217,
  D-254).
- Tamil and other complex-script text (R3, D-191): a run takes the complex-script font, size and bold only with
  `w:cs` or `w:rtl` (ECMA-376 17.3.2.26 step 2a). Otherwise Hebrew, Arabic, Syriac and Thaana take the ASCII font, as
  the standard's table says, and Tamil and the other Indic scripts the High ANSI font, size and bold (standard silent;
  follows Word 2021, measured with calib-tamil-slots). The language tag and `w:hint="cs"` do not change this. Where the
  font has no Tamil letters Layout draws LiPi Sans Tamil; Word draws Vijaya (docs/DIFFERENCES.md, L-4).

- Cell margins (F3, D-181): when neither the table, its style nor Table Normal states them, 115 twips (5.75 pt) left
  and right, as ECMA-376 17.4.34 and 17.4.11 give (was 5.4 pt, Word's Table Normal value). Word uses about 0 pt in
  such a file (docs/DIFFERENCES.md, L-1).
- The empty paragraph that ends a cell after a nested table takes no height (F2, D-181; standard silent, follows
  Word in every mode). A paragraph with content there keeps its line.

- Floating tables in the body: `w:tblpPr` and `w:tblOverlap` are read completely into `FlowTable.Floating` (anchors,
  offsets, relative positions, the four distances from text). Omitted anchors are the page and omitted offsets and
  distances 0, as ECMA-376 17.4.57 says (Word reads omitted anchors as the margin and the column, and offsets 1 twip
  smaller; Layout follows the file). The `UnsupportedFeature` warning for body floating tables is gone. A floating
  table inside a table cell, header, footer, note or text box keeps the earlier behaviour (horizontal position as an
  indent, laid out in its container's flow) with a reworded warning.

- Newspaper columns: `w:cols` is read completely: `w:num`, `w:space` (720 twips when not stated), `w:sep` and, with
  `w:equalWidth="0"`, each `w:col` width and space after it (`FlowSection.ColumnSpacing`, `ColumnSeparator`,
  `ColumnWidths`). Body content is read at the section's column width; a top-level table without `w:tblGrid` widths
  (and not floating) is marked `GridFromLayoutWidth` so the engine sizes it in the column it lands in.
- `w:compat/w:doNotBalanceTextColumns` is not read yet (Chuvadi.Docs has no settings model; requested): columns are
  always balanced before a continuous break.

- First release as its own package (D-115): the docx importer of Chuvadi.Layout 0.1.0, unchanged in behaviour, on
  Chuvadi.Docs 2.0.0. Names (D-121): `DocsLayout.Open` (was `DocumentLayout.OpenDocx` and
  `DocumentLayout.Open(Chuvadi.Docs.Word.Document, …)`), `DocsImporter` (was `DocxImporter`), `DocsImportOptions`
  (was `DocxImportOptions`), namespace `Chuvadi.Layout.Docs` (was `Chuvadi.Layout.Import`). New: `DocsLayout.Open(byte[], …)`.
  `DocsLayout.Open(Chuvadi.Docs.Model.Document, …)` takes the Docs 2.0 model type (namespace `Chuvadi.Docs.Model`, was
  `Chuvadi.Docs.Word`).
