# Chuvadi.Docs — Changelog

## 1.0.0 (2026-06-11)

Initial release.

- **Writer**: paragraphs with run formatting (bold, italic, underline, strikethrough, font, size, color, highlight); styles (Title, Heading 1–3, Quote, Normal); alignment; bulleted/numbered lists with nesting; hyperlinks; page breaks; tables (borders, header rows, shading, column widths, column spans); headers/footers with PAGE/NUMPAGES fields and different-first-page; page setup (A4/Letter/Legal, portrait/landscape, margins).
- **Templates**: `DocxTemplate.Fill` replaces `{{Placeholders}}` in body, headers, footers, footnotes, endnotes while preserving everything else in the file byte-for-byte; handles placeholders split across runs; supports encrypted input and encrypted output.
- **Reader**: `DocxReader` (paragraph stream with styles, table grids, full-text extraction) with `MaxPartBytes` decompression cap; `Document.Load` builds an editable model with basic formatting.
- **Security**: OOXML Agile Encryption read/write (AES-256-CBC, iterated SHA-512 KDF, HMAC-SHA512 verified on decrypt) via the shared Chuvadi crypto internals; restrict-editing protection (`documentProtection`, iterated SHA-512 password hash); DTD processing prohibited everywhere.
- Pure BCL; zero NuGet dependencies; single-assembly DLL.

Cross-validated against python-docx and msoffcrypto-tool.
