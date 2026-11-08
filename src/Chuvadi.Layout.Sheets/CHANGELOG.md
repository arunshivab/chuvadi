# Chuvadi.Layout.Sheets — Changelog

## 0.1.0 (unreleased)

- On Chuvadi.Sheets 2.0.2 (B12, B13): a damaged workbook with a missing workbook or worksheet part (G2), or with a
  shared-string reference that does not exist (F3), now raises `LayoutFormatException` ("The workbook could not be read:
  …", `XlsxFormatException` inside). Before, it opened with blank cells or the index shown as text. No code change in
  Layout.Sheets; the README says so.
- First release as its own package (D-115): the xlsx importer of Chuvadi.Layout 0.1.0, on Chuvadi.Sheets 2.0.0.
  Names (D-121): `SheetsLayout.Open` (was `DocumentLayout.OpenXlsx` and
  `DocumentLayout.Open(Chuvadi.Sheets.Excel.Workbook, …)`), `SheetsImporter` (was `XlsxImporter`),
  `SheetsImportOptions` (was `XlsxImportOptions`), namespace `Chuvadi.Layout.Sheets` (was `Chuvadi.Layout.Import`).
  New: `SheetsLayout.Open(byte[], …)`. `SheetsLayout.Open(Chuvadi.Sheets.Model.Workbook, …)` takes the Sheets 2.0 model
  type (namespace `Chuvadi.Sheets.Model`, was `Chuvadi.Sheets.Excel`).
- Errors on Chuvadi.Sheets 2.0 (D-150, D-167). Layout is otherwise unchanged in behaviour; every case is still a
  `LayoutFormatException` or a `LayoutLimitExceededException`:
  - A damaged encrypted workbook read with a password reports "The workbook could not be read: …" (was "The workbook
    is encrypted: … Set LayoutOptions.Password."). Without a password, and with a wrong password, it is still the
    password message.
  - A malformed workbook: `LayoutFormatException` with Chuvadi.Sheets' `XlsxFormatException` inside (was the
    `InvalidDataException` or `XmlException` itself); the original error is that exception's `InnerException`. The
    message now has Sheets' "The file is not a valid xlsx workbook: " before the original message.
  - A damaged relationship entry (for example an empty `Id`) is now a `LayoutFormatException` (Chuvadi.Sheets 1.x
    threw an `ArgumentException`, which Layout did not map).
  - A part larger than `LayoutLimits.MaxSourceBytes` once decompressed (Chuvadi.Sheets' `MaxPartBytes`) is still a
    `LayoutFormatException` with the `InvalidDataException` inside, as before.
  - `Chuvadi.Sheets.Zip` is no longer named (obsolete in Chuvadi.Sheets 2.0).
