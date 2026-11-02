# Chuvadi.Layout.Sheets — Changelog

## 0.1.0 (unreleased)

- First release as its own package (D-115): the xlsx importer of Chuvadi.Layout 0.1.0, unchanged in behaviour, on
  Chuvadi.Sheets 1.7.0. Names (D-121): `SheetsLayout.Open` (was `DocumentLayout.OpenXlsx` and
  `DocumentLayout.Open(Chuvadi.Sheets.Excel.Workbook, …)`), `SheetsImporter` (was `XlsxImporter`),
  `SheetsImportOptions` (was `XlsxImportOptions`), namespace `Chuvadi.Layout.Sheets` (was `Chuvadi.Layout.Import`).
  New: `SheetsLayout.Open(byte[], …)`.
