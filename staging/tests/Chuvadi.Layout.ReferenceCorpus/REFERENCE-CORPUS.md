# Chuvadi.Layout reference corpus (D-40)

We make our own reference documents and check Layout against them. This folder holds 48 Word documents and 25 Excel
workbooks, each exercising one feature family, generated with Chuvadi.Docs and Chuvadi.Sheets, together with a reference
PDF of each made by **LibreOffice 24.2.7.2 420(Build:2)** in headless mode. The acceptance harness
(staging/samples/Chuvadi.Layout.Acceptance) compares Layout's pages with the references, and
staging/tests/Chuvadi.Layout.Acceptance.Tests checks every run against the baseline in expected.tsv.

LibreOffice is a second implementation of Word's and Excel's layout, not Word or Excel. Where the two disagree this
report says so, and the files at the end are the ones to open in real Word and Excel on Windows.

## Contents

| Path | What it is |
|---|---|
| corpus/*.docx, corpus/*.xlsx | The sources, written by staging/samples/Chuvadi.Layout.ReferenceCorpus. Deterministic apart from the creation date in docProps/core.xml. |
| corpus/*.pdf | The LibreOffice references (PDF export of the default print settings). |
| corpus/cases.tsv | File, feature family and what each file exercises. |
| corpus/manifest.tsv | The harness manifest: source, reference, printer. |
| expected.tsv | The baseline: LibreOffice and Layout page counts, result, lines matched, worst pixel score, category and note per file. |
| make-references.sh, fonts.conf.in | Regenerate the sources and references (Linux or WSL with LibreOffice; LibreOffice itself is not committed). |

## How the references are made

- LibreOffice runs with its own font configuration (fonts.conf.in) that contains only Layout's fonts
  (src/Chuvadi.Layout.Fonts/Fonts and the built-in LiPi fonts in src/Chuvadi.Fonts/Fonts) and the metric-compatible aliases. So both sides draw with the same font files:
  Calibri as Carlito, Cambria as Caladea, Arial as Liberation Sans, Times New Roman as Liberation Serif, Courier New as
  Liberation Mono, and Tamil with LiPi Sans Tamil.
- Command (what make-references.sh runs):
  `FONTCONFIG_FILE=fonts.conf SAL_USE_VCLPLUGIN=svp soffice -env:UserInstallation=file:///tmp/profile --headless --norestore --convert-to pdf --outdir ref src/*.docx src/*.xlsx`

## What is compared, and the thresholds

Per file, per page:

1. **Page count** must equal the reference's.
2. **Text in reading order** (tier A) must be identical page by page, ignoring whitespace, ligatures, soft hyphens and
   the length of tab leaders.
3. **Line breaks and positions** (geometry): every reference line must be found with the same text, its baseline and
   left edge within 8.42 pt (1% of the A4 page height, because the fonts are substitutes; 2 pt with the original fonts).
   "Lines matched" is the share of reference lines Layout has with the same text.
4. **Pixel difference** at 96 dpi: both PDFs rasterized by Chuvadi.Pdf in grey; a pixel differs when it is more than 64
   grey levels from the other image at the same place and all eight neighbours; the score is the differing share of
   the content pixels. Pages above **10%** are named in the report. The score is informational, not a pass/fail gate:
   a whole-line shift of one line moves the score by about as much as the text on the page.

Regression gate (ReferenceCorpusTests): page counts equal the baseline, files that passed still pass, and no file
matches fewer reference lines than in the baseline.

## Results

Word documents: **36 of 48** pass text and geometry; page counts agree on **48 of 48**.
Excel workbooks: **0 of 25** pass; page counts agree on **22 of 25**, and all 25 failures are Calc-vs-Excel
pagination differences (below), not Layout faults found so far.

### Word documents

| File | Exercises | LibreOffice pages | Layout pages | Result | Lines matched | Pixels (worst page) | Category |
|---|---|---|---|---|---|---|---|
| d01-fonts-sizes.docx | fonts and sizes | 2 | 2 | fail | 91.9% | 62.58% | font-substitution |
| d02-font-styles.docx | fonts and sizes | 1 | 1 | pass | 100% | 7.48% | pass |
| d03-tamil-latin-mix.docx | Tamil and Latin | 1 | 1 | fail | 9.1% | 5.08% | reference-extraction |
| d04-tamil-long.docx | Tamil and Latin | 4 | 4 | fail | 0% | 7.46% | reference-extraction |
| d05-paragraph-spacing.docx | paragraph spacing | 2 | 2 | pass | 100% | 6.94% | pass |
| d06-line-spacing.docx | paragraph spacing | 1 | 1 | pass | 100% | 9% | pass |
| d07-alignment.docx | paragraph spacing | 1 | 1 | pass | 100% | 8.01% | pass |
| d08-indents.docx | paragraph spacing | 1 | 1 | pass | 100% | 7% | pass |
| d09-keep-with-next.docx | keep-with-next and widows | 4 | 4 | pass | 100% | 9.36% | pass |
| d10-widow-orphan.docx | keep-with-next and widows | 2 | 2 | pass | 100% | 8.39% | pass |
| d11-page-breaks.docx | keep-with-next and widows | 4 | 4 | pass | 100% | 7.76% | pass |
| d12-bullets.docx | lists | 1 | 1 | pass | 100% | 5% | pass |
| d13-numbered.docx | lists | 1 | 1 | pass | 100% | 4.71% | pass |
| d14-long-text.docx | paragraph spacing | 6 | 6 | pass | 100% | 10.02% | pass |
| d15-table-simple.docx | tables | 1 | 1 | pass | 100% | 4.99% | pass |
| d16-table-merges.docx | tables | 1 | 1 | pass | 100% | 5.61% | pass |
| d17-table-repeat-header.docx | tables | 3 | 3 | pass | 100% | 27.39% | pass |
| d18-table-widths.docx | tables | 2 | 2 | pass | 100% | 19.57% | pass |
| d19-table-banded-style.docx | tables | 1 | 1 | pass | 100% | 1.99% | pass |
| d20-table-nested.docx | tables | 1 | 1 | fail | 100% | 30.54% | libreoffice-differs |
| d21-sections-orientation.docx | sections and columns | 3 | 3 | pass | 100% | 10.28% | pass |
| d22-sections-columns.docx | sections and columns | 1 | 1 | pass | 100% | 4.83% | pass |
| d23-sections-page-numbering.docx | sections and columns | 3 | 3 | pass | 100% | 8.8% | pass |
| d24-header-footer.docx | headers and footers | 2 | 2 | pass | 100% | 8.8% | pass |
| d25-header-first-page.docx | headers and footers | 2 | 2 | pass | 100% | 9% | pass |
| d26-header-odd-even.docx | headers and footers | 2 | 2 | pass | 100% | 8.41% | pass |
| d27-header-table.docx | headers and footers | 1 | 1 | pass | 100% | 9.03% | pass |
| d28-image-inline.docx | images | 1 | 1 | pass | 100% | 8.85% | pass |
| d29-image-sizes.docx | images | 1 | 1 | fail | 100% | 14.55% | libreoffice-differs |
| d30-image-float-square.docx | images | 1 | 1 | pass | 100% | 6.44% | pass |
| d31-image-float-behind.docx | images | 1 | 1 | pass | 100% | 4.79% | pass |
| d32-page-a4-narrow.docx | page sizes and margins | 2 | 2 | pass | 100% | 10.97% | pass |
| d33-page-letter-wide.docx | page sizes and margins | 2 | 2 | pass | 100% | 7.55% | pass |
| d34-page-a5-legal.docx | page sizes and margins | 3 | 3 | pass | 100% | 9.66% | pass |
| d35-tabs.docx | tabs and ptab | 1 | 1 | pass | 100% | 4.41% | pass |
| d36-ptab.docx | tabs and ptab | 1 | 1 | fail | 96.2% | 10.37% | libreoffice-differs |
| d37-borders-shading.docx | paragraph spacing | 1 | 1 | pass | 100% | 2.46% | pass |
| d38-hyperlinks.docx | fonts and sizes | 1 | 1 | pass | 100% | 2.14% | pass |
| d39-watermark.docx | headers and footers | 1 | 1 | fail | 87.1% | 38.11% | open |
| d40-report.docx | mixed | 1 | 1 | pass | 100% | 5.62% | pass |
| d41-paragraph-mark-size.docx | fonts and sizes | 1 | 1 | pass | 100% | 50.8% | pass |
| d42-footnotes.docx | notes | 2 | 2 | fail | 100% | 55.81% | libreoffice-differs |
| d43-tracked-comments.docx | notes | 1 | 1 | fail | 64.7% | 20.99% | libreoffice-differs |
| d44-fields.docx | notes | 1 | 1 | pass | 100% | 5.99% | pass |
| d45-text-boxes.docx | images | 1 | 1 | fail | 63.2% | 10.74% | libreoffice-differs |
| d46-floating-table.docx | tables | 1 | 1 | fail | 77.8% | 13.18% | libreoffice-differs |
| d47-symbols.docx | fonts and sizes | 1 | 1 | pass | 100% | 6.82% | pass |
| d48-embedded-font.docx | fonts and sizes | 1 | 1 | pass | 100% | 5.12% | pass |

### Excel workbooks

| File | Exercises | LibreOffice pages | Layout pages | Result | Lines matched | Pixels (worst page) | Category |
|---|---|---|---|---|---|---|---|
| x01-number-formats.xlsx | number formats | 2 | 2 | fail | 2.4% | 54.42% | calc-vs-excel |
| x02-accounting.xlsx | number formats | 1 | 1 | fail | 80% | 53.32% | calc-vs-excel |
| x03-fonts-alignment.xlsx | fonts and alignment | 2 | 1 | fail | 50% | 58.6% | calc-vs-excel |
| x04-merges.xlsx | merges | 2 | 2 | fail | 76.5% | 61.05% | calc-vs-excel |
| x05-borders.xlsx | borders | 1 | 1 | fail | 100% | 65.3% | calc-vs-excel |
| x06-fills.xlsx | fills | 1 | 1 | fail | 100% | 24.71% | calc-vs-excel |
| x07-conditional.xlsx | conditional formats | 1 | 1 | fail | 100% | 31.5% | calc-vs-excel |
| x08-print-area.xlsx | print areas and titles | 1 | 1 | fail | 90% | 26.94% | calc-vs-excel |
| x09-print-titles.xlsx | print areas and titles | 4 | 4 | fail | 72.6% | 30.95% | calc-vs-excel |
| x10-page-breaks.xlsx | page breaks | 6 | 6 | fail | 93.4% | 31.67% | calc-vs-excel |
| x11-fit-width.xlsx | fit to page | 2 | 2 | fail | 71.9% | 16.37% | calc-vs-excel |
| x12-fit-one-page.xlsx | fit to page | 1 | 1 | fail | 80.2% | 11.56% | calc-vs-excel |
| x13-wide-sheet.xlsx | wide sheets | 10 | 10 | fail | 97.1% | 58.95% | calc-vs-excel |
| x14-wide-over-then-down.xlsx | wide sheets | 10 | 10 | fail | 97.1% | 58.95% | calc-vs-excel |
| x15-long-sheet.xlsx | page breaks | 7 | 7 | fail | 69.1% | 41.8% | calc-vs-excel |
| x16-landscape.xlsx | page setup | 2 | 2 | fail | 86.9% | 31.82% | calc-vs-excel |
| x17-letter.xlsx | page setup | 4 | 2 | fail | 0% | 32.64% | calc-vs-excel |
| x18-header-footer.xlsx | page setup | 3 | 3 | fail | 76.6% | 43.58% | calc-vs-excel |
| x19-gridlines-headings.xlsx | page setup | 1 | 1 | fail | 100% | 70.12% | calc-vs-excel |
| x20-widths-heights.xlsx | fonts and alignment | 2 | 2 | fail | 100% | 60% | calc-vs-excel |
| x21-hidden.xlsx | fonts and alignment | 1 | 1 | fail | 86.7% | 28.06% | calc-vs-excel |
| x22-overflow.xlsx | fonts and alignment | 2 | 1 | fail | 56.3% | 49% | calc-vs-excel |
| x23-wrap-tall.xlsx | fonts and alignment | 2 | 2 | fail | 17.7% | 59.05% | calc-vs-excel |
| x24-multi-sheet.xlsx | page setup | 5 | 5 | fail | 85.7% | 56.79% | calc-vs-excel |
| x25-scale-center.xlsx | fit to page | 1 | 1 | fail | 87.1% | 19.31% | calc-vs-excel |

### Notes per document

- **d01-fonts-sizes.docx** (font-substitution): Cambria is drawn with Caladea; LibreOffice spaces Caladea lines by its typo metrics (1.15 em), Layout by the Windows metrics Word uses (1.30 em). Calibri, Arial, Times and Courier lines match.
- **d03-tamil-latin-mix.docx** (reference-extraction): Glyphs, line breaks and positions match LibreOffice (Chuvadi.Pdf 3.26 shaping; pixel score under 10%). Text differs only because Chuvadi.Pdf extracts LibreOffice's Tamil text wrongly (vowel signs repeated or moved); poppler extracts the same reference exactly as Layout writes it. Reported to the Pdf chat.
- **d04-tamil-long.docx** (reference-extraction): As d03. Spaces between Tamil words now take the Tamil font and size (fixed in Layout by this corpus). To confirm in Word: line 2 ends with காலை.
- **d05-paragraph-spacing.docx** (pass): Space between paragraphs is the larger of space after and space before (fixed in Layout by this corpus). To confirm in Word.
- **d11-page-breaks.docx** (pass): A paragraph holding only a page break keeps its mark on the old page (fixed in Layout by this corpus). To confirm in Word.
- **d15-table-simple.docx** (pass): Rows make room for their horizontal borders (fixed in Layout by this corpus). To confirm in Word.
- **d20-table-nested.docx** (libreoffice-differs): After a nested table the cell holds its required empty paragraph; Layout gives it a line, as Word shows it, LibreOffice hides it, so the next outer row starts one line lower in Layout. To confirm in Word.
- **d22-sections-columns.docx** (pass): Newspaper columns, laid out as the file states them (ECMA-376 17.6.4). Where the standard is silent (balancing, the separator line, the mark after a column break, footnotes) Layout follows Word, measured with calib-columns.docx; d22 matches Word 2021 within 0.1 pt.
- **d29-image-sizes.docx** (libreoffice-differs): Layout makes a line holding an inline picture as tall as the picture plus the font descent (the picture sits on the baseline, as in Word); LibreOffice uses the picture height alone. About 3 pt per picture. To confirm in Word.
- **d35-tabs.docx** (pass): Each tab draws the leader of its own stop (fixed in Layout by this corpus).
- **d36-ptab.docx** (libreoffice-differs): LibreOffice does not draw the dot leader of a positional tab; Layout does, as Word does.
- **d39-watermark.docx** (open): One line breaks one word later in Layout than in LibreOffice (a word that fits within about 1 pt of the right margin). To confirm in Word.
- **d41-paragraph-mark-size.docx** (pass): Lines pass within tolerance, but the pixel score is high: Layout sizes the last line by the 11 pt paragraph mark as well, as Word does; LibreOffice uses the 8 pt text alone. To confirm in Word.
- **d42-footnotes.docx** (libreoffice-differs): Footnotes match LibreOffice on page 1 (positions within 0.6 pt, two-inch separator). Endnotes differ: LibreOffice puts them on a page of their own with its endnote page style; Layout, like Word, continues them after the text. To confirm in Word.
- **d43-tracked-comments.docx** (libreoffice-differs): LibreOffice exports the markup (deleted text shown); Layout lays out the final view (changes accepted, comment balloons not drawn), as the Docs integration guide says. To confirm what Word prints by default (Print Markup).
- **d44-fields.docx** (pass): Complex fields (REF, PAGEREF, SEQ) show their cached results; bookmark and field markers take no room.
- **d45-text-boxes.docx** (libreoffice-differs): Fill, outline, insets and text match. LibreOffice places the right-aligned box 8.45 pt further left (it keeps the 9 pt wrap distance inside the margin); Layout aligns the box edge to the margin, so lines beside it hold about one word more. To confirm in Word.
- **d46-floating-table.docx** (libreoffice-differs): The floating table is placed as the file states it (200 pt from the margin, at the top of the next paragraph) with text on both sides, as in Word 2021 (calibration T1, the same settings, matches Word line for line). One word on the last row beside the table falls on the other side of the table in LibreOffice.
- **d47-symbols.docx** (pass): w:sym characters drawn in their font.
- **d48-embedded-font.docx** (pass): DejaVu Serif comes only from the document (LibreOffice and Layout both use the embedded font; it is in neither font set).

## Layout bugs found and fixed by this corpus

Docs 1.6.0 content (d42 to d48), new in this round: footnotes at the foot of the page that holds their reference
(the lines with references fit only if their notes fit too), endnotes after the text, tracked changes laid out as
accepted, comment anchors and field/bookmark markers taking no room, symbols, floating and inline text boxes (fill,
outline, insets, vertical alignment, auto-fit), floating tables at their horizontal position, and embedded fonts.

1. **Space between paragraphs** (d05): Layout added the first paragraph's space after and the next one's space before.
   The space between them is the larger of the two. FlowEngine.SpaceBefore now adds only the excess over the previous
   paragraph's space after. POI docx corpus: drawing.docx now matches Word's page count (22 to 20) and bug59058.docx
   moves closer (56 to 54, Word 52); exact page counts 85 to 86 of 95, no regressions.
2. **Line height of the font** (d01, d06): lines used winAscent + winDescent only. Word also adds the hhea line gap
   beyond the Windows metrics as external leading (Times New Roman 12 pt is a 13.8 pt line, Arial 11 pt 12.65 pt).
   FontFace.WordLineMetrics; Calibri, Arial, Times and Courier lines now match.
3. **A paragraph holding only a page break** (d11): Chuvadi.Docs reads it as Paragraph.PageBreakBefore, and Layout
   mapped that to "page break before", leaving an empty line at the top of the next page. It is now a break
   character, and a page break followed only by the paragraph mark keeps the mark on the old page.
4. **Table rows and horizontal borders** (d15 to d19): a row now makes room for the border above it, and the last row
   (or a vertical merge reaching it) for the table's bottom border, so rows are 0.75 pt taller with the usual single
   borders. Before, the drift grew by a border width per row.
5. **Tab leaders** (d35): the leader was taken from the next stop with a leader, so a tab to a centre stop without a
   leader drew the following stop's dots. Each tab now draws the leader of the stop it goes to.
6. **Spaces between complex-script words** (d04): a space between two Tamil words was set in the Latin font and size
   (here 10 pt Carlito instead of 12 pt LiPi Sans Tamil), so Tamil lines were about 0.6 pt per space too narrow. Such a
   space now takes the complex-script slot and is shaped in the Tamil run.
7. **Indic shaping on Chuvadi.Pdf 3.26** (d03, d04): `PdfShaper` now shapes the whole run instead of one syllable at a
   time and takes Chuvadi.Pdf's HarfBuzz clusters, so cross-syllable kerning and contextual forms apply. Tamil word
   widths now equal LibreOffice's to 0.01 pt (சீராக 33.40, நோயாளியின் 90.16, உள்ளது 50.67 pt), and the d04 pixel score
   fell from 28.0% (Pdf 3.25) to 7.5%; d03 from 17.6% to 5.1%.

## Found elsewhere (reported, not fixed here)

- **Chuvadi.Pdf (fixed in 3.26):** Tamil shaping placed the pre-base vowel sign ை before க் instead of before the final க
  in conjuncts such as அறிக்கை (d03, d04). Checked visually on 3.26: correct, and identical to LibreOffice.
- **Chuvadi.Pdf (open, for the Pdf chat):** `TextExtractor` extracts LibreOffice's Tamil PDFs wrongly: vowel signs are
  repeated or moved (LibreOffice writes "மிகப் பழமையான", Chuvadi.Pdf reads "மிகிப் பழமைமியான"; poppler's pdftotext
  reads it correctly). This is why d03 and d04 still fail on text although glyphs and positions match.
- **Chuvadi.Sheets:** XlsxReader turns serials below 1 into DateTime 1899-12-30 + time, and NumberFormatter then
  formats that DateTime as a negative serial, so 0 and 0.375 under dd/mm/yyyy, d-mmm-yy, hh:mm:ss and h:mm AM/PM show
  ######## instead of 00/01/1900, 0-Jan-00, 00:00:00 / 09:00:00 and 12:00 AM / 9:00 AM (x01). Formatting the double
  serial directly gives the right text, so the round trip through DateTime loses it.
- **Chuvadi.Docs:** settings.xml carries no w:compat/compatibilityMode (still so in 1.6.0), so Word opens these files
  in compatibility mode. Worth writing compatibilityMode 15.

## Known LibreOffice-vs-Word and Calc-vs-Excel differences

| Area | LibreOffice / Calc | Word / Excel (and Layout) | Files |
|---|---|---|---|
| Cambria substitute | Caladea lines by typo metrics, 1.15 em | Windows metrics, 1.30 em (Layout); real Cambria in Word to be confirmed | d01 |
| Paragraph mark size | Last line sized by the text only | Last line also sized by the paragraph mark's font | d41 |
| Inline picture line | Picture height | Picture height plus the font descent below the baseline | d29 |
| Empty paragraph after a nested table | Hidden | Takes a line | d20 |
| Positional tab leader | Not drawn | Drawn | d36 |
| Tamil text extraction | Correct with poppler; Chuvadi.Pdf misreads it (see above) | Layout writes ToUnicode and ActualText; extracts as written | d03, d04 |
| Default sheet margins (no pageMargins) | 0.75 in left/right, about 1 in top/bottom | 0.7 in left/right, 0.75 in top/bottom | all x |
| Default row height (Calibri 11) | about 13.8 pt (measured in the references) | 15 pt | all x |
| Endnotes | On a page of their own (LibreOffice endnote page style) | After the text, under a separator | d42 |
| Tracked changes | Exported with the markup (deleted text shown) | Final view (Layout); Word prints the markup when Print Markup is on | d43 |
| Right-aligned floating box | Wrap distance kept inside the margin | Box edge on the margin (Layout) | d45 |
| Column widths | Wider conversion: the last column moves to the next page | width × 7 px + 5 px padding (ECMA formula) | x01, x03, x17 |
| Negative serial under a date format | Shown as a date before 1900 | ######## | x01 |
| Overflowing text past the used range | Printed on further pages | Clipped at the last used column (to confirm) | x22 |

Multi-column sections (d22) are laid out since the columns PR and match both LibreOffice and Word.

## Open these in real Word / Excel on Windows

Please open each in Word or Excel (File > Print preview, A4 or Letter as stated) and compare with Layout's output
(Viewer or the harness images). One line each on what to look at:

| File | Check |
|---|---|
| d01-fonts-sizes.docx | Cambria lines: is the line pitch 1.30 em (Layout) or 1.15 em (LibreOffice)? Page 2 starts at which line? |
| d05-paragraph-spacing.docx | Gap between paragraphs = larger of space after / space before (Layout now), not the sum. Page 2 starts with which paragraph? |
| d11-page-breaks.docx | Pages 2, 3 and 4 start at the top with text, no empty line. |
| d15-table-simple.docx | Row pitch with 0.75 pt borders: 13.43 + 0.75 pt per row. |
| d20-table-nested.docx | Is there an empty line below the nested table inside the outer cell? |
| d22-sections-columns.docx | Two-column section with a separator: matches Word 2021 (checked 3 Nov 2026). |
| d29-image-sizes.docx | Gap below each inline picture: about 3 pt (Layout) or none (LibreOffice). |
| d04-tamil-long.docx | Tamil at 12 pt: does line 2 end with காலை (Layout and LibreOffice) and are the spaces between Tamil words the Tamil font's? |
| d36-ptab.docx | Dot leader to the right-aligned "right" on the first body line. |
| d39-watermark.docx | Line 6 of the first paragraph: does "nursing" end line 6 (Layout) or start line 7 (LibreOffice)? |
| d41-paragraph-mark-size.docx | First line top: does the 8 pt line sit lower because of the 11 pt paragraph mark? |
| d42-footnotes.docx | Footnotes 1 to 5 at the foot of page 1 under a short rule; endnotes i and ii after the text (Layout carries them, with their separator, to page 2 because page 1 is full), not on a page with its own margins. |
| d43-tracked-comments.docx | Print preview with and without "Print Markup": Layout shows the accepted text ("50 Gy in 25 fractions", the deleted sentence gone). |
| d45-text-boxes.docx | Is the blue box's right edge on the right margin (Layout) or 9 pt inside it (LibreOffice)? |
| d48-embedded-font.docx | On a PC without DejaVu Serif, Word shows the first paragraph in DejaVu Serif (embedded) like Layout. |
| x01-number-formats.xlsx | Margins and row height; columns A to E on page 1; negative and zero dates. |
| x03-fonts-alignment.xlsx | One page (Layout) or two (Calc)? |
| x17-letter.xlsx | Letter, 1 in margins: all six columns on one page width (Layout, 2 pages) or not (Calc, 4 pages)? |
| x22-overflow.xlsx | Long text in column A: clipped at the last used column or printed in full? |
| x05-borders.xlsx | Row height 15 pt with borders. |

## Regenerating

```bash
# Linux or WSL, with LibreOffice and the .NET 10 SDK
staging/tests/Chuvadi.Layout.ReferenceCorpus/make-references.sh
```

Then rerun the harness and, if the change is intended, update expected.tsv from the report's TSV (file, ref_pages,
layout_pages, lines_matched, pixels_worst) keeping the category and note columns.
