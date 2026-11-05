# Chuvadi.Layout.Docs — Changelog

## 0.1.0 (unreleased)

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
