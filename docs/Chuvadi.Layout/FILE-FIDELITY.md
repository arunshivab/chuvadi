# Chuvadi.Layout: how a document is laid out, point by point

This page says, for each layout point Chuvadi.Layout handles, what the file format's standard says, what Word 2021 or
Excel 2021 does, and what Layout does. Where Layout differs from Word, Excel or WPS Office on purpose, it says so here;
those points are also listed, for the whole Chuvadi family, in [docs/DIFFERENCES.md](../DIFFERENCES.md).

## The rule

1. **The file and its standard decide.** For .docx and .xlsx that is ECMA-376 Part 1, 5th edition (2016). Where the
   standard is clear and Word does something else, Layout follows the standard.
2. **Where the standard is silent, Layout follows Word 2021 (or Excel 2021) as measured.** Each such point is marked
   "standard silent; follows Word" below, with the test file that measured it.
3. **Word is measured from its own PDF export** (File > Save As > PDF), not from its screen. Word's screen layout can
   differ from its PDF export of the same file (see "Screen and print" below).

Sources:
- ECMA-376 Part 1, 5th edition (2016). Section numbers below refer to it.
- Microsoft's implementer notes for Word, [MS-OI29500](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/),
  where Microsoft lists the points on which Word departs from the standard.
- Word 2021 PDF exports of the calibration files in `tests/Chuvadi.Layout.Docs.Tests/Fixtures/floating` and
  `tests/Chuvadi.Layout.Docs.Tests/Fixtures/columns` (measured 3 to 5 November 2026).
- Word and Excel 2021 PDF exports and Excel's own width readings for the A7 calibration files (3 November 2026):
  parts B (paragraph mark), C (pictures), D (tables), E (right margin), and the Columns, Default and Overflow sheets.
- WPS Office, observed on screen on 5 November 2026 (no measurements; noted only where it differs visibly).

## Lines, paragraphs and tables (.docx)

| Point | The standard | Word 2021 | Layout |
|---|---|---|---|
| Right margin and indents | The text area's limit | A line 0.0008 pt too wide wraps (A7 E1 to E3: exact, in every mode) | Same: a line may pass the edge only by floating-point noise |
| Height of a line with text | Standard silent | The paragraph mark's size does not raise it (A7 B1: 8 pt text under an 11 pt mark keeps 9.7 pt) | Same (standard silent; follows Word) |
| Height of an empty paragraph | Standard silent | The paragraph mark's size (A7 B3, B4) | Same (standard silent; follows Word) |
| A line of pictures only | Standard silent | Exactly the pictures' height (A7 C1 to C3); a picture in a line of text adds the text's descent (C4) | Same (standard silent; follows Word) |
| Cell margins when the table, its style and Table Normal state none | 115 twips (5.75 pt) left and right, 0 top and bottom (§17.4.34, §17.4.11) | About 0 pt (A7 D1, in both modes); 5.4 pt when the file has Word's Table Normal | **The standard: 5.75 pt.** A Table Normal style in the file still wins |
| The empty paragraph that must end a cell after a nested table | Required; its height not stated | No height, in every mode (A7 D4) | Same (standard silent; follows Word) |

## Workbooks (.xlsx)

| Point | The standard | Excel 2021 | Layout |
|---|---|---|---|
| Column width (`col/@width`) | Truncate(((256·w + Truncate(128/MDW))/256)·MDW) px (§18.3.1.13) | Its own readings are exactly that (7, 14, 35 … 350 px); printing scales each font a little (Calibri ×1.074) | The standard's formula |
| Default column without `defaultColWidth` | baseColWidth × MDW + 5 px: 61 px for Calibri 11 (§18.3.1.81) | Shows 63 px, prints 64 px | **The standard: 61 px** |
| Workbook font missing (Cambria) | MDW of the font the file names | Real Cambria: MDW 8 at 11 pt | Columns sized with real Cambria's digits (554/1000 em, from Word's PDF); the text drawn in Caladea |
| Number-format fill (`*x`) | x is repeated to fill the cell | Same | Same, for any character |
| Text overflowing to the right past the last used column | Standard silent (printing) | Printed in full, adding pages across; a filled neighbour stops it (A7 sheet Overflow) | Same (standard silent; follows Excel): the printed range widens to where the text ends, and the text continues on the page it runs onto |
| Printed range without a print area | Standard silent (printing) | From A1 to the last value, fill or border; every page printed, blank ones too; bold, number format, alignment and other invisible formats on empty cells don't count (D-287, three Excel exports) | Same (D-291) |
| Row height | The stored height | Prints about 14.5 pt for a stored 15 pt | The stored height |
| Row with no stored height | Standard silent | Fits the row to its fonts when it opens the file, using its own font heights (Calibri 16: 28 px; Arial 13 bold: 22 px), never below the default | Same idea, with the font Layout draws: the default while the line fits it, otherwise line height + 2 px to the nearest pixel. Equal to Excel for Calibri; up to a few px apart where the font or its substitute differs (X3) |

## Fonts

| Font the file names | Layout's substitute when it is missing | Metric-compatible? |
|---|---|---|
| Calibri, Arial, Times New Roman, Courier New | Carlito, Liberation Sans, Liberation Serif, Liberation Mono | Yes: breaks are kept |
| Cambria | Caladea | **No:** real Cambria is 6.5% wider and its lines are shorter (measured against Word 2021's embedded Cambria); a `NonMetricFontSubstitution` warning says breaks may move |

Layout uses the real font whenever it is installed and the application turns on `AllowSystemFonts`.

## Tamil and other complex-script text (.docx)

| Point | The standard | Word 2021 (measured, calib-tamil-slots and d04, 5 Nov 2026) | Layout |
|---|---|---|---|
| A run with `<w:cs/>` or `<w:rtl/>` | Every character takes the complex-script font, size and bold (§17.3.2.26 step 2a) | Same | Same |
| A run without them: Tamil and other Indic letters | Not in the table of §17.3.2.26: standard silent | The ordinary (High ANSI) font, size (`w:sz`) and bold; the language tag and `w:hint="cs"` change nothing | Same (standard silent; follows Word) |
| A run without them: Hebrew, Arabic, Syriac, Thaana | The ASCII font (§17.3.2.26 table) | Not measured | The ASCII font (the standard) |
| Spaces between Tamil words | As the characters' slot | The ordinary font | Same |
| The slot's font has no Tamil letters | Standard silent | Vijaya, even with Nirmala UI and Latha installed | **LiPi Sans Tamil, the same on every machine** (D-191; docs/DIFFERENCES.md L-4) |
| The theme's Tamil font (`a:font script="Taml"`) | Not said for WordprocessingML text | Never used (18 cases) | Not used |

## Floating tables (`w:tblpPr`, §17.4.57; `w:tblOverlap`, §17.4.56)

A table with `w:tblpPr` floats. Layout places it at the position the file states and flows the text around it.

| Point | The standard | Word 2021 | Layout |
|---|---|---|---|
| Which tables float | Every table with `w:tblpPr` (§17.4.57) | Ignores `w:tblpPr` when all its attributes are omitted; in text boxes, footnotes, endnotes and comments; and when `tblpX` and `tblpY` are 0, `horzAnchor` is `text` and `vertAnchor` is not `text` (MS-OI29500 2.1.162 note b) | Every table with `w:tblpPr` in the body floats. In a table cell, header, footer, note or text box the table stays in the flow at its horizontal position, with an `UnsupportedFeature` warning |
| Anchors not stated | `page` across and `page` down | `text` (the column) across and `margin` down (MS-OI29500 note c). Measured (T6): border at 172 pt, top at 97 pt. WPS does the same | **The standard: page and page.** T6 is at 100 pt, 25 pt, not where Word and WPS put it |
| `tblpX`, `tblpY` | Used as written | Adds 1 twip when it saves a file and subtracts 1 when it opens one (note e) | As written; at most 0.05 pt from Word |
| Value range | Any integer | 0 to 32,767 only (note d) | Any value; negative offsets place the table before its anchor. Negative distances from text count as 0 |
| Right-to-left paragraphs | The horizontal position is from the right edge | Always from the left edge (note a) | Right-to-left is not laid out yet: from the left edge, with a `BidiNotSupported` warning |
| Relative vertical position with the paragraph anchor (`tblpYSpec` with `vertAnchor="text"`) | Not allowed, and ignored | Ignored | Ignored; `tblpY` is used |
| Column anchor (`horzAnchor="text"`) | The column the anchor paragraph is in | Same (T7: 315.65 + 15 = 330.65 pt) | Same |
| Distances from text | Minimums: text stays at least that far from the table | Lets text run over a margin-anchored table at the top of a page (T4, Word's PDF) | **Text never overlaps a floating table** (the standard's minimums) |
| Text beside the table | Standard silent | Text on both sides, the stated distances away; the right distance is measured from the outside of the right border (T1: 272 + 150 + 0.75 + 9 = 431.8 pt) | Same (standard silent; follows Word) |
| Paragraph-anchored table taller than the space left | Standard silent | Rows to the foot of the page, the rest at the top of the next page at the same x, the following text beside it (T5) | Same (standard silent; follows Word) |
| Page- or margin-anchored table above its paragraph | Standard silent | Placed where stated; the text above its paragraph reflows around it (T3) | Same (standard silent; follows Word) |
| Page- or margin-anchored table reaching past the bottom margin | Placed where stated | Moves the table to the next page and splits it after row 1, leaving the page before almost empty (T2) | **Placed exactly where the file says, whole**: never moved or split (decision A, 3 Nov 2026) |
| `w:tblOverlap` not stated | Other tables may overlap it | Same | Same |
| `w:tblOverlap="never"` | Tables are adjusted so they do not overlap; how is not said | Measured (calib-overlap, 5 Nov 2026): no single rule. Two narrow margin-anchored tables at one spot are drawn on top of each other (O1, O3, against the standard); wide ones, the second moves down to the upper table's distance from text (O2); page-anchored ones, the second moves sideways (O4) | **The table placed later moves down** to the upper table's `bottomFromText` below it (decision B, 3 Nov; distance N5, D-191). See docs/DIFFERENCES.md L-5 |

### Screen and print

Word lays out floating tables in passes, and the result can depend on where it starts. For T4 (a margin-anchored table
at the top of the page that may not overlap, and a paragraph-anchored table below the heading):

| | T4b (may not overlap) | T4a (paragraph-anchored) | Heading |
|---|---|---|---|
| Word 2021 PDF export (3 and 5 Nov 2026, identical) | At the top margin, 72 pt, 72 pt | At its paragraph, 72 pt, 134 pt | Runs over T4b; the tables never meet |
| Word 2021 on screen (5 Nov 2026, estimated from a screenshot) | Moved right, to about 228 pt, beside T4a | Higher, at about 106 pt | Wraps on both sides of T4b |
| WPS Office on screen (5 Nov 2026, estimated from a screenshot) | Drawn at 72 pt, 72 pt, but the text leaves a gap at about 224 to 395 pt, as if T4b had moved right | At its paragraph | Drawn over T4b, which sits under the text |
| Chuvadi.Layout | At the top margin, 72 pt, 72 pt | Moved down below T4b (decision B) | Beside T4b; no text over either table |

Layout places page- and margin-anchored tables first, then the text and the paragraph-anchored tables. The result is the
same every time and never puts text over a table.

## Newspaper columns (`w:cols`, §17.6.4)

| Point | The standard | Word 2021 | Layout |
|---|---|---|---|
| `w:space` not stated | 720 twips (schema default) | 36 pt | 36 pt |
| Unequal widths (`w:col`) | As stated | As stated | As stated |
| Separator position | Centred in the gap | Centred in the gap | Centred in the gap |
| Separator line | Standard silent | Filled black, about 0.75 pt | 0.75 pt black (standard silent; follows Word) |
| Separator height | Standard silent | From the top of the region to the bottom of the longest column's last line; none before an empty column | Same (standard silent; follows Word) |
| Section ending at a next-page break | Standard silent | Not balanced | Not balanced |
| Section ending at a continuous break | Standard silent | Balanced; only the region where the section ends | Same (standard silent; follows Word). `doNotBalanceTextColumns` is not read yet |
| Last section of the document | Standard silent | Not balanced: all text in column 1, no separator | Same (standard silent; follows Word) |
| Column break | The next text starts in the next column | Same; space before is kept at the top of the column | Same |
| Section starting in the next column (`nextColumn`) | Starts in the next column | Same | Same |
| Footnote in a multi-column section | Standard silent | In the column of its reference, at that column's width | Same (standard silent; follows Word) |
| Picture positioned relative to the column | The column the anchor paragraph is in | Same | Same |

## When Layout cannot do what the file says

Each such case is reported in `LaidOutDocument.Warnings` with a code (`UnsupportedFeature`, `BidiNotSupported` and the
others in DESIGN.md §11). Layout never changes a value in the file to make it fit.
