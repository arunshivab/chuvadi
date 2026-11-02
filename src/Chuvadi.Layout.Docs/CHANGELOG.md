# Chuvadi.Layout.Docs — Changelog

## 0.1.0 (unreleased)

- First release as its own package (D-115): the docx importer of Chuvadi.Layout 0.1.0, unchanged in behaviour, on
  Chuvadi.Docs 1.6.0. Names (D-121): `DocsLayout.Open` (was `DocumentLayout.OpenDocx` and
  `DocumentLayout.Open(Chuvadi.Docs.Word.Document, …)`), `DocsImporter` (was `DocxImporter`), `DocsImportOptions`
  (was `DocxImportOptions`), namespace `Chuvadi.Layout.Docs` (was `Chuvadi.Layout.Import`). New: `DocsLayout.Open(byte[], …)`.
