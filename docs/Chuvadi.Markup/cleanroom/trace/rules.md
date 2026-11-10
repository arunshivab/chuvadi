# Trace log: rules of the clean-room Markdown parser and renderer

One entry per behaviour rule. The code carries `// TRACE R-MD-nnnn` at the place that implements the rule.
The log is append-only: a changed rule gets a new entry, and the old one gets `- Status: superseded by R-MD-nnnn`.
`gate trace` checks this file against the code. Format (fields in any order):

```
### R-MD-0001
- Location: Markdown/Parsing/SomeFile.cs, SomeType.SomeMethod
- Source: spec
- Citation: CommonMark 0.31.2 "Thematic breaks", examples 43-61
- Rule: one line in your own words
```

- `Location`: path relative to `src/Chuvadi.Markup/`, then the type and method.
- `Source`: `spec`, `experiment` or `general`.
  - `spec`: `Citation` is one or more of `CommonMark 0.31.2 "<heading>"`, `GFM 0.29 "<heading>"` or
    `GFM extensions "<heading>"`, each optionally followed by `, example N` or `, examples N-M`, separated by `;`.
    The heading must be a heading of the bundled spec file, written exactly.
  - `experiment`: the rule was found by comparing with the frozen expected outputs. Add
    `- Experiment: E-nnnn` (a file in `trace/experiments/`) and `- Rule5: R5-nnnn` (a case in
    `trace/rule5-cases.md`), or `- Rule5: spec-covered` together with a spec `Citation` when the spec does
    decide the case and the experiment only confirmed your reading.
  - `general`: ordinary engineering with no Markdown content (buffers, stacks, a hash map, iteration instead
    of recursion). `Citation` is a short description.
- `Rule`: one line in your own words.

### R-MD-0001
- Location: Markdown/Parsing/LineCursor.cs, LineCursor.Load
- Source: spec
- Citation: CommonMark 0.31.2 "Characters and lines"
- Rule: A line ends at a line feed, a carriage return, or a carriage return followed by a line feed, or at the end of input.

### R-MD-0002
- Location: Markdown/Parsing/LineCursor.cs, LineCursor.TabStopAfter
- Source: spec
- Citation: CommonMark 0.31.2 "Tabs", examples 1-11
- Rule: Where tabs matter for block structure they advance to the next multiple of four columns.

### R-MD-0003
- Location: Markdown/Parsing/LineCursor.cs, LineCursor.RestIsBlank
- Source: spec
- Citation: CommonMark 0.31.2 "Characters and lines"
- Rule: The rest of a line counts as blank when it holds nothing but spaces and tabs.

### R-MD-0004
- Location: Markdown/Parsing/LineCursor.cs, LineCursor.AppendRest
- Source: spec
- Citation: CommonMark 0.31.2 "Tabs", examples 1-11
- Rule: When block structure uses only part of a tab, the columns left over enter the content as spaces; other tabs stay literal.

### R-MD-0005
- Location: Markdown/Parsing/CharacterReferences.cs, CharacterReferences.Match
- Source: spec
- Citation: CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: A hexadecimal reference is &# then x or X, one to six hex digits, then a semicolon.

### R-MD-0006
- Location: Markdown/Parsing/CharacterReferences.cs, CharacterReferences.Match
- Source: spec
- Citation: CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: A decimal reference is &# then one to seven digits, then a semicolon.

### R-MD-0007
- Location: Markdown/Parsing/CharacterReferences.cs, CharacterReferences.Match
- Source: spec
- Citation: CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: A numeric reference to code point zero, a surrogate or a value past U+10FFFF yields U+FFFD.

### R-MD-0008
- Location: Markdown/Parsing/CharacterReferences.cs, CharacterReferences.Match
- Source: spec
- Citation: CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: A named reference is & then a name from the HTML5 entity table then a semicolon; names without the semicolon are not recognised.

### R-MD-0009
- Location: Markdown/Parsing/CharacterReferences.cs, CharacterReferences.Resolve
- Source: spec
- Citation: CommonMark 0.31.2 "Backslash escapes", examples 12-24; CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: In destinations, titles and info strings a backslash before ASCII punctuation yields that char, and character references are decoded.

### R-MD-0010
- Location: Markdown/Parsing/CharacterReferences.cs, CharacterReferences.SafeChar
- Source: spec
- Citation: CommonMark 0.31.2 "Insecure characters"
- Rule: U+0000 is replaced by U+FFFD in every text the parser produces.

### R-MD-0011
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.s_literalContentTags
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: The literal-content tags of HTML block type 1 are pre, script, style and textarea.

### R-MD-0012
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.s_blockTags
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: HTML block type 6 uses the fixed list of block-level tag names given by the spec.

### R-MD-0013
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockStart
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 1 starts with < and a literal-content tag name (any case) followed by space, tab, > or end of line.

### R-MD-0014
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockStart
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 2 starts with <!-- .

### R-MD-0015
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockStart
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 3 starts with <? .

### R-MD-0016
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockStart
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 4 starts with <! followed by an ASCII letter.

### R-MD-0017
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockStart
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 5 starts with <![CDATA[ .

### R-MD-0018
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockStart
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 6 starts with < or </ and a block-level tag name (any case) followed by space, tab, end of line, > or />.

### R-MD-0019
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockStart
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 7 is a complete open tag (not a literal-content tag) or closing tag alone on its line apart from trailing spaces and tabs.

### R-MD-0020
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockEnds
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 1 ends on a line containing </pre>, </script>, </style> or </textarea> in any case.

### R-MD-0021
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockEnds
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 2 ends on a line containing -->.

### R-MD-0022
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockEnds
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 3 ends on a line containing ?>.

### R-MD-0023
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockEnds
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 4 ends on a line containing >.

### R-MD-0024
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.BlockEnds
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: Type 5 ends on a line containing ]]>.

### R-MD-0025
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.SkipTagWhitespace
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: Whitespace inside a tag is spaces and tabs with at most one line ending.

### R-MD-0026
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.TagName
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: A tag name is an ASCII letter followed by ASCII letters, digits or hyphens.

### R-MD-0027
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.OpenTag
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: An open tag ends with optional whitespace, an optional slash and >.

### R-MD-0028
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.OpenTag
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: Each attribute needs whitespace before it; its name is a letter, _ or : then letters, digits, _, ., : or -.

### R-MD-0029
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.OpenTag
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: An attribute value follows = (whitespace allowed around it) and is unquoted, single-quoted or double-quoted.

### R-MD-0030
- Location: Markdown/Parsing/HtmlSyntax.cs, HtmlSyntax.ClosingTag
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: A closing tag is </, a tag name, optional whitespace and >.

### R-MD-0031
- Location: Markdown/Parsing/LinkSyntax.cs, LinkSyntax.MaxLabelChars
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A link label holds at most 999 chars between its brackets.

### R-MD-0032
- Location: Markdown/Parsing/LinkSyntax.cs, LinkSyntax.Label
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A label ends at the first unescaped ], may not contain an unescaped [, and needs a char other than whitespace.

### R-MD-0033
- Location: Markdown/Parsing/LinkSyntax.cs, LinkSyntax.Destination
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A destination in angle brackets has no line ending and no unescaped < or >.

### R-MD-0034
- Location: Markdown/Parsing/LinkSyntax.cs, LinkSyntax.Destination
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A bare destination is non-empty, has no space or control char, and only balanced or escaped parentheses.

### R-MD-0035
- Location: Markdown/Parsing/LinkSyntax.cs, LinkSyntax.Title
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A title is in double quotes, single quotes or parentheses; the closing delimiter may appear inside only when escaped.

### R-MD-0036
- Location: Markdown/Parsing/LinkSyntax.cs, LinkSyntax.Title
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A title may span lines but may not contain a blank line.

### R-MD-0037
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: spec
- Citation: CommonMark 0.31.2 "Link reference definitions", examples 192-218
- Rule: A definition starts a line of the paragraph with [ and at most three columns of indentation.

### R-MD-0038
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: spec
- Citation: CommonMark 0.31.2 "Link reference definitions", examples 192-218
- Rule: The label must be followed directly by a colon.

### R-MD-0039
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: spec
- Citation: CommonMark 0.31.2 "Link reference definitions", examples 192-218
- Rule: After the colon and optional whitespace (one line ending at most) comes the destination, which may be empty only in angle brackets.

### R-MD-0040
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: spec
- Citation: CommonMark 0.31.2 "Link reference definitions", examples 192-218
- Rule: A title must be separated from the destination by whitespace and be followed only by spaces or tabs on its line.

### R-MD-0041
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: spec
- Citation: CommonMark 0.31.2 "Link reference definitions", examples 192-218
- Rule: Without a valid title, the definition stands if the destination ends its line; otherwise the text is not a definition.

### R-MD-0042
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: experiment
- Rule: A definition span runs from its [ to the content start of the next paragraph line, or past the line ending of its last line.
- Experiment: E-0005
- Rule5: R5-0007

### R-MD-0043
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: spec
- Citation: CommonMark 0.31.2 "Link reference definitions", examples 192-218
- Rule: When several definitions share a normalized label, the first one is kept.

### R-MD-0044
- Location: Markdown/Parsing/LinkReferenceReader.cs, DefinitionBudget
- Source: general
- Citation: MarkupLimits.MaxLinkReferenceDefinitions contract: definitions past the limit are read as text
- Rule: Counts kept definitions; once the limit is reached no further definition is recognised.

### R-MD-0045
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.LineOf
- Source: general
- Citation: forward scan from the current line keeps definition reading linear
- Rule: Finds the paragraph line holding a position by scanning forward from the current line.

### R-MD-0046
- Location: Markdown/Parsing/BlockState.cs, InlineContent.ToSource
- Source: general
- Citation: binary search over line starts
- Rule: Maps a position in the joined paragraph text back to its source offset.

### R-MD-0047
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Run
- Source: spec
- Citation: CommonMark 0.31.2 "Phase 1: block structure"
- Rule: The block structure is built by reading the input one line at a time.

### R-MD-0048
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Run
- Source: spec
- Citation: CommonMark 0.31.2 "Phase 1: block structure"
- Rule: At the end of input every block still open is closed.

### R-MD-0049
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Phase 1: block structure"
- Rule: When the deepest continued block is a code block or an HTML block, no new block start is looked for on that line.

### R-MD-0050
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Indented code blocks", examples 107-118
- Rule: Four or more columns of indentation start an indented code block, except where the line could continue an open paragraph.

### R-MD-0051
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Block quotes", examples 228-252
- Rule: A > after at most three columns of indentation opens a block quote; one following space or tab column belongs to the marker.

### R-MD-0052
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Setext headings", examples 80-106
- Rule: A setext underline is only recognised under a paragraph that this line continued (not a lazy line).

### R-MD-0053
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Thematic breaks", examples 43-61
- Rule: A thematic break line opens a thematic break holding its marker char.

### R-MD-0054
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Block quotes", examples 228-252; CommonMark 0.31.2 "List items", examples 253-300
- Rule: A non-blank line that starts no block and is not continued by every open container is a lazy continuation of an open paragraph.

### R-MD-0055
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: A blank line is noted on the blocks it passes through, up to the nearest block quote, unless it opened a block itself.

### R-MD-0056
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: HTML blocks of types 1 to 5 close after the line (the first one included) that meets their end condition.

### R-MD-0057
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Paragraphs", examples 219-226
- Rule: Any other non-blank remainder starts a paragraph.

### R-MD-0058
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ClearBlankFlags
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: A non-blank line ends any run of blank lines for the blocks it reaches.

### R-MD-0059
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.TouchAncestors
- Source: experiment
- Rule: A container ends at the end of the last line whose content reached one of its blocks.
- Experiment: E-0001
- Rule5: R5-0003

### R-MD-0060
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "Block quotes", examples 228-252
- Rule: An open block quote continues on a line with > after at most three columns of indentation.

### R-MD-0061
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "Phase 1: block structure"
- Rule: A list itself never stops a line; its items decide.

### R-MD-0062
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300
- Rule: A blank line ends a list item that has no content yet (an item can begin with at most one blank line).

### R-MD-0063
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300
- Rule: A list item continues on a line indented at least as far as its content indentation, which is then consumed.

### R-MD-0064
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "Paragraphs", examples 219-226
- Rule: A paragraph continues on any non-blank line.

### R-MD-0065
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "Indented code blocks", examples 107-118
- Rule: Indented code continues on lines with four columns of indentation and on blank lines.

### R-MD-0066
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "Fenced code blocks", examples 119-147
- Rule: A closing fence (at most three columns of indentation) ends the fenced code block and uses up its line.

### R-MD-0067
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "Fenced code blocks", examples 119-147
- Rule: Content lines lose up to as many columns of indentation as the opening fence had.

### R-MD-0068
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: HTML blocks of types 6 and 7 end before a blank line.

### R-MD-0069
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: experiment
- Rule: An open thematic break is continued by every line (blank lines included) until a new block takes its place.
- Experiment: E-0002
- Rule5: R5-0004

### R-MD-0070
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.CanContain
- Source: spec
- Citation: CommonMark 0.31.2 "Container blocks and leaf blocks"; CommonMark 0.31.2 "Lists", examples 301-326
- Rule: Documents, block quotes and list items hold any block but a list item; a list holds only list items; leaves hold no blocks.

### R-MD-0071
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Open
- Source: spec
- Citation: CommonMark 0.31.2 "Phase 1: block structure"
- Rule: Before a new block is added, unmatched blocks are closed and so is every block that cannot hold it.

### R-MD-0072
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Open
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: A list is loose when a new item, or a new block inside an item that already has one, follows a blank line.

### R-MD-0073
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Close
- Source: experiment
- Rule: A block that the line continued but that has to give way to a new block ends at the end of that line.
- Experiment: E-0002
- Rule5: R5-0004

### R-MD-0074
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Close
- Source: spec
- Citation: CommonMark 0.31.2 "Indented code blocks", examples 107-118
- Rule: Blank lines at the end of an indented code block are not part of its code.

### R-MD-0075
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Close
- Source: experiment
- Rule: A list starts at its first item and ends where its last item ends, unless it gave way to a new block on a later line.
- Experiment: E-0006
- Rule5: R5-0009

### R-MD-0076
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.HasRoom
- Source: experiment
- Rule: A container (list and item each count) is opened only while its depth, the document being 0, stays below MaxNestingDepth; otherwise its marker is read as text.
- Experiment: E-0007
- Rule5: R5-0010

### R-MD-0077
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryAtxHeading
- Source: spec
- Citation: CommonMark 0.31.2 "ATX headings", examples 62-79
- Rule: One to six # after at most three columns of indentation, followed by space, tab or end of line, open an ATX heading of that level.

### R-MD-0078
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryAtxHeading
- Source: spec
- Citation: CommonMark 0.31.2 "ATX headings", examples 62-79
- Rule: A closing run of # preceded by a space or tab (or directly after the opening) is dropped, and the content is trimmed of spaces and tabs.

### R-MD-0079
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryAtxHeading
- Source: experiment
- Rule: An ATX heading ends where its trimmed content ends; an empty heading ends where its content would have started.
- Experiment: E-0003
- Rule5: R5-0005

### R-MD-0080
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryFenceOpening
- Source: spec
- Citation: CommonMark 0.31.2 "Fenced code blocks", examples 119-147
- Rule: A code fence is three or more backticks or three or more tildes.

### R-MD-0081
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryFenceOpening
- Source: spec
- Citation: CommonMark 0.31.2 "Fenced code blocks", examples 119-147
- Rule: The info string is the rest of the opening line trimmed of spaces and tabs.

### R-MD-0082
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryFenceOpening
- Source: spec
- Citation: CommonMark 0.31.2 "Fenced code blocks", examples 119-147
- Rule: The info string of a backtick fence may not contain a backtick.

### R-MD-0083
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryFenceOpening
- Source: spec
- Citation: CommonMark 0.31.2 "Backslash escapes", examples 12-24; CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: Backslash escapes and character references are resolved in the info string.

### R-MD-0084
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.IsClosingFence
- Source: spec
- Citation: CommonMark 0.31.2 "Fenced code blocks", examples 119-147
- Rule: A closing fence uses the opening char, is at least as long, and is followed only by spaces or tabs.

### R-MD-0085
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.IsSetextUnderline
- Source: spec
- Citation: CommonMark 0.31.2 "Setext headings", examples 80-106
- Rule: A setext underline is a run of = or of - followed only by spaces or tabs.

### R-MD-0086
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TurnIntoSetextHeading
- Source: spec
- Citation: CommonMark 0.31.2 "Link reference definitions", examples 215-216
- Rule: Definitions at the start of the paragraph are taken out first; with nothing left the underline is read as paragraph text.

### R-MD-0087
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TurnIntoSetextHeading
- Source: spec
- Citation: CommonMark 0.31.2 "Setext headings", examples 80-106
- Rule: The paragraph becomes a heading of level 1 for = and level 2 for -.

### R-MD-0088
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.IsThematicBreak
- Source: spec
- Citation: CommonMark 0.31.2 "Thematic breaks", examples 43-61
- Rule: Three or more of the same char among *, - and _, with only spaces and tabs between and after them, form a thematic break.

### R-MD-0089
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryListItem
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300
- Rule: A bullet list marker is -, + or *.

### R-MD-0090
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryListItem
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300
- Rule: An ordered list marker is one to nine digits followed by . or ).

### R-MD-0091
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryListItem
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300
- Rule: A list marker must be followed by a space, a tab or the end of the line.

### R-MD-0092
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryListItem
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300; CommonMark 0.31.2 "Lists", examples 301-326
- Rule: A new list interrupting a paragraph may not start with a blank item and, if ordered, must start at 1.

### R-MD-0093
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryListItem
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300
- Rule: The content of an item starts one to four columns after the marker; five or more, or nothing, count as one.

### R-MD-0094
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryListItem
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: An item that is not of the same type as the open list starts a new list, which takes the start number of its first item.

### R-MD-0095
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.SameType
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: Items are of the same type when they share the bullet char, or are ordered with the same delimiter.

### R-MD-0096
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.AddParagraphLine
- Source: spec
- Citation: CommonMark 0.31.2 "Paragraphs", examples 219-226
- Rule: Each paragraph line loses its leading spaces and tabs.

### R-MD-0097
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.AddLiteralLine
- Source: experiment
- Rule: Code and HTML block lines are stored with "\n" as line ending whatever the source line ending was.
- Experiment: E-0004
- Rule5: R5-0008

### R-MD-0098
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.FinishParagraph
- Source: spec
- Citation: CommonMark 0.31.2 "Phase 1: block structure"; CommonMark 0.31.2 "Link reference definitions", examples 192-218
- Rule: When a paragraph closes, definitions at its start are taken out; a paragraph left empty is dropped.

### R-MD-0099
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.BuildContent
- Source: spec
- Citation: CommonMark 0.31.2 "Paragraphs", examples 219-226
- Rule: The raw inline content is the lines joined by line endings with final spaces and tabs removed.

### R-MD-0100
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Close
- Source: spec
- Citation: CommonMark 0.31.2 "ATX headings", examples 62-79; CommonMark 0.31.2 "Setext headings", examples 80-106
- Rule: A heading keeps its raw content for inline parsing.

### R-MD-0101
- Location: Markdown/Parsing/MarkdownEngine.cs, MarkdownEngine.Parse
- Source: spec
- Citation: CommonMark 0.31.2 "Overview"
- Rule: Parsing builds the block structure first and the inline content second.

### R-MD-0102
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: experiment
- Citation: CommonMark 0.31.2 "Lists", example 302
- Rule: The paragraph-interruption limits on a new list apply only when this line continued the paragraph, not to a lazy line.
- Experiment: E-0008
- Rule5: R5-0011

### R-MD-0103
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TryListItem
- Source: experiment
- Rule: A list item starts at its marker and at first ends at the end of its marker line, trailing spaces included.
- Experiment: E-0006
- Rule5: R5-0009

### R-MD-0104
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: experiment
- Rule: A blank line taken into a code or HTML block extends the blocks that contain it.
- Experiment: E-0004
- Rule5: R5-0008

### R-MD-0105
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: experiment
- Rule: An HTML block of type 7 is refused only when this line continued an open paragraph, not after a lazy-capable one.
- Experiment: E-0008
- Rule5: R5-0011

### R-MD-0106
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TurnIntoSetextHeading
- Source: experiment
- Rule: A setext heading spans from the paragraph start (taken-out definitions included) to the end of its underline line.
- Experiment: E-0005
- Rule5: R5-0006

### R-MD-0107
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.FinishParagraph
- Source: experiment
- Rule: A paragraph keeps its start offset even when definitions are taken from its first lines.
- Experiment: E-0005
- Rule5: R5-0006

### R-MD-0108
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: experiment
- Rule: A definition label is its text between the brackets, with each line ending as "\n" and continuation-line indentation removed.
- Experiment: E-0005
- Rule5: R5-0012

### R-MD-0109
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ParseInto
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: At the end of the content, emphasis is processed over the whole delimiter list.

### R-MD-0110
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.PlainText
- Source: spec
- Citation: CommonMark 0.31.2 "Textual content", examples 650-652
- Rule: Characters with no other interpretation are literal text.

### R-MD-0111
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.LineEnding
- Source: spec
- Citation: CommonMark 0.31.2 "Hard line breaks", examples 633-647; CommonMark 0.31.2 "Soft line breaks", examples 648-649
- Rule: A line ending after two or more spaces is a hard break; any other line ending is a soft break.

### R-MD-0112
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.LineEnding
- Source: experiment
- Rule: A line break spans from the line ending to where the next line entered the paragraph.
- Experiment: E-0010
- Rule5: R5-0013

### R-MD-0113
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.Backslash
- Source: spec
- Citation: CommonMark 0.31.2 "Hard line breaks", examples 633-647
- Rule: A backslash right before a line ending is a hard break.

### R-MD-0114
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.Backslash
- Source: spec
- Citation: CommonMark 0.31.2 "Backslash escapes", examples 12-24
- Rule: A backslash before ASCII punctuation yields that char as text; before anything else it is a literal backslash.

### R-MD-0115
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.Entity
- Source: spec
- Citation: CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: A valid character reference becomes its char as text; otherwise & is literal.

### R-MD-0116
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.IndexBacktickRuns
- Source: general
- Citation: an index of backtick strings by length makes each closer search constant amortised time
- Rule: Lists the start of every backtick string of each length once per block.

### R-MD-0117
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.CodeSpanAt
- Source: spec
- Citation: CommonMark 0.31.2 "Code spans", examples 328-349
- Rule: A code span closes at the next backtick string of the same length; without one the backticks are literal.

### R-MD-0118
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.CodeSpanAt
- Source: spec
- Citation: CommonMark 0.31.2 "Code spans", examples 328-349
- Rule: Line endings inside a code span become spaces.

### R-MD-0119
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.CodeSpanAt
- Source: spec
- Citation: CommonMark 0.31.2 "Code spans", examples 328-349
- Rule: One space is removed at each end when both ends are spaces and the content is not all spaces.

### R-MD-0120
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.DelimiterRunAt
- Source: spec
- Citation: CommonMark 0.31.2 "Emphasis and strong emphasis", examples 350-481
- Rule: Left- and right-flanking are decided from the chars around the run, the start and end counting as whitespace.

### R-MD-0121
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.DelimiterRunAt
- Source: spec
- Citation: CommonMark 0.31.2 "Emphasis and strong emphasis", examples 350-481
- Rule: A * run can open when left-flanking and close when right-flanking.

### R-MD-0122
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.DelimiterRunAt
- Source: spec
- Citation: CommonMark 0.31.2 "Emphasis and strong emphasis", examples 350-481
- Rule: An _ run can open when left-flanking and not right-flanking unless preceded by punctuation; closing mirrors this.

### R-MD-0123
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.BreaksRuleOfThree
- Source: spec
- Citation: CommonMark 0.31.2 "Emphasis and strong emphasis", examples 350-481
- Rule: When either run can both open and close, run lengths summing to a multiple of 3 cannot pair unless both are multiples of 3.

### R-MD-0124
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: Openers below a remembered floor per char, closer length mod 3 and closer-can-open are not searched again.

### R-MD-0125
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: For each closer, the nearest earlier opener of the same char that passes the rule of three is used.

### R-MD-0126
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: With no opener the floor moves up to the closer, and a closer that cannot open leaves the list.

### R-MD-0127
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: spec
- Citation: CommonMark 0.31.2 "Emphasis and strong emphasis", examples 350-481
- Rule: Two chars from each run make strong emphasis when both have two or more; otherwise one char makes emphasis.

### R-MD-0128
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: experiment
- Rule: Emphasis spans its used delimiter chars and content; delimiter runs are taken from the content side.
- Experiment: E-0010
- Rule5: R5-0013

### R-MD-0129
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: Delimiters between a matched opener and closer leave the list.

### R-MD-0130
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: After processing, every delimiter above the bottom leaves the list.

### R-MD-0131
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.OpenBracket
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: A [ or ![ becomes literal text and an entry on the bracket stack.

### R-MD-0132
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.CloseBracket
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: An inactive [ opener is dropped and the ] is literal.

### R-MD-0133
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.CloseBracket
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: If no link or image follows, the opener is dropped and the ] is literal.

### R-MD-0134
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.CloseBracket
- Source: spec
- Citation: CommonMark 0.31.2 "An algorithm for parsing nested emphasis and links"
- Rule: Emphasis inside link text is processed before the link node takes its content.

### R-MD-0135
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.CloseBracket
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: After a link, every earlier [ opener becomes inactive, so links never contain links.

### R-MD-0136
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.TryLinkTail
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: An inline link tail is tried first, directly after the ].

### R-MD-0137
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.TryLinkTail
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: Otherwise a following label makes a full reference; [] makes a collapsed one.

### R-MD-0138
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.TryLinkTail
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A full reference needs its label to match a definition; if it does not, there is no link.

### R-MD-0139
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.TryLinkTail
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A collapsed reference uses the link text as its label.

### R-MD-0140
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.TryLinkTail
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A shortcut reference uses the link text as its label when no label follows.

### R-MD-0141
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.Lookup
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: Labels match by their normalized form.

### R-MD-0142
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.SkipLinkSpace
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: Parts of an inline link may be separated by spaces, tabs and at most one line ending.

### R-MD-0143
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.TryInlineTail
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: The destination is in angle brackets or bare; escapes and references are resolved.

### R-MD-0144
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.TryInlineTail
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A title must be separated from the destination by whitespace.

### R-MD-0145
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.AngleBracket
- Source: spec
- Citation: CommonMark 0.31.2 "Autolinks", examples 594-612
- Rule: A URI autolink becomes a link to the URI, with the URI as text.

### R-MD-0146
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.AngleBracket
- Source: spec
- Citation: CommonMark 0.31.2 "Autolinks", examples 594-612
- Rule: An email autolink becomes a mailto: link with the address as text.

### R-MD-0147
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.AngleBracket
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: Otherwise an HTML tag starting at < becomes raw inline HTML.

### R-MD-0148
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.UriAutolink
- Source: spec
- Citation: CommonMark 0.31.2 "Autolinks", examples 594-612
- Rule: A scheme is 2 to 32 chars starting with a letter; the URI has no control chars, spaces, < or >.

### R-MD-0149
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.EmailAutolink
- Source: spec
- Citation: CommonMark 0.31.2 "Autolinks", examples 594-612
- Rule: An email address follows the HTML5 non-normative pattern: local part, @, dot-separated labels of 1 to 63 chars.

### R-MD-0150
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.RawHtml
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: A processing instruction runs from <? to the next ?>.

### R-MD-0151
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.RawHtml
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: A comment is <!-->, <!---> or <!-- up to the next -->.

### R-MD-0152
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.RawHtml
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: A CDATA section runs from <![CDATA[ to the next ]]>.

### R-MD-0153
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.RawHtml
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: A declaration is <! and a letter up to the next >.

### R-MD-0154
- Location: Markdown/Parsing/InlineParser.Scanning.cs, InlineParser.Escaped
- Source: spec
- Citation: CommonMark 0.31.2 "Backslash escapes", examples 12-24
- Rule: A backslash escapes the ASCII punctuation char after it; an escaped backslash escapes nothing further.

### R-MD-0155
- Location: Markdown/Parsing/InlineParser.Scanning.cs, InlineParser.RawDestination
- Source: general
- Citation: prefix parenthesis depths and a next-lower table answer each bare destination scan in constant time
- Rule: Finds where a bare destination ends without rescanning the rest of the block.

### R-MD-0156
- Location: Markdown/Parsing/InlineParser.Scanning.cs, InlineParser.TitleAt
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A title ends at the next unescaped closing delimiter; a parenthesised title may not contain an unescaped (.

### R-MD-0157
- Location: Markdown/Parsing/InlineParser.Scanning.cs, ForwardFinder
- Source: general
- Citation: remembered forward searches keep repeated failing scans linear
- Rule: A later search starting inside the last searched range reuses its answer.

### R-MD-0158
- Location: Markdown/Parsing/InlineParser.Scanning.cs, WorkBudget
- Source: general
- Citation: MarkupLimits.MaxParserWorkPerChar contract and Arun's decision R5-02
- Rule: Counts inline steps against MaxParserWorkPerChar x (length + 1024) + 1,000,000 and throws past it.

### R-MD-0159
- Location: Markdown/Parsing/MarkdownEngine.cs, InlineTree.MergeAdjacentText
- Source: experiment
- Rule: Neighbouring text nodes are joined into one node spanning from the first to the last.
- Experiment: E-0010
- Rule5: R5-0013

### R-MD-0160
- Location: Markdown/Parsing/MarkdownEngine.cs, InlineTree.CountNodes
- Source: general
- Citation: MarkupLimits.MaxNodes contract and Arun's decision R5-01
- Rule: Every inline node of the resulting AST counts against MaxNodes.

### R-MD-0161
- Location: Markdown/Parsing/MarkdownEngine.cs, InlineTree.FlattenDeepEmphasis
- Source: experiment
- Rule: Emphasis at a depth of MaxNestingDepth or more is replaced by its content.
- Experiment: E-0013
- Rule5: R5-0016

### R-MD-0162
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.AddParagraphLine
- Source: experiment
- Rule: A continued paragraph line enters at its first non-space char, a lazy line where the container markers stopped.
- Experiment: E-0011
- Rule5: R5-0014

### R-MD-0163
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.BuildContent
- Source: spec
- Citation: CommonMark 0.31.2 "Insecure characters"
- Rule: U+0000 is replaced by U+FFFD before inline parsing.

### R-MD-0164
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.Spaces
- Source: experiment
- Citation: CommonMark 0.31.2 "Soft line breaks", examples 648-649
- Rule: Spaces and tabs before a line ending are dropped; the spaces right before it decide hard or soft.
- Experiment: E-0012
- Rule5: R5-0015

### R-MD-0165
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.BuildContent
- Source: experiment
- Rule: Inline content of lines after the first starts at their entry point, so a lazy line keeps its indentation for raw HTML.
- Experiment: E-0011
- Rule5: R5-0014

### R-MD-0166
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.SkipIndentation
- Source: spec
- Citation: CommonMark 0.31.2 "Soft line breaks", examples 648-649; CommonMark 0.31.2 "Paragraphs", examples 219-226
- Rule: Spaces and tabs at the start of the next line are not part of the text.

### R-MD-0167
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Run
- Source: general
- Citation: depth-first walk with an explicit stack of enter and leave steps
- Rule: Renders the tree without recursion.

### R-MD-0168
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Text
- Source: spec
- Citation: CommonMark 0.31.2 "Backslash escapes", examples 12-24
- Rule: Text escapes &, <, > and " for HTML.

### R-MD-0169
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.SourcePos
- Source: experiment
- Rule: data-sourcepos gives one-based line:column of a block's first and last char, after any start attribute.
- Experiment: E-0016
- Rule5: R5-0020

### R-MD-0170
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: Paragraphs in a tight list are rendered without <p> tags.

### R-MD-0171
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "List items", examples 253-300
- Rule: Bullet lists render as <ul>, ordered as <ol>, with start="n" when the start is not 1.

### R-MD-0172
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: <li> is followed by a line ending unless its first block is a tight paragraph.

### R-MD-0173
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "HTML blocks", examples 148-191
- Rule: An HTML block is written as it was read.

### R-MD-0174
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "Soft line breaks", examples 648-649
- Rule: A soft break is a line ending, or a space or a hard break when the options ask for it.

### R-MD-0175
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "Hard line breaks", examples 633-647
- Rule: A hard break is <br /> and a line ending.

### R-MD-0176
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "Code spans", examples 328-349
- Rule: A code span is <code> with escaped text.

### R-MD-0177
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "Emphasis and strong emphasis", examples 350-481
- Rule: Emphasis is <em>, strong emphasis <strong>.

### R-MD-0178
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: spec
- Citation: CommonMark 0.31.2 "Links", examples 482-571
- Rule: A link is <a href> with an optional title attribute.

### R-MD-0179
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.CodeBlock
- Source: spec
- Citation: CommonMark 0.31.2 "Fenced code blocks", examples 119-147
- Rule: Code blocks are <pre><code>; a fenced block's first info word adds class="language-word".

### R-MD-0180
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.ImageTag
- Source: spec
- Citation: CommonMark 0.31.2 "Images", examples 572-593
- Rule: An image is <img> with src, alt from its description, and an optional title.

### R-MD-0181
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Url
- Source: experiment
- Rule: URLs keep ASCII letters, digits, -_.!~*'();/?:@&=+$,# and valid %XX; everything else is percent-encoded as UTF-8.
- Experiment: E-0015
- Rule5: R5-0018

### R-MD-0182
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.RawHtml
- Source: spec
- Citation: CommonMark 0.31.2 "Raw HTML", examples 613-632
- Rule: In passthrough mode raw HTML is written unchanged.

### R-MD-0183
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.PlainText
- Source: experiment
- Rule: Alt text is the description's plain text: text, code and raw HTML literally, line breaks as spaces.
- Experiment: E-0014
- Rule5: R5-0017

### R-MD-0184
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.SplitRow
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", examples 198-205
- Rule: A table row splits into cells at unescaped pipes; a leading and a trailing pipe are dropped and each cell loses spaces and tabs at both ends.

### R-MD-0185
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.DelimiterRow
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", examples 198-205
- Rule: A delimiter row holds only cells of hyphens with an optional colon at either end, which set left, right or center alignment.

### R-MD-0186
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.TryTable
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", example 203
- Rule: The header row must have exactly as many cells as the delimiter row, otherwise there is no table.

### R-MD-0187
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.TryTable
- Source: general
- Citation: MarkupLimits.MaxTableColumns and MaxTableCells contract
- Rule: A table with more than MaxTableColumns columns, or whose cells would pass MaxTableCells over the document, is not made.

### R-MD-0188
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.TryTable
- Source: experiment
- Rule: When the header row is the last line of a longer paragraph, the paragraph keeps the earlier lines and ends where the header line starts.
- Experiment: E-0018
- Rule5: R5-0021

### R-MD-0189
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.TryAddBodyRow
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", examples 201-202
- Rule: Each further non-blank line that starts no other block becomes a body row.

### R-MD-0190
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.AddRow
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", example 204
- Rule: A row keeps as many cells as the header: extra cells are ignored and missing cells are empty.

### R-MD-0191
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.CellContent
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", example 200
- Rule: In a cell an escaped pipe is a literal pipe, also inside a code span.

### R-MD-0192
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: experiment
- Rule: A table is tried after setext headings, on a paragraph line starting with |, : or -; the delimiter row needs no pipe.
- Experiment: E-0018
- Rule5: R5-0021

### R-MD-0193
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", examples 201-202
- Rule: A table continues on every line that is not blank.

### R-MD-0194
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.TryTable
- Source: experiment
- Rule: A table spans from its header row and is extended like other blocks to the end of a line that displaces it.
- Experiment: E-0018
- Rule5: R5-0021

### R-MD-0195
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.AddRow
- Source: experiment
- Rule: A cell spans from its first char for the length of its text with escaped pipes unescaped; a missing cell has an empty span at the row end.
- Experiment: E-0018
- Rule5: R5-0021

### R-MD-0224
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.CellContent
- Source: experiment
- Rule: Positions inside a cell are counted on its unescaped text from the cell start.
- Experiment: E-0018
- Rule5: R5-0021

### R-MD-0196
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TakeTaskMarker
- Source: experiment
- Rule: The first paragraph of a list item that starts with [ ], [x] or [X] and a space or tab is a task item; the paragraph then starts after the marker and its spaces or tabs.
- Experiment: E-0019
- Rule5: R5-0022

### R-MD-0225
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.TaskMarker
- Source: experiment
- Rule: The task marker is written at the start of the first paragraph: a disabled checkbox, a glyph span or [x]/[ ] text, as the option asks.
- Experiment: E-0019
- Rule5: R5-0022
- Status: superseded by R-MD-0257

### R-MD-0197
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ParseInto
- Source: spec
- Citation: GFM 0.29 "Strikethrough (extension)", examples 491-492
- Rule: With strikethrough on, a run of tildes is a delimiter run.

### R-MD-0198
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.DelimiterRunAt
- Source: experiment
- Rule: A run of one or two tildes opens when left-flanking and closes when right-flanking, as * does; a longer run is text.
- Experiment: E-0020
- Rule5: R5-0023

### R-MD-0199
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: experiment
- Rule: A strikethrough pairs runs of equal length and uses the whole run; DelimiterLength is that length.
- Experiment: E-0020
- Rule5: R5-0023

### R-MD-0229
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.ProcessEmphasis
- Source: experiment
- Rule: A closing-only tilde run that reaches an opening-only tilde run of another length discards it and every run between them.
- Experiment: E-0020
- Rule5: R5-0023

### R-MD-0213
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.DelimiterRunAt
- Source: experiment
- Rule: A delimiter run that can neither open nor close is plain text at once, so it joins the text around it.
- Experiment: E-0021
- Rule5: R5-0024
- Status: superseded by R-MD-0246

### R-MD-0200
- Location: Markdown/Parsing/InlineParser.Autolinks.cs, InlineParser.IsAutolinkTrigger
- Source: experiment
- Rule: www. may start a link at the start of the content or after space, tab, line ending, form feed, *, _, ~ or (.
- Experiment: E-0022
- Rule5: R5-0025
- Status: superseded by R-MD-0245

### R-MD-0201
- Location: Markdown/Parsing/InlineParser.Autolinks.cs, InlineParser.IsAutolinkTrigger
- Source: experiment
- Rule: http://, https:// and ftp:// may start a link anywhere not after an ASCII letter or digit.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0203
- Location: Markdown/Parsing/InlineParser.Autolinks.cs, InlineParser.TryExtendedAutolink
- Source: experiment
- Rule: www and scheme links are found on the raw content, so references stay as written in their text; none is made while a [ or ![ is open.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0204
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.SchemeLength
- Source: spec
- Citation: GFM 0.29 "Autolinks (extension)", examples 621-631
- Rule: The schemes are http://, https:// and ftp://.
- Status: superseded by R-MD-0249

### R-MD-0205
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.IsDomainChar
- Source: experiment
- Rule: A domain char is any char that is not whitespace, a control char or ASCII punctuation other than - _ and .
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0206
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.ValidDomain
- Source: spec
- Citation: GFM 0.29 "Autolinks (extension)", examples 621-631
- Rule: A valid domain has no underscore in its last two segments; www links also need a period.

### R-MD-0222
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.WwwLink
- Source: experiment
- Rule: The domain is checked on the whole run of domain chars, before trailing punctuation is trimmed.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0207
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.PathEnd
- Source: experiment
- Rule: A link runs to the next space, tab, line ending, form feed or <.
- Experiment: E-0022
- Rule5: R5-0025

### R-MD-0209
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.IsLocalChar
- Source: spec
- Citation: GFM 0.29 "Autolinks (extension)", examples 621-631
- Rule: The local part of an address is ASCII letters, digits, ., -, _ and +.

### R-MD-0210
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.EmailDomain
- Source: spec
- Citation: GFM 0.29 "Autolinks (extension)", examples 621-631
- Rule: An address domain is letters, digits, - and _ in period-separated segments, with at least one period and not ending in - or _.
- Status: superseded by R-MD-0253

### R-MD-0211
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.Trim
- Source: spec
- Citation: GFM 0.29 "Autolinks (extension)", examples 621-631
- Rule: Trailing ? ! . , : * _ ~, unmatched trailing ) and an entity-like &name; ending are trimmed, repeatedly.

### R-MD-0212
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.Trim
- Source: experiment
- Rule: A trailing ; that does not end an entity-like suffix is trimmed too.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0221
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.Trim
- Source: experiment
- Rule: Trailing double and single quotes are trimmed like the other trailing punctuation.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0202
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Apply
- Source: experiment
- Rule: Email addresses are found after inline parsing in the merged, decoded text nodes outside links (images included).
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0216
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Split
- Source: experiment
- Rule: An address after mailto: or xmpp: (not after an ASCII letter or digit) includes the prefix and keeps its text as destination; xmpp: may add a /resource.
- Experiment: E-0021
- Rule5: R5-0024
- Status: superseded by R-MD-0254

### R-MD-0217
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Split
- Source: experiment
- Rule: An email link and the text before it are placed by their offset in the decoded text from the node start.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0218
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.ExtendBack
- Source: experiment
- Rule: A prefixed address also takes in local-part chars written before the prefix.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0219
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Split
- Source: experiment
- Rule: An address directly followed by @ is not a link.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0220
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Split
- Source: experiment
- Rule: An address whose domain ends in a digit is not a link.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0223
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Split
- Source: experiment
- Rule: The text after the last email link spans from the link end to the end of the original node.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0230
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Split
- Source: experiment
- Rule: An address right after the @ of a rejected mailto: or xmpp: address keeps its text as destination, without mailto:.
- Experiment: E-0021
- Rule5: R5-0024

### R-MD-0226
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.TableEnter
- Source: spec
- Citation: GFM 0.29 "Tables (extension)", examples 198-205
- Rule: A table is written with the header row in thead and th cells, body rows in tbody with td cells, and the alignment on each cell.

### R-MD-0227
- Location: Markdown/MarkdownHtmlRenderer.cs, TagFilter.Write
- Source: spec
- Citation: GFM 0.29 "Disallowed Raw HTML (extension)", example 652
- Rule: With the tag filter, the < of title, textarea, style, xmp, iframe, noembed, noframes, script and plaintext tags is written as &lt;.

### R-MD-0228
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Url
- Source: experiment
- Rule: In a URL attribute & is written as &amp; and an apostrophe as &#x27;.
- Experiment: E-0023
- Rule5: R5-0026

### R-MD-0231
- Location: Markdown/Parsing/MarkdownSyntax.cs, MarkdownSyntax.IsUriAutolinkBody
- Source: spec
- Citation: CommonMark 0.31.2 "Autolinks", examples 594-612
- Rule: A body is a URI autolink when it has a scheme of 2 to 32 chars and then no control chars, spaces, < or >.

### R-MD-0232
- Location: Markdown/Parsing/MarkdownSyntax.cs, MarkdownSyntax.TrimExtendedAutolink
- Source: spec
- Citation: GFM 0.29 "Autolinks (extension)", examples 621-631
- Rule: Extended autolink path validation applied to a candidate in plain text.

### R-MD-0233
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.DelimiterRow
- Source: experiment
- Rule: The end of a delimiter row also loses U+000B and U+000C; header and body rows lose only spaces and tabs.
- Experiment: E-0024
- Rule5: R5-0027

### R-MD-0234
- Location: Markdown/MarkdownHtmlRenderer.cs, UrlSafety.IsDangerous
- Source: general
- Citation: UnsafeUrlPolicy.Remove contract (the XML documentation of the given library)
- Rule: javascript:, vbscript:, file: and data: URLs (scheme in any case) are dangerous, except data:image/png, gif, jpeg and webp; with Remove they are written as an empty URL.

### R-MD-0235
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Enter
- Source: experiment
- Rule: In Escape mode an HTML block is written as a paragraph holding its escaped text, without its final line ending.
- Experiment: E-0025
- Rule5: R5-0028

### R-MD-0236
- Location: Markdown/MarkdownHtmlRenderer.cs, Slugs.Make
- Source: experiment
- Rule: A heading id is its plain text without raw inline HTML, trimmed and lower-cased; letters, digits, - and _ are kept, each space becomes -, everything else is dropped.
- Experiment: E-0026
- Rule5: R5-0029
- Status: superseded by R-MD-0271

### R-MD-0237
- Location: Markdown/MarkdownHtmlRenderer.cs, Slugs.Make
- Source: experiment
- Rule: A slug already used in the same rendering gets -1, -2 and so on, in document order; the prefix comes in front.
- Experiment: E-0026
- Rule5: R5-0029

### R-MD-0238
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.HasRoom
- Source: experiment
- Rule: A list or list item opens only while its first new level stays below MaxNestingDepth, counted from the deeper of its parent and the last block the line continued; block quotes keep R-MD-0076.
- Experiment: E-0027
- Rule5: R5-0030

### R-MD-0239
- Location: Markdown/Parsing/MarkdownEngine.cs, InlineTree.FlattenDeepEmphasis
- Source: experiment
- Rule: Strikethrough at a depth of MaxNestingDepth or more is replaced by its content, like emphasis.
- Experiment: E-0027
- Rule5: R5-0030

### R-MD-0240
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.TaskMarker
- Source: experiment
- Rule: A task item's paragraph rendered on its own gets no task marker; the marker belongs to the item.
- Experiment: E-0028
- Rule5: R5-0031

### R-MD-0241
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.TableLeave
- Source: experiment
- Rule: The line ending after a table row or cell is written by its parent, so a row or cell rendered on its own ends at its closing tag; a cell rendered on its own is a td.
- Experiment: E-0028
- Rule5: R5-0031

### R-MD-0242
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.CloseBelow
- Source: general
- Citation: walking up once from the deepest open block closes a chain of n blocks in n steps
- Rule: Open blocks are closed deepest first by one upward walk, not by searching for the deepest block again each time.

### R-MD-0243
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.IsThematicBreak
- Source: general
- Citation: a remembered failing scan keeps repeated scans of one line linear
- Rule: A thematic break scan that failed at a char fails again from any later start before it with the same marker char, without rescanning.

### R-MD-0244
- Location: Markdown/Parsing/LineCursor.cs, LineCursor.MeasureSpaces
- Source: general
- Citation: a remembered measurement of a run of spaces and tabs, valid for every position inside the run
- Rule: The end and end column of a run of spaces and tabs are measured once and reused by every open block that asks about it.

### R-MD-0245
- Location: Markdown/Parsing/InlineParser.Autolinks.cs, InlineParser.IsAutolinkTrigger
- Source: spec
- Citation: GFM 0.29 "Autolinks (extension)", examples 621-631; GFM 0.29 "Characters and lines"
- Rule: www. may start a link at the start of the content or after a whitespace character (space, tab, line feed, U+000B, U+000C, carriage return), *, _, ~ or (; decided by Arun in D-157.

### R-MD-0246
- Location: Markdown/Parsing/InlineParser.cs, InlineParser.DelimiterRunAt
- Source: general
- Citation: a run that can neither open nor close never takes part in emphasis, so it is kept as plain text instead of a delimiter entry (E-0021: no output changes either way)
- Rule: A delimiter run that can neither open nor close is added as plain text and not to the delimiter list.

### R-MD-0247
- Location: Markdown/Parsing/CharacterReferences.cs, CharacterReferences.DecodeReferences
- Source: spec
- Citation: CommonMark 0.31.2 "Entity and numeric character references", examples 25-41
- Rule: Inside a URI autolink, entity and numeric character references are decoded in its text and its destination ("<tp:&mp;>" links "tp:∓"; D-232, G1).

### R-MD-0248
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.Trim
- Source: experiment
- Rule: When an extended link is trimmed, an ending is entity-like only when "&" is followed by an ASCII letter, then letters or digits, then ";"; "&0;" is not, so only its ";" is trimmed ("http://a&0;" links "http://a&0"; D-232, N4). This refines R-MD-0211.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0249
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.SchemeLength
- Source: experiment
- Rule: The schemes http://, https:// and ftp:// are matched in any case of their letters ("HTTP://a" links; D-232, N1); www. stays lower case.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0250
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.IsDomainChar
- Source: experiment
- Rule: A space separator (U+00A0 and the other Unicode Zs chars) is not a domain char ("www.\u00A0" is text); U+2028 still is, as the frozen output shows ("http://\u2028&mp;x" links). This refines R-MD-0205.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0251
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.ValidDomain
- Source: experiment
- Rule: A domain must start right after www. or the scheme with a domain char that is not ASCII punctuation other than "_" and "-" ("ftp://.f", "www.&" and "www.\\_x" are text; "www._xxx.yyy.zzz" and "http://-" link); for the underscore check a domain ends before a period that follows another ("_..x1c.d" is checked as "_"). D-232 G2, D-242. This refines R-MD-0206.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0252
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.DomainEnd
- Source: experiment
- Rule: A backslash before "-", "_" or "." that follows a letter, digit or period counts as part of the domain with the char it escapes ("www.a.b\\_" is text; D-232, G3), but not after other chars ("http://-\\_" has the domain "-" and links "http://-\\"; D-242).
- Experiment: E-0030
- Rule5: R5-0034

### R-MD-0253
- Location: Markdown/Parsing/ExtendedAutolinks.cs, ExtendedAutolinks.ScanEmailDomain
- Source: experiment
- Rule: An email domain is labels of letters, digits, "-" and "_" joined by single periods; it may not start with a period, ends before a period that does not start another label ("a.b@c.dwww..com" links "a.b@c.dwww"), needs a period and may not end in "-" or "_" (D-232, N6).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0254
- Location: Markdown/Parsing/EmailAutolinks.cs, EmailAutolinks.Split
- Source: experiment
- Rule: An address search keeps a floor: the end of the last link, or the end of the domain scan of the last address that had a local part or a prefix, whose domain was invalid, and whose scan did not stop at "@". Local parts, prefixes and the chars before a prefix stay at or after it ("e.com@foo+bar@ex.co" links "+bar@ex.co"). Prefixes may repeat ("xmpp:+mailto:x@y.z" links from "xmpp:"), and after a prefix the local part may be empty ("mailto:@a.b_c"). D-232, N6.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0255
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.Url
- Source: experiment
- Rule: A "%" in a URL is always kept, followed by two hex digits or not ("%" stays "%", not "%25"; D-232, N5). This refines R-MD-0168.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0256
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.FinishParagraph
- Source: experiment
- Rule: A task item whose marker is all its paragraph holds keeps its task state and loses the empty paragraph ("- [ ] " is a task item with no content, as the frozen diff-core and diff-full outputs show; D-232, G6).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0257
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.TaskMarkerFor
- Source: experiment
- Rule: The task marker is a disabled checkbox, a glyph span or [x]/[ ] text, as the option asks. It is written at the start of the first paragraph in a tight list, and right after <li> (before the line ending and the <p>) in a loose list or when the item has no content (D-232, G6).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0258
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.TakeTaskMarker
- Source: experiment
- Rule: A tab inside the brackets with nothing after the marker is plain text ("- [\t] "; D-232, G6). This refines R-MD-0196.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0259
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.DelimiterRow
- Source: experiment
- Rule: In a delimiter row U+000B and U+000C are whitespace at the start of, inside and at the end of each cell ("-\f|" is a delimiter row); header and body cells keep them (D-232, G5 and N8). This extends R-MD-0233.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0260
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.StartsListItemWithContent
- Source: experiment
- Rule: A line of "-", a space or tab, and more content starts a list item, not a delimiter row ("a\n- |" is a paragraph and a list; D-232, G4).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0261
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.Continue
- Source: experiment
- Rule: A body row whose cells are all empty (pipes and whitespace only) ends the table and becomes paragraph text (D-232, N9). This refines R-MD-0193.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0262
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.Trimmed
- Source: experiment
- Rule: A table cell that follows a pipe also loses U+000B and U+000C at its start; a row's first cell without a leading pipe keeps them, as a paragraph line does; every cell keeps them at its end (D-232, G5). This refines R-MD-0184.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0263
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.TryTable
- Source: experiment
- Rule: A header row whose cells are all empty is no header ("|\n-:" is a paragraph; D-232, N7). Once a delimiter row was refused in a paragraph for its cell count, no later line of that paragraph starts a table (D-232, G4).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0264
- Location: Markdown/Parsing/BlockTables.cs, BlockParser.TryTable
- Source: experiment
- Rule: A delimiter row ending in U+000B or U+000C counts as a blank line for the looseness of the lists around the table ("- o\n  -\v\n-" is a loose list). Fitted to dc-1337, the only frozen case.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0265
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: experiment
- Rule: A block opened on a line with nothing after its marker still extends the blocks around it to the end of that line ("*\n\t>" gives the item the span 0+4; D-232, C1).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0266
- Location: Markdown/Parsing/BlockStarts.cs, BlockParser.AddParagraphLine
- Source: experiment
- Rule: A lazy line that starts inside a partly used tab has its text positions moved on by the tab's unused columns, and its end becomes its content end (trailing spaces and tabs left out) plus those columns; its soft break ends at its first non-space char ("- >o\n\t[" puts "[" at 8 and ends the blocks at 9, past the input; D-232, C1 as frozen).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0267
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: experiment
- Rule: A lazy line extends the blocks the line did continue (and a list keeps that end even when its item does not take the line in) at once, but the unmatched blocks around its paragraph only when the paragraph stays, i.e. is not all link reference definitions (">\t[foo]:\n/url" keeps the quote at 0+8; "- [foo]:\n/url" gives the list 0+13 and the item 0+8; D-232, N3). This refines R-MD-0075.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0268
- Location: Markdown/Parsing/InlineParser.Links.cs, InlineParser.CloseBracket
- Source: experiment
- Rule: A full reference's Label is its second bracket's raw text trimmed of spaces, tabs and line endings ("[\ta][\ta]" gives "a", df-0106); collapsed and shortcut references keep the raw text ("[\ta]" gives "\ta", lab-0027). This refines R5-0019.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0269
- Location: Markdown/Parsing/LinkReferenceReader.cs, LinkReferenceReader.Read
- Source: experiment
- Rule: A definition's span runs to where the next line's content enters (its first non-space char, or for a lazy line where the container markers stopped), and a lazy line that becomes a paragraph's first line after definitions keeps that entry point (">[foo]:\n/url\r\n   a" gives the definition 1+13 and the text "   a"; D-232, N3). This refines R5-0007.
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0270
- Location: Markdown/Parsing/BlockParser.cs, BlockParser.ProcessLine
- Source: spec
- Citation: CommonMark 0.31.2 "Lists", examples 301-326
- Rule: A blank line inside an open fenced code block is part of the code and does not make a list loose ("- ~~~\n\n-" is tight; D-232, C2).

### R-MD-0271
- Location: Markdown/MarkdownHtmlRenderer.cs, Slugs.Make
- Source: experiment
- Rule: A heading id is its plain text without raw inline HTML, trimmed and lower-cased: letters, digits, combining marks (Mn, Mc, Me), "-" and "_" stay, a space or a soft break becomes "-", a hard break gives nothing, and everything else (punctuation, symbols, emoji, tabs, U+00A0) is dropped; a repeat gets -1, -2 (D-237, GitHub's heading-id rules).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0272
- Location: Markdown/MarkdownHtmlRenderer.cs, HtmlWriter.SourcePos
- Source: experiment
- Rule: A span that runs past the input (R-MD-0266) is placed at the input's end in data-sourcepos ("- >o\n\t[" gives 1:1-2:3).
- Experiment: E-0029
- Rule5: R5-0033

### R-MD-0273
- Location: Markdown/Parsing/InlineParser.Scanning.cs, WorkBudget.Step
- Source: general
- Citation: cancellation contract (BRIEF section 1, "cancellation honoured in long loops"; checked by gate time from patch 9)
- Rule: Every inline work step also polls the cancellation token (once per 4,096 steps), so the inline parsing of one long block, and the email pass that uses the same budget, stop promptly when the token is cancelled.
