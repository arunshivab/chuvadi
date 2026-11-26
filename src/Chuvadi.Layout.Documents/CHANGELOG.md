# Chuvadi.Layout.Documents — Changelog

## 0.3.1 (2026-11-26)

Security patch (D-406). Requires Chuvadi.Layout.Docs 0.3.1 and Chuvadi.Layout.Sheets 0.3.1, so `AutoDetectLayout` gets
the safe defaults and every fix of Layout 0.3.1. No code change.

## 0.3.0 (2026-11-15)

Dependency update (D-383). Requires Chuvadi.Layout.Docs 0.3.0 and Chuvadi.Layout.Sheets 0.3.0, so an application
gets **Chuvadi.Docs 2.3.0** (with its Word preset for documents built in code) and **Chuvadi.Sheets 2.1.0**. No code
change; see those two changelogs for what can look different.

## 0.2.0 (2026-11-12)

- `AutoDetectLayout.Open` reports a missing or wrong password as `LayoutPasswordRequiredException` (D-253), with
  `PasswordGiven`. An OLE file without a password now goes to Chuvadi.Layout.Docs too, instead of the engine's
  message "an encrypted Office document (set LayoutOptions.Password) or a legacy .doc/.xls", so the app gets the typed
  exception.
- A legacy .doc or .xls is never the password exception (shared decryptor, D-293). Through `AutoDetectLayout` it is
  `LayoutFormatException` "The file could not be read: …", whose `InnerException` is the Chuvadi.Sheets
  `XlsxFormatException` naming the old format. Any OLE file that neither Docs nor Sheets can read now says "file"
  where it said "workbook" (D-301). A compound file whose directory cannot be read may be a damaged encrypted file, so
  without a password it still asks for one; with a password it is `LayoutFormatException` "The file could not be
  read: …", because Chuvadi.Docs 2.1.0 and Chuvadi.Sheets both report a damaged container as a format error (D-300,
  D-305).
- A wrong password is reported from the document step, so its message says "document" for a workbook too.

## 0.1.0 (2026-11-09)

- Trim- and NativeAOT-compatible: `IsTrimmable` and `IsAotCompatible` are set, so the analyzers check every build (D-217,
  D-254).
- First release (D-115): `AutoDetectLayout.Open(Stream / byte[], format, options, ct)`, the former behaviour of
  `DocumentLayout.Open(Stream, Auto)` for every format: docx, xlsx, encrypted files of either kind (with
  `LayoutOptions.Password`) and text.
