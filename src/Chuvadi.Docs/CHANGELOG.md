# Chuvadi.Docs — Changelog

## Unreleased

### Hardening for untrusted input

- **Resource limits** on `DocxReaderOptions`, applied by `DocxReader.Open`, `Document.Load` and `DocxTemplate.Fill`: `MaxEntries` (10,000), `MaxTotalUncompressedBytes` (1 GiB), `MaxCompressionRatio` (1,000:1 above `CompressionRatioThresholdBytes`, 1 MiB), `MaxXmlDepth` (256) and `MaxSpinCount` (500,000). Package limits are checked from the zip central directory before any part is inflated; the declared sizes are a hard ceiling because the runtime never inflates an entry past them. The spin count is read from EncryptionInfo before key derivation starts. Every limit is nullable; `DocxReaderOptions.CreateUnrestricted()` disables them all.
- **`DocxSecurityLimitException`** (derives from `DocxFormatException`) reports `Limit`, `Actual`, `Allowed` and `PartName`. `DocxFormatException` is no longer sealed.
- **Active-content detection** (detect only, never execute or fetch): `DocxReader.HasMacros`, `ContainsVbaProject`, `IsMacroEnabledPackage`, `MainPartContentType`, and `GetActiveContent()` returning `DocxActiveContent` — ActiveX controls, embedded OLE objects, altChunks, DDE/DDEAUTO fields (including instructions split across runs), remote attached templates, other fetch-on-render external resources, and every external reference verbatim.
- **Cancellation**: `CancellationToken` overloads on `DocxReader.Open` (path and stream), `Paragraphs`, `Tables`, `ExtractText`, `Images`, `SaveImages`, `GetActiveContent`, and new `Document.Load(path|stream, password, options, cancellationToken)` overloads. Checked on every XML node and every decompression buffer fill; cancellation latency measured at under a millisecond.
- `MaxPackageBytes` (1 GiB) bounds the input file/stream itself before it is buffered or decrypted.
- **Behaviour change:** a `MaxPartBytes` breach now throws `DocxSecurityLimitException` (`Limit = PartBytes`) instead of `InvalidDataException`, from every reader path, and also caps embedded media read by `Images()`; `Document.Load` no longer swallows it while collecting images.
- `DocxTemplate.Fill` gains an overload taking `DocxReaderOptions` and a `CancellationToken`; the existing overloads apply the default limits. A cancelled fill writes no output.
- `DocxReader.Open(stream)` takes ownership of the stream on every path (disposed with the reader, or when opening fails), accepts non-seekable streams (buffered, bounded), and opening a file path that turns out to be encrypted no longer leaks the file handle.
- Fix (pre-existing): an empty `<wp:inline/>`/`<wp:anchor/>` made `Images()` and `Document.Load` loop forever.
- DDE detection scans every WordprocessingML part (comments, glossary…), and a linked (external) OLE object is reported as an external resource, not an embedded object.
- README: fixed an image example that called the internal `AddBlock`.

## 1.1.0 (2026-06-12)

Image support — read, write, and templates.

- **Write**: insert inline and floating images anywhere a run can go — `Document.AddImage`, `Paragraph.Image`, `TableCell.SetImage`/`AddImage`. Full DrawingML emission (`wp:inline` and `wp:anchor`) with every floating option exposed: horizontal/vertical anchors (page/margin/column/character/line/paragraph and the four margin variants), named alignment or point offsets, all five wrap modes (square, tight, through, top-and-bottom, none), behind/in-front-of text, overlap and anchor locking, z-order, and wrap padding.
- **Auto-sizing**: `ImageSpec.FromFile`/`FromBytes` read pixel dimensions and DPI from PNG, JPEG, BMP, GIF, and TIFF headers (pure BCL, no System.Drawing) to compute display size in points; `ScaleToWidth`/`ScaleToHeight` preserve aspect ratio. Explicit sizing via `Inline`/`Float` for any format including EMF/WMF.
- **Read**: `DocxReader.Images()` returns every image (body, headers, footers) with bytes, content type, display size, placement, floating position, and — for images in tables — table/row/column location. `DocxReader.SaveImages(folder)` extracts them to disk. `Document.Load(...).Images` exposes the same on the editable model. This metadata is the input the planned PDF pipeline consumes.
- **Templates**: `DocxTemplate.Fill` gains an `imageValues` overload with two mechanisms — *text-to-image* (`{{Key}}` becomes an inline/floating image in place) and *replace-by-alt-text* (an existing template image whose alt text matches a key has its bytes swapped, keeping the template's position and size). Media parts, relationships, and content-type entries are injected automatically; everything else is still copied byte-for-byte.
- **Reader fix**: `[Content_Types].xml` `<Default Extension=...>` mappings are now honored when resolving parts, not just `<Override>` entries. This lets the reader open image (and other) media parts in documents authored by Word, which declare media via Default extension rules.

Cross-validated against python-docx (inline shape dimensions read back exactly; media bytes verified by SHA-256) and msoffcrypto-tool (encrypted documents containing images decrypt and round-trip).

## 1.0.0 (2026-06-11)

Initial release.

- **Writer**: paragraphs with run formatting (bold, italic, underline, strikethrough, font, size, color, highlight); styles (Title, Heading 1–3, Quote, Normal); alignment; bulleted/numbered lists with nesting; hyperlinks; page breaks; tables (borders, header rows, shading, column widths, column spans); headers/footers with PAGE/NUMPAGES fields and different-first-page; page setup (A4/Letter/Legal, portrait/landscape, margins).
- **Templates**: `DocxTemplate.Fill` replaces `{{Placeholders}}` in body, headers, footers, footnotes, endnotes while preserving everything else in the file byte-for-byte; handles placeholders split across runs; supports encrypted input and encrypted output.
- **Reader**: `DocxReader` (paragraph stream with styles, table grids, full-text extraction) with `MaxPartBytes` decompression cap; `Document.Load` builds an editable model with basic formatting.
- **Security**: OOXML Agile Encryption read/write (AES-256-CBC, iterated SHA-512 KDF, HMAC-SHA512 verified on decrypt) via the shared Chuvadi crypto internals; restrict-editing protection (`documentProtection`, iterated SHA-512 password hash); DTD processing prohibited everywhere.
- Pure BCL; zero NuGet dependencies; single-assembly DLL.

Cross-validated against python-docx and msoffcrypto-tool.
