# Chuvadi.Docs — Changelog

## Unreleased

### Tables

- **Table formatting** (`DocTable.Format`, `TableFormat`): table style + `TableLook`, width (`TableWidth.Points` / `Percent` / `Auto`), alignment, indent, fixed/autofit layout, borders incl. inside lines (`TableBorders.Grid` / `Horizontal` / `NoBorders`), default cell margins, cell spacing, shading, right-to-left, band sizes, and the accessibility caption/description.
- **Rows**: height with rule (auto / at least / exact), can't-split, repeat-as-header (now settable), row alignment, hidden, grid-before/after for ragged tables.
- **Cells**: vertical merge (`VerticalMerge.Restart` / `Continue`), width, full shading, borders incl. diagonals, margins, vertical alignment, text direction (rotated headers), no-wrap, fit-text, hide-mark; **nested tables** (`TableCell.AddTable`, `NestedTables`, ordered `Blocks`; Word's trailing paragraph is written automatically).
- **Table styles**: `StyleDefinition.TableFormat`, `RowFormat`, `CellFormat` and conditional formats per `TableRegion` (whole table, odd/even row and column bands, first/last row and column, the four corner cells) with paragraph, run, border, margin, row and cell formatting; `Styles.AddTableStyle`. Replaces the verbatim carry-through of 1.3-dev.
- **Effective table formatting**: `Document.GetEffectiveTableFormat(table)`, `GetEffectiveCellFormat(TableCellRef)` (table borders mapped onto each cell edge, style cell format, applicable regions in Word's order, direct cell formatting) and table-aware `GetEffectiveRunFormat` / `GetEffectiveParagraphFormat(…, TableCellRef)` with the table style as the lowest style level in the toggle rule. Verified against Word's built-in "Light Grid Accent 1" style (bold heading-font header with 2.25pt rule, banded rows #D3DFEE).
- **Document.Load builds structured tables** — every table/row/cell property, merges, spans, ragged rows, nested tables (text in content controls inside cells is kept) — instead of text-only grids; load → save reproduces `document.xml` exactly, tables included. Hostile tables (huge spans, rows wider than the grid) are clamped and loaded.
- **Rows and cells**: legacy horizontal merges (`TableCell.HorizontalMerge`), the widths of skipped grid columns (`TableRow.WidthBefore` / `WidthAfter`) and per-row cell spacing (`TableRow.CellSpacingPt`) are read and written.
- Rows and cells wrapped in content controls or custom XML (`w:sdt`, `w:customXml`) are loaded with their content; the wrapper itself is not kept on save.
- Fixes: `tblLayout` is now written after `tblBorders`/`shd` as CT_TblPr requires; a spanned cell's width is the sum of the columns it covers (was only the first); empty text runs (`<w:t/>`) survive load → save.
- Robust against hostile tables: loaded tables are capped at Word's 63 grid columns (extra cells beyond the cap are dropped), a huge `gridSpan` or `gridBefore` can neither widen the grid nor push later cells out, placeholder cells are created only when accessed, ragged rows stay ragged on save (missing trailing cells are not invented), and a table nested inside itself is refused (`ArgumentException` from `TableCell.AddTable`; `InvalidOperationException` at save as a last guard) instead of overflowing the stack. `GridBefore`/`GridAfter` are clamped to the row when written.
- Nested tables are anchored to the paragraph they follow, so inserting or removing paragraphs earlier in the cell no longer moves them.
- Effective formatting follows ECMA-376 §17.7.6: conditional regions apply in the order whole table, column bands, row bands, first/last row, first/last column, then the corner cells (so the first column beats the header row and `nwCell` beats both); borders apply level by level, so a region's table border overrides an earlier level's cell border. A conditional format's table properties (`TableConditionalFormat.TableFormat`: shading, alignment, indent, spacing…) are kept, written and its shading applied; table styles write every stated table property, including `tblLook` and `tblW`. A `TableCellRef` outside its table throws `ArgumentOutOfRangeException`.
- Behaviour notes: loaded tables keep exactly the borders the file states (`Borders` is false and `Format.Borders` holds them) instead of the legacy default grid; when no `tblLook` is set anywhere, Word's default look applies (`TableLook.Default`, 0x04A0: header row, first column, row banding — what Word and LibreOffice do); floating-table positioning is carried verbatim; conditional-formatting marks on cells (`cnfStyle`, a cache Word recomputes) and row-level property exceptions (`tblPrEx`) are not kept; `DocTable.GetText()` and `TableCell.GetText()` include the text of nested tables; table-style regions are written in ST_TblStyleOverrideType order (Word's order).

### Styles

- **Style model**: `Document.Styles` (`StyleCollection`) of `StyleDefinition`s — paragraph, character, table and numbering styles with id, name, aliases, basedOn, next, link, default, custom, quick-format, UI priority, hidden / semi-hidden / unhide-when-used, locked, auto-redefine, a `ParagraphFormat` and a run `TextFormat`. `AddParagraphStyle` / `AddCharacterStyle` helpers; `AddOrReplace`, `Remove`, `TryGet`, `GetDefault`.
- **Document defaults**: `Document.Defaults` (`RunFormat`, `ParagraphFormat`) replace the hard-coded Calibri 11pt; set e.g. `FontForAllScripts = "Nirmala UI"` once for a whole Indic document.
- **Custom styles in content**: `Paragraph.StyleId` (any style id; built-in ids map to `ParagraphStyle`) and character styles on runs via `Paragraph.StyledText(text, characterStyleId)` / `Run.CharacterStyleId`.
- **Effective formatting**: `Document.GetEffectiveRunFormat(paragraph, run)` and `GetEffectiveParagraphFormat(paragraph)` resolve document defaults → paragraph style chain → character style chain → direct formatting, with toggle properties combining by exclusive-or between paragraph and character styles (ECMA-376 §17.7.3), tab stops accumulating (Clear removes) and borders merging per side. Robust to basedOn cycles and missing styles. Verified against a Word-authored styles.xml (164 styles; Heading 1 resolves to Word's 14pt bold accent-1 heading font).
- **Document.Load reads styles.xml** — every style and the document defaults — and keeps custom paragraph and character style ids on content (previously unknown style ids became Normal). Load → save reproduces both `styles.xml` and `document.xml` exactly for documents written by Chuvadi.Docs.
- Built-in styles now come from the model, with the same look; headings and Title, Quote and List Paragraph are based on Normal with Next = Normal, headings keep with the next paragraph and keep their lines together (Word's behaviour), and style bold/size now also apply to complex-script text (`bCs`/`szCs`).
- **Theme carried through**: `Document.ThemeXml` holds the theme part (word/theme/theme1.xml) of a loaded document and writes it back, so theme fonts and colours survive load → save (verified: LibreOffice renders a Word-authored file in the same fonts before and after).
- **Never a dangling style reference**: built-in styles that content refers to but a loaded styles.xml lacks (Word lists only styles in use) — e.g. after adding a heading, hyperlink or list item to a loaded file — are written to the output; the document's own collection is not changed.
- Styles keep their numbering reference (`numPr`) verbatim through load → save until lists are modelled. (Table-style properties are modelled — see Tables.)
- Duplicate style ids in a loaded styles.xml: the first definition wins (linear time, robust to hostile files).
- Colour `auto` is kept as an explicit value (`ColorHex = "auto"`, also for underline colour) so it overrides an inherited colour; previously it read as "unset".
- Effective paragraph formatting: a level that sets a first-line indent replaces an inherited hanging indent (and vice versa), and explicit spacing replaces inherited auto-spacing. A paragraph referring to a non-paragraph style falls back to the default paragraph style.
- Design choice: toggles combined from the style levels replace a toggle set in the document defaults (the spec leaves this open; Word documents do not set toggles in docDefaults in practice).
- Not yet loaded: latent style information (Word regenerates it).

## 1.2.0 (2026-10-28)

Hardening for untrusted input, the complete character (run) and paragraph models, Indic/complex-script fixes, and cancellable, faster encryption.

### Fixes

- `DocxReader` text extraction no longer turns tab-stop definitions (`w:pPr/w:tabs`) into tab characters — Word documents with custom tab stops gained phantom leading tabs per stop — and ignores the style recorded in `w:pPrChange` history.
- `Document.SaveTo(Stream)` now leaves the caller's stream open (flushed, positioned at the end). Previously it disposed it, so save-to-MemoryStream-then-read failed.

### Paragraph model

- **`ParagraphFormat`** (`Paragraph.Format`, plus `ApplyFormat` / `Configure` / `Clone`) with the full paragraph-property set: alignment (now also distribute, kashida and Thai distributed), keep with next, keep lines together, page break before (`StartOnNewPage`), widow control, line-number and hyphenation suppression; space before/after (points or auto), line spacing (`LineSpacing.Multiple` / `Exactly` / `AtLeast`), contextual spacing; left/right/first-line/hanging indents and mirror indents; paragraph borders (top, left, bottom, right, between, bar) and shading; custom tab stops with alignment and leaders; outline level, bidi, font alignment, grid snapping; East Asian line-breaking toggles; and the paragraph mark's own run formatting (`MarkFormat`), which sets the height of empty paragraphs.
- Shared value types **`Shading`** (all 38 ST_Shd patterns, pattern color, fill, theme fill), **`BorderSpec`** (27 line styles, width, color, theme color, spacing, shadow, frame; Word's decorative art borders round-trip via `RawStyleToken`), **`TabStop`** and **`LineSpacing`**, reused by tables in a later phase.
- **`TextFormat.Shading`** carries full run shading; a plain fill still loads as `ShadingHex`. This closes the run-shading gap noted under the text model.
- **Heading 1–9**: `AddHeading` accepts levels 1–9 (previously 1–3) and `styles.xml` defines Heading 4–9 with outline levels.
- `Paragraph.Alignment` is now a shortcut for `Format.Alignment` (Left means inherit); existing code and output are unchanged.
- Robust against odd or hostile input: non-finite, underflowing or absurd measurements are ignored on load (never thrown), border width/spacing are clamped to the schema ranges, out-of-range outline levels and non-token `textAlignment` values are ignored. Values set through the API that cannot be written (undefined enum values, NaN measurements, a non-token `RawStyleToken`) fail clearly at save with `ArgumentException`/`ArgumentOutOfRangeException`.
- **Behaviour changes for loaded documents:** `jc="distribute"` now loads as `ParagraphAlignment.Distribute` (was `Justify`); a run's tabs and plain line breaks load inside its text (`\t`, `\n`) rather than as separate `IsTab` / `Break == Line` runs. An encrypted file opened with NO password now reports `DocxPasswordRequiredException` even when its spin count exceeds `MaxSpinCount` (the password check runs first).
- **Document.Load** reads every paragraph property, including Word's `w:start`/`w:end` indents and borders; tracked previous properties never override. A run (`w:r`) now loads as ONE `Run` whose text carries `\t` / `\n` for its tabs and line breaks, so a load → save round-trip reproduces `document.xml` exactly, paragraph properties included. Page, column and text-wrapping breaks remain explicit `Break` runs.

### Text model (run formatting, scripts, breaks)

- **Complete run formatting on `TextFormat`**: four font slots (`AsciiFont`, `HighAnsiFont`, `EastAsiaFont`, `ComplexScriptFont`) plus `FontForAllScripts`, theme fonts per slot and `FontHint`; complex-script `BoldComplexScript`, `ItalicComplexScript`, `SizeComplexScriptPt`, `ComplexScript`, `RightToLeft`; `Language` / `EastAsiaLanguage` / `ComplexScriptLanguage`; `UnderlineStyle` (all 18 styles) and `UnderlineColorHex`; `DoubleStrikethrough`, `Caps`, `SmallCaps`, `Hidden`, `Outline`, `Shadow`, `Emboss`, `Imprint`, `NoProof`; `VerticalPosition` (superscript/subscript), `BaselineShiftPt`, `CharacterSpacingPt`, `HorizontalScalePercent`, `KerningMinSizePt`; `ThemeColor` with tint/shade; `ShadingHex`. New toggles are tri-state (`bool?`) so they can switch an inherited effect off.
- **Indic / complex-script fix**: `Font` only ever filled the Latin slots, so Tamil, Devanagari or Malayalam text ignored it, and bold/size didn't apply to those scripts. `FontForAllScripts` and `ComplexScriptFont` set the slot those scripts use, and with `MirrorToComplexScript` (default on) `Bold`, `Italic` and `SizePt` also write `bCs`, `iCs` and `szCs`, as Word's toolbar does. Document defaults now include `szCs`, so complex-script text defaults to 11pt like Latin text instead of Word's 10pt fallback.
- **Breaks and tabs**: `Paragraph.Break(BreakType)` — line, page, column, and text-wrapping breaks that clear floating objects left/right/both — plus `LineBreak()` and `Tab()`. `\n`, `\r\n` and `\r` in text are written as `w:br`, and `\t` as `w:tab` (previously they landed inside `w:t` and rendered as spaces); the stored run text is unchanged.
- **Templates**: line feeds and tabs inside a FILLED value become `w:br` / `w:tab` in the same run, keeping its formatting. Untouched template text is never altered.
- **Formatted hyperlinks**: `Paragraph.Hyperlink(text, url, TextFormat)` layers formatting over the Hyperlink character style.
- **Document.Load reads every run property** through the same mapping the writer uses, and loads breaks, tabs, non-breaking and soft hyphens as runs; text inside text boxes is kept (flattened). Loaded formats carry complex-script properties exactly as stated (`MirrorToComplexScript = false`), so load → save reproduces `document.xml` exactly. Tracked previous paragraph properties (`w:pPrChange`) no longer override current ones on load, and `numId 0` is no longer treated as a list.
- `TextFormat.ExplicitlyOff` (`RunToggles`) carries bold/italic/strikethrough written as explicitly off (`w:val="0"`), so a run that cancels a style's bold round-trips; `highlight="none"` is kept likewise. Known gap until the shading model lands: run shading keeps only its fill color.
- Text boxes stored as `mc:AlternateContent` are read once (the `mc:Fallback` copy is skipped) and moved-away / deleted content (`w:moveFrom`, `w:del`) is no longer shown — by both `Document.Load` and `DocxReader`. Previously text-box text appeared twice.
- **Schema order fix**: `w:highlight` is now written before `w:u`, as CT_RPr requires (previously reversed).
- `DocxReader` text extraction maps non-breaking and soft hyphens to U+2011 and U+00AD.

### Shared crypto (shared/Chuvadi.Internal — additive)

- Key derivation observes a `CancellationToken` (checked every 4,096 rounds, ~10 ms) and the decryptor takes an optional spin-count cap, checked after EncryptionInfo is parsed and before any derivation. Chuvadi.Docs now passes `MaxSpinCount` and the caller's token straight through: cancelling `DocxReader.Open`, `Document.Load` or `DocxTemplate.Fill` on an encrypted file stops key derivation too. Existing internal signatures are unchanged (Sheets is unaffected until it opts in).
- The iterated hash is computed once per open/save instead of three times (the verifier keys and key-encryption key share it): opening an encrypted file is ~3.4× faster (1,000,000 spins: 2.4 s → 0.7 s), with byte-identical results. Cross-checked with msoffcrypto-tool (HMAC verified).

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
