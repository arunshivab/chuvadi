# Chuvadi.Layout: how a document is laid out, point by point

This page says, for each layout point Chuvadi.Layout handles, what the file format's standard says, what Word 2021
does, and what Layout does. Where Layout differs from Word or WPS Office on purpose, it says so here.

## The rule

1. **The file and its standard decide.** For .docx and .xlsx that is ECMA-376 Part 1, 5th edition (2016). Where the
   standard is clear and Word does something else, Layout follows the standard.
2. **Where the standard is silent, Layout follows Word 2021 as measured.** Each such point is marked "standard silent;
   follows Word" below, with the test file that measured it.
3. **Word is measured from its own PDF export** (File > Save As > PDF), not from its screen. Word's screen layout can
   differ from its PDF export of the same file (see "Screen and print" below).

Sources:
- ECMA-376 Part 1, 5th edition (2016). Section numbers below refer to it.
- Microsoft's implementer notes for Word, [MS-OI29500](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/),
  where Microsoft lists the points on which Word departs from the standard.
- Word 2021 PDF exports of the calibration files in `tests/Chuvadi.Layout.Docs.Tests/Fixtures/floating` and
  `tests/Chuvadi.Layout.Docs.Tests/Fixtures/columns` (measured 3 to 5 November 2026).
- WPS Office, observed on screen on 5 November 2026 (no measurements; noted only where it differs visibly).

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
| `w:tblOverlap="never"` | Tables are adjusted so they do not overlap; how is not said | Not reached in Word's PDF of T4: the two tables never meet there. On screen, Word moved the table sideways (see "Screen and print") | **The table placed later moves down** until it is clear (decision B, 3 Nov 2026). Standard silent on the direction; Word's print behaviour is not yet measured |

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
