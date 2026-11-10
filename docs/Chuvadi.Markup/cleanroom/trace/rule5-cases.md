# Rule-5 cases: behaviour the specifications do not decide

Every case where the spec is silent (or ambiguous) and only the frozen expected outputs decide what the code
must do. Arun reviews each one under rule 5. Matching the frozen output is still the target; this list makes
every such choice visible. Format:

```
### R5-0001
- Cases: gate case ids (a few examples) and the check ids
- Spec: what the spec says, with the citation, or "silent"
- Frozen behaviour: what the expected outputs show, in your own words
- Rules: R-MD-nnnn, ...
```

Three limit items were already decided by Arun (R5-01 to R5-03, see BRIEF.md section 3 and
gate/data/rule5-rules.txt); they need no entry here.

## Coordinator numbers and these numbers (D-198)

The coordinator's early messages numbered some cases differently. This file keeps its own numbers; the
coordinator uses them from D-198 on. Where the coordinator's number was not listed below, it was the same as here.

| Coordinator | This file | Subject |
|---|---|---|
| R5-0001, R5-0002 | R5-0001, R5-0002 | U+000B, U+000C, U+00A0, U+2028 not blank (decided: follow the spec) |
| R5-0003 | R5-0001, R5-0002 (one part) | U+000C at the start or end of a paragraph or setext heading not stripped (decided as B) |
| R5-0004 | R5-0032 | a label whose only non-space content is U+00A0 is valid (D-157, D-174) |
| R5-0025, R5-0027, R5-0030 | R5-0025, R5-0027, R5-0030 | same numbers (D-157, D-193) |

### R5-0001
- Status: conflict, decided by Arun: follow the spec (U+000B, U+000C, U+00A0 and U+2028 are not blank and are not stripped). The code follows the spec; the cases were refrozen in gate patches 2 and 3 (D-141, D-143) and now pass
- Cases: blocks/P-CM: fuzz-0928, fuzz-1017, fuzz-1631, fuzz-1711, fuzz-2018, fuzz-2164, fuzz-2427, fuzz-2556, fuzz-2661, fuzz-3909. From P2 the same family also fails 56 cases in ast/P-CM, html/P-CM/H-Spec and html/P-CM/H-DataSourcepos (61 per check together with R5-0002; listed in the P2 gate report).
- Spec: CommonMark 0.31.2 "Characters and lines": a blank line holds only spaces or tabs; "Paragraphs": the raw content loses only spaces or tabs at its start and end. U+000B, U+000C, U+00A0 and U+2028 are therefore content.
- Frozen behaviour (pinned down by E-0017): (a) a paragraph whose lines hold only Unicode whitespace (as .NET char.IsWhiteSpace defines it, so U+000B, U+000C, U+00A0, U+2028 too) does not appear; (b) a paragraph's raw content is trimmed at its end of spaces, tabs, U+000C and line endings (so a last line holding only U+000C disappears with its soft break), but not of U+000B, U+00A0 or U+2028. Everywhere else these chars are content in the frozen output too.
- Rules: none. The code follows the spec, as decided (E-0009, E-0017).

### R5-0002
- Status: conflict, decided by Arun: follow the spec (U+000B, U+000C, U+00A0 and U+2028 are not blank and are not stripped). The code follows the spec; the cases were refrozen in gate patches 2 and 3 (D-141, D-143) and now pass
- Cases: blocks/P-CM: fuzz-0150, fuzz-0621, fuzz-1747, fuzz-1867, fuzz-1932; the same 5 also fail in ast/P-CM and both html checks.
- Spec: CommonMark 0.31.2 "ATX headings": the raw content is stripped of leading and trailing spaces or tabs only.
- Frozen behaviour: the end of an ATX heading's content is also trimmed of U+000B and U+000C (with any spaces or tabs before them), in its span and its text; U+00A0 is not trimmed (fuzz-3328).
- Rules: none. The code follows the spec, as decided (E-0009, E-0017).

### R5-0003
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0001, cm-0002, cm-0005, cm-0006, cm-0093, cm-0148, cm-0320, lim-0081 (blocks/P-CM)
- Spec: silent (the spec defines no source spans).
- Frozen behaviour: a leaf block starts at its first char after indentation and container markers (a partly used tab gives the tab's own offset); a block quote starts at its >, a list item at its marker; spans never include the final line ending; a paragraph ends at the end of its last line, trailing spaces included; a block quote ends at the end of the last line that carried its > or reached it lazily; a container ends at the end of the last line whose content reached one of its blocks.
- Rules: R-MD-0059

### R5-0004
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0043, cm-0057, cm-0058, cm-0104, cm-0148, cm-0280, cm-0301 (blocks/P-CM)
- Spec: silent.
- Frozen behaviour: when a block that the line continued (a paragraph, a list, a thematic break) has to give way to a new block on that line, its span runs to the end of that line, so it overlaps the new block (in cm-0148 the paragraph runs to the end of the HTML block's line). A thematic break stays open across blank lines and is replaced only by the next block start (cm-0104: the break spans "---", the blank line and "baz"). Blocks closed because the line did not continue them keep their own end.
- Rules: R-MD-0069, R-MD-0073

### R5-0005
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0067, cm-0071, cm-0073, cm-0079, cm-0647 (blocks/P-CM)
- Spec: silent on spans; CommonMark 0.31.2 "ATX headings" decides the content.
- Frozen behaviour: an ATX heading ends where its content (closing sequence and trailing spaces or tabs removed) ends. An empty heading ends where its content would start: after the spaces that follow the opening run ("## " spans 3 chars), or after the opening run when a closing run was removed ("### ###" spans 3 chars).
- Rules: R-MD-0079

### R5-0006
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0208, cm-0210, cm-0215, cm-0216 (blocks/P-CM)
- Spec: silent.
- Frozen behaviour: when link reference definitions are taken from the start of a paragraph, the remaining paragraph (or the setext heading made from it) keeps the start offset of the first definition. A setext heading ends at the end of its underline line.
- Rules: R-MD-0106, R-MD-0107

### R5-0007
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0192, cm-0317, fuzz-0512, fuzz-2686 (blocks/P-CM)
- Spec: silent.
- Frozen behaviour: a definition spans from its [ to the first content char of the next paragraph line (indentation of that line included in the span), or, when it is the last line of the paragraph, past that line's line ending.
- Rules: R-MD-0042

### R5-0008
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0117, cm-0253, cm-0272, fuzz-0518 (blocks/P-CM), and every fuzz case with CR or CR LF line endings in a code block
- Spec: silent on spans; the spec examples use LF only.
- Frozen behaviour: code and HTML text uses "\n" after every line whatever the source line ending. An indented code block's span includes the blank lines it took in, even though they are trimmed from Code. A blank line taken into a code or HTML block extends the blocks that contain it.
- Rules: R-MD-0097, R-MD-0104

### R5-0009
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0278, cm-0280, cm-0282, cm-0306, fuzz-0115, fuzz-0149, fuzz-0266 (blocks/P-CM)
- Spec: silent.
- Frozen behaviour: a list item starts at its marker; on its marker line it ends at the end of the line, trailing spaces included, even when nothing follows the marker; blank lines that only separate items do not extend it. A list starts at its first item and ends with its last item, unless it gave way to a new block on a later line (R5-0004).
- Rules: R-MD-0075, R-MD-0103

### R5-0010
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: path-0017, path-0019, lim-0001 to lim-0036, lim-0106 (blocks/P-CM)
- Spec: silent (the spec sets no nesting limit).
- Frozen behaviour: with MaxNestingDepth 512, 50,000 nested > markers give 511 block quotes and a paragraph holding the rest as text. A container is opened only while its depth (document 0, every block quote, list and list item one level) stays below the limit; a marker that does not fit is read as text.
- Rules: R-MD-0076

### R5-0011
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0302 (spec example), fuzz-1390, fuzz-1553 (blocks/P-CM)
- Spec: ambiguous. "List items" rule 1 and "HTML blocks" say a new list (unless empty-free and starting at 1) and an HTML block of type 7 cannot interrupt a paragraph, and the laziness clauses make such a line paragraph continuation text; but example 302 starts a new list after a lazy-capable paragraph.
- Frozen behaviour: the interruption limits for a new list and for HTML type 7 apply only when this line continued the open paragraph. When the paragraph sits in a container the line did not continue, "3) baz" starts a new list (cm-0302) and "</a>" alone starts an HTML block (fuzz-1390) instead of being lazy text. Indented code still cannot start in that position (spec examples 236 and 237 keep it lazy).
- Rules: R-MD-0102, R-MD-0105

### R5-0012
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0208 (blocks/P-CM)
- Spec: silent (the spec does not define the AST's Label property).
- Frozen behaviour: Label is the text between the brackets as written, with line endings as "\n" (cm-0208: "\nfoo\n").
- Rules: R-MD-0108

### R5-0013
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0012, cm-0025, cm-0351, cm-0466, cm-0526, cm-0633 to cm-0636, cm-0649, cm-0238 (ast/P-CM)
- Spec: silent (no spans, no rule for splitting text into nodes).
- Frozen behaviour: neighbouring text pieces form one Text node spanning all of them (escapes and entities included). A soft or hard break spans from the line ending (after a backslash for a backslash break) to where the next line entered the paragraph (R5-0014); spaces and tabs before the line ending belong to no node. Emphasis spans its used delimiters and content; the delimiters used are those next to the content, so leftover delimiters stay outside. Links and images span from [ or ! to their last char; code spans include their backtick strings; autolinks include < and >, their text node excludes them.
- Rules: R-MD-0112, R-MD-0128, R-MD-0159

### R5-0014
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0238, cm-0291, cm-0312, cm-0649, fuzz-0480, fuzz-0591, fuzz-0812, fuzz-0988 (ast/P-CM)
- Spec: ambiguous. "Paragraphs" removes initial spaces and tabs of the lines; "Soft line breaks" removes spaces at the beginning of the next line; neither says where the line's text begins for raw HTML or code spans that cross the line, nor for spans.
- Frozen behaviour: a paragraph line that the paragraph continued enters at its first non-space char; a lazy continuation line enters where the container markers stopped, indentation included. Raw HTML crossing into a lazy line keeps that indentation; a code span crossing into a continued line does not. Text after the break starts after the indentation in both cases.
- Rules: R-MD-0162, R-MD-0165

### R5-0015
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: fuzz-0445, fuzz-0759, fuzz-0791, fuzz-1563, fuzz-1623, fuzz-1992, fuzz-2525, fuzz-2947, fuzz-2950 (ast/P-CM)
- Spec: ambiguous. "Soft line breaks" removes spaces at the end of the line; "Paragraphs" strips final spaces or tabs; tabs before an inner line ending are not mentioned.
- Frozen behaviour: spaces and tabs before a line ending inside a paragraph are dropped. Hard breaks still need two or more spaces; the code counts the spaces right before the line ending.
- Rules: R-MD-0164

### R5-0016
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: path-0001, path-0002, path-0005 (ast/P-CM, default MaxNestingDepth 512); lim-0037 to lim-0046 under limit/L-Depth show the same rule at depth 6
- Spec: silent (no nesting limit).
- Frozen behaviour: emphasis and strong emphasis are matched as usual, then any of them at a tree depth of MaxNestingDepth or more (the document is 0) is replaced by its content: its delimiters disappear and its text joins the surrounding text.
- Rules: R-MD-0161

### R5-0017
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: fuzz-0245, fuzz-0353, fuzz-0748, fuzz-1362, fuzz-1805, fuzz-2304, fuzz-3098, fuzz-3564 (html/P-CM/H-Spec)
- Spec: silent ("Images": the description is standardly used as the alt attribute; its text form is not given).
- Frozen behaviour: alt is the description's plain text: text, code span content and raw inline HTML as written, line breaks as a space, all escaped for the attribute. A hard break inside an alt has not been seen in the frozen data; the code also writes a space.
- Rules: R-MD-0183

### R5-0018
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0502, cm-0503, cm-0526, cm-0603 (html/P-CM/H-Spec) and every link destination in the sets
- Spec: silent (the examples show percent-encoded hrefs but the spec sets no encoding rule).
- Frozen behaviour: ASCII letters and digits, -_.!~*'();/?:@&=+$,# and % followed by two hex digits are kept; every other char is percent-encoded as UTF-8 with upper-case hex; then & becomes &amp;.
- Rules: R-MD-0181

### R5-0019
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0192, cm-0482, cm-0526, cm-0549, cm-0562, cm-0569 (ast/P-CM)
- Spec: silent (the AST properties are the library's).
- Frozen behaviour: Label of a full reference is the raw text of its second bracket pair; of a collapsed or shortcut reference the raw link text; null for inline links and autolinks. Style records the form; IsEmail is true for email autolinks, whose Destination carries mailto:.
- Rules: R-MD-0138, R-MD-0139, R-MD-0140

### R5-0020
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0043, cm-0119, cm-0228, cm-0265, cm-0283, cm-0301 (html/P-CM/H-DataSourcepos)
- Spec: silent.
- Frozen behaviour: data-sourcepos="L1:C1-L2:C2" on block elements only, one-based, from the span's first char to its last char inclusive (columns count UTF-16 chars, a tab as one); on an ordered list it follows start="n"; on hr it comes before the closing />.
- Rules: R-MD-0169

### R5-0021
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: ext-0002, ext-0003, ext-0005, ext-0013, fuzz-1011 (paragraph before a table); gfm-0200, ext-0009, ext-0011, doc-0025 (escaped pipes); gfm-0202, gfm-0204, ext-0008 (missing cells); all in ast/P-GFM
- Spec: silent. GFM 0.29 "Tables (extension)" gives the rows and cells, not where a table is tried among the other block starts (beyond the delimiter row needing to match the header), and spans are the library's.
- Frozen behaviour: a table is tried after a setext underline would have been, on a paragraph's last line followed by a line starting with |, : or -; the delimiter row needs no pipe when there is one column. A paragraph whose last line becomes the header ends where that line starts. The table spans from the header row and, like other blocks (R5-0003), is extended to the end of a line that displaces it. A cell spans from its first char for the length of its text after escaped pipes lose their backslash, and positions inside it count on that text; a missing cell has an empty span at the end of its row.
- Rules: R-MD-0188, R-MD-0192, R-MD-0194, R-MD-0195, R-MD-0224

### R5-0022
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: gfm-0279, gfm-0280, ext-0028, ext-0029, ext-0030 (ast/P-GFM and html checks)
- Spec: partly silent. GFM 0.29 "Task list items (extension)" defines the marker and shows a disabled checkbox; it does not say where the paragraph starts or how the other TaskListStyle values are written.
- Frozen behaviour: the paragraph of a task item starts after the marker and its spaces or tabs. Checkbox writes <input type="checkbox" checked="" disabled="" /> (checked="" only when checked) and a space; Glyph writes a span of class task-checked or task-unchecked holding a ballot box and a space; Text writes [x] or [ ] and a space. The marker comes at the start of the item's first paragraph.
- Rules: R-MD-0196, R-MD-0225

### R5-0023
- Status: accepted by Arun (D-199) as matching the frozen output; the tilde rule R-MD-0229 is accepted as built
- Cases: gfm-0491, ext-0017, ext-0018, ext-0026; fuzz-3187, fuzz-3329, fuzz-3792 (a closer meeting a run of another length); fuzz-0270, fuzz-0596 (runs of another length passed over); all in ast/P-GFM
- Spec: silent. GFM 0.29 "Strikethrough (extension)" says text wrapped in one or two tildes, and that a new paragraph ends the parsing; it gives no flanking rules and does not say what happens when runs of different lengths meet.
- Frozen behaviour: runs of one or two tildes open and close by the * flanking rules; longer runs are text. Only runs of equal length pair, the whole run is used and DelimiterLength is its length. When a run that can only close meets, going back, a run that can only open and has another length, that run and every delimiter run between them are dropped and the closer matches nothing; runs of another length that can both open and close are passed over.
- Rules: R-MD-0198, R-MD-0199, R-MD-0229

### R5-0024
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: gfm-0621 to gfm-0631, ext-0019 to ext-0021; fuzz-0112, fuzz-0162, fuzz-0179, fuzz-0196 (references stay raw); fuzz-1163, fuzz-1891, fuzz-3327, fuzz-3542 (address after a rejected prefixed address); all in ast/P-GFM
- Spec: partly silent. GFM 0.29 "Autolinks (extension)" defines www, scheme and email links, the valid domain and the path validation; it does not say whether links are found before or after references are decoded, inside link text, after which chars a scheme link may start, or how prefixed addresses and spans work, and xmpp: is in neither spec file.
- Frozen behaviour: www and scheme links are found while parsing the raw content (so &amp; stays as written in their text and destination), never while a [ or ![ is open, and scheme links not after an ASCII letter or digit. A domain char is anything but whitespace, control chars and ASCII punctuation other than - _ .; the underscore check runs on the whole domain run. A trailing ; is trimmed even when not entity-like, and so are trailing quotes. Email addresses are found afterwards in the decoded, merged text outside links (inside images too); text nodes are merged first, so an address can cross a run of _ that formed no emphasis. An address followed by @, or whose domain ends in a digit, is not a link. A mailto: or xmpp: prefix not after an ASCII letter or digit is included, with local-part chars before it, and the text is the destination; xmpp: may add a /resource; IsEmail is true for both. An address right after the @ of a rejected prefixed address keeps its text as destination. An email link and the text before it are placed by their offset in the decoded text; the text after the last one spans to the end of the original node.
- Rules: R-MD-0201, R-MD-0202, R-MD-0203, R-MD-0205, R-MD-0212, R-MD-0216, R-MD-0217, R-MD-0218, R-MD-0219, R-MD-0220, R-MD-0221, R-MD-0222, R-MD-0223, R-MD-0230

### R5-0025
- Status: conflict, decided by Arun (D-157): follow GFM 0.29 literally; a www. link may start after U+000B and U+000C (R-MD-0245). The end of a link is unchanged (spaces, tabs, line endings, U+000C and <). Gate patch4 refroze the ten U+000B cases; they pass
- Cases: fuzz-0071, fuzz-0353, fuzz-1677, fuzz-2696, fuzz-3556, fuzz-3930 (www. after U+000C); fuzz-0020, fuzz-0187, fuzz-0469, fuzz-0907, fuzz-1425, fuzz-1708, fuzz-2100, fuzz-2610, fuzz-3542, fuzz-3748 (www. after U+000B); all in ast/P-GFM and the GFM html and writer checks
- Spec: GFM 0.29 "Autolinks (extension)": these links come at the start of a line, after whitespace, or after * _ ~ (; GFM 0.29 "Characters and lines" makes U+000B a whitespace character. The same section says a link continues over "non-space non-<" chars, and a space is U+0020 only. CommonMark 0.31.2, the base of everything else here, has no whitespace character term and its Unicode whitespace excludes U+000B.
- Frozen behaviour: www. may follow space, tab, a line ending or U+000C, but not U+000B or other Unicode whitespace. A link ends at space, tab, a line ending, U+000C or <, not at U+000B, U+00A0 or U+2028.
- Decision needed: whether U+000B before www. (and as a link end) follows GFM 0.29's whitespace definition or the frozen output. The code does what the frozen output shows; following GFM 0.29 would fail the ten U+000B cases (E-0022). The end-of-link set is ambiguous rather than in conflict, since "non-space" read literally would let tabs and line endings into links.
- Rules: R-MD-0200 (superseded), R-MD-0245, R-MD-0207

### R5-0026
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: fuzz-0202, fuzz-1000, fuzz-1651, fuzz-1865 (html/P-GFM/H-GfmSpec)
- Spec: silent (as R5-0018).
- Frozen behaviour: in addition to R5-0018, an apostrophe in a URL attribute is written as &#x27;.
- Rules: R-MD-0228

### R5-0027
- Status: confirmed by Arun as built (D-157)
- Cases: path-0024 (blocks/P-GFM, ast/P-GFM, html/P-GFM/H-GfmSpec, writer/P-GFM/W-Default); fuzz-1007 (ast/P-GFM, the header side)
- Spec: silent. GFM 0.29 "Tables (extension)" says spaces between pipes and cell content are trimmed, and its "Characters and lines" makes U+000B and U+000C whitespace characters, but it does not say whether whitespace after the last delimiter cell is ignored.
- Frozen behaviour: a line of "-" followed by U+000B is a delimiter row, so "bbb" above it becomes a one-column table. A header or body row ending in U+000B keeps it in its last cell (fuzz-1007).
- Rules: R-MD-0233

### R5-0028
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0021, cm-0148, cm-0149, cm-0153, cm-0154, cm-0155 (html/P-GFM/H-Default, H-Alt2, H-Out)
- Spec: silent (raw HTML modes are the library's).
- Frozen behaviour: with RawHtml Escape an HTML block becomes <p>, its escaped text without the final line ending, and </p>; inline HTML is escaped in place. Strip leaves both out. Unsafe URLs (javascript: and the others listed in UnsafeUrlPolicy) become an empty href or src.
- Rules: R-MD-0235

### R5-0029
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: cm-0062, cm-0068, cm-0072, cm-0076 (repeated slugs); fuzz-1626, fuzz-1760, fuzz-2176, fuzz-2407 (html/P-GFM/H-Alt1, H-Alt2)
- Spec: silent ("GitHub-style" in the option's documentation only).
- Frozen behaviour: the slug is the heading's plain text without raw inline HTML, trimmed of whitespace (U+00A0 included), lower-cased; letters, digits, - and _ stay, each space becomes -, every other char (U+000B and U+00A0 inside the text too) is dropped. A slug used before in the same rendering gets -1, -2, ... The prefix is put in front.
- Rules: R-MD-0236, R-MD-0237

### R5-0030
- Status: decided by Arun (D-193): confirmed as built. A list item may sit at the depth limit, except where it would interrupt a paragraph there; block quotes stay below the limit. The maintainer will fix the MarkupLimits documentation wording in the given library
- Cases: cm-0009, cm-0294, cm-0299, cm-0307, gfm-0277, doc-0012, doc-0024, fuzz-3603, lim-0006 to lim-0010 (limit/L-Depth); fuzz-0112, fuzz-0826 (strikethrough)
- Spec: silent (no nesting limit). The MarkupLimits documentation says containers deeper than MaxNestingDepth are not opened.
- Frozen behaviour: block quotes open only while their depth stays below MaxNestingDepth (R-MD-0076). A list or list item opens while its first new level stays below the limit, counted from the deeper of its parent and the last block the line continued, so an item may sit at the limit itself, but not when it would interrupt a paragraph at that depth. Strikethrough past the limit loses its formatting like emphasis (R5-0016).
- Rules: R-MD-0238, R-MD-0239

### R5-0031
- Status: accepted by Arun (D-199) as matching the frozen output
- Cases: probe/render-subtree (contract)
- Spec: silent (rendering a subtree is the library's).
- Frozen behaviour: a node rendered on its own is written as inside its document, except: a task item's paragraph gets no marker; a table row or cell ends at its closing tag (the parent writes the line endings), a header row still has </thead> and a first body row <tbody>; a cell rendered on its own is a td.
- Rules: R-MD-0240, R-MD-0241

### R5-0032
- Status: decided by Arun (D-157): a link label whose only non-space content is U+00A0 is a valid label, per the spec. (The coordinator's list calls this R5-0004; the R5-0004 above is a different case.)
- Cases: lab-0001 to lab-0041 and probe/normalize-label (added in gate patch5).
- Spec: CommonMark 0.31.2 "Links": a link label must hold at least one character that is not a space, tab or line ending; U+00A0 is none of these. Matching folds case and collapses spaces, tabs and line endings only, so U+00A0 stays part of the label.
- Frozen behaviour: patch4 changed no output. D-174 (patch5) then made the given NormalizeLabel follow the spec: it trims and collapses only space, tab, LF and CR, so U+000B, U+000C and U+00A0 stay in the key. The 41 label cases test this.
- Code: unchanged by D-157 and D-174. The label scanner (R-MD-0032) treats only space, tab, LF and CR as blank; raw labels go to NormalizeLabel untrimmed (R-MD-0108, R-MD-0141), so it does the only trimming, with those four characters.
- Rules: R-MD-0032

### R5-0033
- Status: decided by Arun (D-232, D-237): groups G1 to G6, N1 to N12, C1 to C4 and the heading-id rules, pinned by the sets diff-core, diff-full and slugs (gate patch7)
- Cases: dc-0001 to dc-2086, df-0001 to df-1379, slug-0001 to slug-0026
- Spec: silent or unclear for the "as frozen" groups (G1, G3, G4, G6, N1, N3, N4, N5, N7, N9, C1, slugs); the "spec" groups (G2, N2, N6, N11, C2, C3, C4) follow the GFM 0.29 and CommonMark 0.31.2 text as the ruling reads it.
- Frozen behaviour: as the ruling lists it in BRIEF.md. Where the data go beyond or against the ruling's words: a task marker with nothing after it ("- [ ] ", "1. [x] ") gives a task item with no paragraph in the expected output, not plain text, while "- [\t] " is plain text (R-MD-0256 to R-MD-0258); "_" and "-" may start a domain (R-MD-0251); U+2028 after the scheme still links when an entity follows (R-MD-0250); spans of lazy lines inside a partly used tab run past the input end (R-MD-0266, placed at the end by R-MD-0272); no case tests N11 beyond the earlier R-MD-0196.
- Rules: R-MD-0247 to R-MD-0272

### R5-0034
- Status: decided by Arun (D-242): held domain-edge cases, pinned by the set diff-domain (gate patch8)
- Cases: dd-0001 to dd-1336
- Spec: GFM 0.29 "Autolinks (extension)", valid domain; silent on a domain that starts with a period or a hyphen, and on escapes inside it.
- Frozen behaviour: a domain may not start with a period; single-label and hyphen-only hosts link after http://, https:// and ftp://; a backslash before "_" right after the scheme ends the domain ("http://-\\_" links "http://-\\").
- Rules: R-MD-0251, R-MD-0252
