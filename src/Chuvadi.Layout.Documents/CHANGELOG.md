# Chuvadi.Layout.Documents — Changelog

## 0.1.0 (unreleased)

- First release (D-115): `AutoDetectLayout.Open(Stream / byte[], format, options, ct)`, the former behaviour of
  `DocumentLayout.Open(Stream, Auto)` for every format: docx, xlsx, encrypted files of either kind (with
  `LayoutOptions.Password`) and text.
