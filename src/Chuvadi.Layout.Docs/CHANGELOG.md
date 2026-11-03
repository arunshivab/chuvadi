# Chuvadi.Layout.Docs — Changelog

## 0.1.0 (unreleased)

- Newspaper columns: `w:cols` is read completely: `w:num`, `w:space` (720 twips when not stated), `w:sep` and, with
  `w:equalWidth="0"`, each `w:col` width and space after it (`FlowSection.ColumnSpacing`, `ColumnSeparator`,
  `ColumnWidths`). Body content is read at the section's column width; a top-level table without `w:tblGrid` widths
  (and not floating) is marked `GridFromLayoutWidth` so the engine sizes it in the column it lands in.
- `w:compat/w:doNotBalanceTextColumns` is not read yet (Chuvadi.Docs has no settings model; requested): columns are
  always balanced before a continuous break.

- First release as its own package (D-115): the docx importer of Chuvadi.Layout 0.1.0, unchanged in behaviour, on
  Chuvadi.Docs 1.6.0. Names (D-121): `DocsLayout.Open` (was `DocumentLayout.OpenDocx` and
  `DocumentLayout.Open(Chuvadi.Docs.Word.Document, …)`), `DocsImporter` (was `DocxImporter`), `DocsImportOptions`
  (was `DocxImportOptions`), namespace `Chuvadi.Layout.Docs` (was `Chuvadi.Layout.Import`). New: `DocsLayout.Open(byte[], …)`.
