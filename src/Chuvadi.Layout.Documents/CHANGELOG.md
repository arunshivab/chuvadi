# Chuvadi.Layout.Documents — Changelog

## Unreleased

- `AutoDetectLayout.Open` reports a missing or wrong password as `LayoutPasswordRequiredException` (D-253), with
  `PasswordGiven`. An OLE file without a password now goes to Chuvadi.Layout.Docs too, instead of the engine's
  message "an encrypted Office document (set LayoutOptions.Password) or a legacy .doc/.xls", so the app gets the typed
  exception.
- A legacy .doc or .xls is never the password exception (shared decryptor, D-293). Through `AutoDetectLayout` it is
  `LayoutFormatException` "The file could not be read: …", whose `InnerException` is the Chuvadi.Sheets
  `XlsxFormatException` naming the old format. Any OLE file that neither Docs nor Sheets can read now says "file"
  where it said "workbook" (D-301). A compound file whose directory cannot be read may be a damaged encrypted file, so
  it still asks for the password.
- A wrong password is reported from the document step, so its message says "document" for a workbook too.

## 0.1.0 (2026-11-09)

- Trim- and NativeAOT-compatible: `IsTrimmable` and `IsAotCompatible` are set, so the analyzers check every build (D-217,
  D-254).
- First release (D-115): `AutoDetectLayout.Open(Stream / byte[], format, options, ct)`, the former behaviour of
  `DocumentLayout.Open(Stream, Auto)` for every format: docx, xlsx, encrypted files of either kind (with
  `LayoutOptions.Password`) and text.
