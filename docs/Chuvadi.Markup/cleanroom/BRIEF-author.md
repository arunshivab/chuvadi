# Brief for the clean-room author: Chuvadi.Markup Markdown parser and HTML renderer (D-106 step 2)

You are writing, from the public specifications only, the Markdown parser and the Markdown-to-HTML renderer
of Chuvadi.Markup, a pure-.NET (BCL only) markup library. Your code replaces code that was derived from
another implementation. The goal is a parser and renderer that are **our own work**, provably written from
the specifications, with **byte-identical public output** to today's library.

Everything you need is in this bundle. Nothing outside it is needed or allowed (see "Firewall").

## 1. What you write, and what you must not touch

You write:
- every file under `src/Chuvadi.Markup/Markdown/Parsing/` (today two stubs: `MarkdownEngine.cs`,
  `MarkdownSyntax.cs`; add as many files there as you like);
- `src/Chuvadi.Markup/Markdown/MarkdownHtmlRenderer.cs` (today a stub);
- your own tests, if you want them, in `tests/Chuvadi.Markup.CleanRoom.Tests/` (xunit 2.9.3,
  xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1; the library already grants it internals);
- the trace files under `trace/`.

Everything else under `src/` is the given library (AST types, options, limits, the Markdown writer, the HTML,
CSS, sanitizer and plain-text code). You may read it, use it and call its internal helpers, but **do not change
it**. It is the contract. In particular:
- the public AST (`MarkdownNode`, `Blocks.cs`, `Inlines.cs`), `MarkdownOptions`, `MarkdownHtmlOptions`,
  `MarkupLimits`, `SourceSpan`, and their XML documentation;
- `MarkdownParser.cs`, which calls your `MarkdownEngine.Parse` (read its comment for the exact contract);
- `MarkdownWriter.cs` and `Text/PlainText.cs`, which call your `MarkdownSyntax` checks;
- the public signatures and documentation in `MarkdownHtmlRenderer.cs`.

`gate api` fails if the public API changes in any way.

Language and dependencies: C# for .NET 10, BCL only (no NuGet package in `src/`), no P/Invoke, no native code,
no `unsafe`. The project builds with `TreatWarningsAsErrors` and the globalization analyzers on: use ordinal
string operations and invariant culture. The library is for untrusted input: no recursion that input can deepen
(use explicit stacks), linear time apart from bounded rescans, cancellation honoured in long loops, limits
enforced (see `MarkupLimits`).

## 2. Firewall (binding)

1. **Specifications only.** Your sources are the files in `specs/`: the CommonMark Spec 0.31.2, the GitHub
   Flavored Markdown Spec 0.29 and the GFM extensions test file. The CommonMark spec's appendix "A parsing
   strategy" is part of the spec and may be used and cited.
2. **Never open, search for, or deliberately recall** the source code, documentation of internals, issue
   threads or blog posts about internals of any Markdown implementation: cmark, cmark-gfm, commonmark.js,
   commonmark-java, markdown-it, Markdig, md4c, pulldown-cmark, micromark and all others. This includes
   reading them "just to check". If you notice you are recalling the code of a particular implementation,
   stop, and write that part from the spec in your own structure and your own names.
3. **Never look at the derived code.** It is not in this bundle; do not ask for it, for its file or type names,
   or for a description of how it works.
4. **The frozen expected outputs are your oracle.** Where the spec is silent, find the behaviour by experiment
   against the gate (black box): change your code, run the gate, compare. Record each such experiment and list
   the case as a rule-5 case (section 4). Never ask anyone how the old code or any other implementation behaves.
5. **Log every source** you read in `trace/sources.md` before you read it. Web access is not needed; if you
   think you need a page, ask the coordinator first.
6. **Identifiers** come from the spec's own terms (block quote, list item, link reference definition,
   delimiter run, left-flanking, container block, lazy continuation line, ...) and ordinary C# naming.
7. Similarity checking is done by a separate checker who compares your code with other implementations.
   Its findings reach you only as **your own file and line plus a category** (name, structure, logic,
   constant, comment). You then rewrite that part from the spec with a new trace entry. You will never be
   told what it resembled.

## 3. Byte-identical: what "the same output" means (D-127)

All public output must be identical to the frozen expected outputs:
- the HTML of every renderer configuration;
- the AST: every node's kind, source span (`Span`) and every public property, and the link reference
  definitions;
- `data-sourcepos` attributes (`MarkdownHtmlOptions.SourcePositions`);
- `MarkdownWriter` output (the writer is given; it depends on your AST and your `MarkdownSyntax.IsUriAutolinkBody`);
- `PlainText.ToHtml` output (given code; it depends on your `MarkdownSyntax.TrimExtendedAutolink`);
- the limit and degradation behaviour: which exception (type, `LimitName`, `Limit`, `Actual`, message, except as
  section 3 below says) and,
  where a limit degrades instead of throwing, the exact degraded result;
- argument checks, cancellation and the other contract probes.

Your rule-5 cases R5-0001 and R5-0002 are decided as option B, follow the spec (D-141): U+000B, U+000C,
U+00A0 and U+2028 are not blank (a blank line or blank paragraph contains only spaces, tabs and line endings),
so a paragraph made only of them is kept, and an ATX heading's content is stripped of spaces and tabs only, so a
leading or trailing U+000B or U+000C stays. 15 cases were refrozen with that output (fuzz-0150, fuzz-0621,
fuzz-0928, fuzz-1017, fuzz-1631, fuzz-1711, fuzz-1747, fuzz-1867, fuzz-1932, fuzz-2018, fuzz-2164, fuzz-2427,
fuzz-2556, fuzz-2661, fuzz-3909).

R5-0003 is decided B as well (D-143): paragraph and setext heading content is stripped of spaces and tabs only,
so a leading or trailing U+000C stays. 46 more fuzz cases were refrozen with that output (list in the
coordinator's record). All these cases are compared exactly.

Decided in D-157:
- R5-0025 is option C, GFM 0.29 literally: U+000B and U+000C count as whitespace before an extended www.
  autolink, so a link starts after either. 10 fuzz cases were refrozen with that output (fuzz-0020, fuzz-0187,
  fuzz-0469, fuzz-0907, fuzz-1425, fuzz-1708, fuzz-2100, fuzz-2610, fuzz-3542, fuzz-3748).
- R5-0027 stands as you wrote it: U+000B and U+000C at the end of a delimiter row are ignored, and kept at the end
  of header and body rows. The frozen output already does this.
- R5-0004: follow the spec. A link label is valid when it contains at least one character that is not a space,
  tab or line ending, so a label made only of U+00A0 is valid. Matching goes through the given
  LinkReferenceDefinition.NormalizeLabel, which you must use and must not change.

Decided in D-174: NormalizeLabel follows the spec. It trims and collapses only space, tab, LF and CR (U+0020,
U+0009, U+000A, U+000D); U+000B, U+000C and U+00A0 stay in the label as they are. The given Markdown/Blocks.cs
already has this change. The 41 cases of the new "labels" set (lab-0001 to lab-0041) and the probe
normalize-label test it; trim the raw label you pass to it with the same four characters only.

Decided in D-193: R5-0030 is confirmed as you built it. A list item may sit exactly at MaxNestingDepth, except where
it would interrupt a paragraph at that depth; block quotes stay strictly below the limit. The given `MarkupLimits.cs`
now documents this, together with the cost of very deep block quotes (D-199).

Decided in D-199: your spec-silent cases R5-0003 to R5-0024, R5-0026, R5-0028, R5-0029 and R5-0031 are accepted as
built. (Correction: patch 6 said "R5-0003 and R5-0004 as decided in D-143 and D-157"; that mixed up two numberings.
Your R5-0003 and R5-0004 are your own spec-silent span cases, accepted here; the coordinator's early numbers are
mapped in the table at the top of your rule5-cases.md.) Three cases were added as the new set "urls" (url-0001 to
url-0003): links, images, an autolink and a reference definition whose destination is a `data:image/png`, `gif`,
`jpeg` or `webp` URL (kept by the unsafe-URL rule, whatever the case of the scheme), and other `data:` URLs (removed
where unsafe URLs are removed). Their expected output comes from the given library.

Decided in D-232 and D-237 (patch 7): a comparison outside the frozen corpus found inputs where your build and the
frozen behaviour differ. Arun ruled on every group, and three new sets pin the result: "diff-core" (2,086 minimal
inputs, dc-0001 to dc-2086), "diff-full" (1,379 longer inputs, df-0001 to df-1379) and "slugs" (26 heading-id
inputs, slug-0001 to slug-0026). Each case's meta "group" names its group below. The expected output follows these
rules (where a rule says "as frozen" the output is the frozen library's; where it says "spec" it was derived from
the spec text):
- G1, N4 (as frozen; the spec recognizes entity references "in any context besides code spans or code blocks,
  including URLs"): entity and numeric character references inside `<...>` autolinks and inside extended www. and
  http(s)/ftp links are decoded, in the link text and the URL: `<tp:&mp;>` links to `tp:∓`.
- G2 (spec, GFM valid domain): an extended link needs a domain right after `www.` or the scheme; punctuation or
  whitespace there (U+000B, U+000C, U+00A0, U+2028 included) means no link (`www.&`, `www.\u00A0`, `http://\u000B`);
  `_` in the last two segments of the domain, checked to its last character, means no link (`http://a_`, `www.a_`);
  periods at the very end of the domain do not start a segment. D-242 (patch 8): a domain may not start with a
  period (`www..a`, `http://.c` and `www..` are text); "at least one period" does not apply to http://, https://
  and ftp:// links, so single-label hosts link (`http://intranet/path`, `http://localhost:8080/x`) and so does a
  host of only hyphens (`http://-`, `HTTP://-`); for www. links the period after www counts. The set "diff-domain"
  (1,336 inputs, dd-0001 to dd-1336) pins these.
- G3 (as frozen): backslash escapes inside or next to an extended link (`http://1\_`, `www.c\_`) keep the
  frozen behaviour, so the writer's escaped output reads back the same.
- G4, N12 (as frozen): a paragraph line followed by `- |` (also nested, or with U+000B or U+000C after the dash)
  stays a paragraph and a list item; a line that failed as a table header does not become the header of a table
  started by the next line.
- G5, N8 (R5-0027 and D-157, GFM whitespace): U+000B and U+000C at the start of, inside or at the end of a
  delimiter row are whitespace (the row is a delimiter row; alignment reads the colons); they are kept at the end
  of header and body cells (a header cell of only U+000C keeps it).
- G6 (as frozen): a task marker with nothing after it (`- [ ] `, `1. [x] `, `- [\t] `) is plain text.
- N1 (as frozen): upper-case schemes (`HTTP://a`) link.
- N2 (spec): no extended http(s)/ftp link straight after an ASCII letter or digit (`0http://a`, `xhttp://a` stay
  text); after any other character, a link as before (`<http://a.b c>`, `"http://a.b"`, `éhttp://a.b`).
- N3 (as decided: never past the input): a link reference definition's span covers its whole line ending (CR LF
  included) and ends at the end of the input when there is no line ending (`[f]:r` gives span 0+5).
- N5 (as frozen, also the checker's F7): a `%` not followed by two hex digits stays `%` in the URL, not `%25`.
- N6 (spec, GFM extended email): no link when the domain starts with a period (`b@.c`); a label may start with `-`
  or `_` (`a@b._x` links); an empty xmpp resource is not part of the link (`xmpp:a@b.f/` links `xmpp:a@b.f`).
- N7, N9 (as frozen): a header row of only `|` is not a table header; a body row of only `|` ends the table.
- N10 and slugs (D-237, GitHub's heading-id rules; S-P4-001): keep letters, digits, combining marks (vowel signs,
  viramas, other marks), `-` and `_`; a space becomes `-`; everything else is removed, including punctuation,
  symbols, emoji, tabs, U+00A0 and line breaks (`a  \nd` gives `ad`). Duplicates get `-1`, `-2`.
- N11 (spec): a task marker may hold any whitespace character (`- [\t] x` is a task item); see G6 for empty items.
- C1 (as frozen, the R5-0003 rule): an empty list item whose content starts on the next line spans to the end of
  that content (`-\n  >` gives the item span 0+5).
- C2 (spec): a blank line inside an unclosed fence does not loosen a list (`- ~~~\n\n-` is tight); a blank line
  between an item that ends in a thematic break and the next item does (`0. ___\n\n0.` is loose).
- C3 (spec): a backtick string that never closes does not stop later code spans (``` ``;`>`-`>` ``` has two).
- C4 (spec, as D-141): U+000B and U+000C are not whitespace inside an HTML tag (`<r\u000B>` is text).
None of the 5,561 earlier cases changed, and patch 8 changed none of the patch-7 cases.

D-198: `gate trace` now accepts `- Status: superseded by R-MD-nnnn` (the named entry must exist); the bare word
`superseded` still passes, with a warning. Please change your superseded entries to that form.

Patch 9 (cancellation responsiveness): the contract has always been "cancellation honoured in long loops"
(section 1). `gate time` now checks it. Each of 12 input shapes is parsed (or, for the last one, rendered) with a
token that is cancelled after 20, 120 and 300 ms. Every run must end within 0.75 s of the cancel, normally with
`OperationCanceledException`. A run that ends within that time without seeing the cancel also passes, because
the input was too fast to tell. Revision 6 fails the shapes for emphasis delimiters (`*a_ ` x 500k), open
brackets (`[` x 3M), code spans (`` `a`` x 3M), extended autolinks (`www.a ` x 1M), and sometimes raw HTML
(`<a ` x 6M) and many short lines (`a\n` x 1M). These loops run long without checking the token, or they check it
only once the loop is done. The shapes and limits are in `GateCancel` (gate/Chuvadi.Markdown.Gate/GateCore.cs).
Output must not change: patch 9 changes no frozen data, and every other check must still pass. The same check runs
as a unit test in the monorepo.

Patch 10 (revision 8: D-294, D-303, D-309). One round with five changes; one similarity check follows.
- Gate data: 10,423 cases and 353,669 outputs. 9 existing cases change (157 outputs) and a new set `pins` adds 35
  cases (1,190 outputs). Revision 7 fails 280 outputs in 18 cases, plus the new span check below. Every other output
  is unchanged.
- **1. Positions after a partly used tab (D-309).** Spans are UTF-16 indexes into the input, and a tab is one
  character. A continuation line may start with a tab that container indentation (a list item's content offset,
  a block quote) uses only partly. The columns of that tab left over for the content take no width in the source,
  and the content after the tab keeps its true position. The soft break before the line covers the line ending and
  the tab, as it covers indentation spaces. Example, ` - - ~` LF TAB `|`:
  - the `|` Text node is 8+1 (revision 7: 9+1);
  - its paragraph is 5+4, the soft break 6+2, and the inner and outer lists 3+6 and 1+8.

  Cases: dc-1308, dc-1385, df-0452, df-0475, df-0545, df-0610, df-0635, df-0692, df-0766, and pins pin-0001 to
  pin-0013 (pin-0011 to pin-0013 are controls that already pass).
- **2. Span safety net (D-309).** After a parse, every span lies inside the input: Start >= 0 and
  Start + Length <= the input length. This covers every node and every link reference definition. A span that
  would not is clamped to the input. Each clamp adds 1 to
  `[ThreadStatic] internal static int SpanClampsOnThisThread` on `Chuvadi.Markup.Markdown.Parsing.MarkdownEngine`.
  The clamp is only a safety net: once change 1 is right it never fires.
  - In P4, `gate run` checks that the field exists, is a thread-static int, and stays 0 for every frozen input
    under every parse and limit configuration.
  - It also checks every span of the tree directly.
  - It reports "Spans (D-309): passed".
- **3. Unsafe URLs, `data:image/` (D-303).** The exception in the unsafe-URL rule is `data:image/png`,
  `data:image/gif`, `data:image/jpeg` and `data:image/webp`, compared in ASCII case-insensitively, and only when
  the next character is `;` or `,`. Every other `data:` URL is unsafe. So `data:image/pngx;...`,
  `data:image/png-evil,...` and a bare `data:image/png` are now removed under the default options. Cases: pin-0029
  to pin-0035.
- **4. The renderer's summary comment (D-303).** The class summary of `MarkdownHtmlRenderer` says the output
  "matches the CommonMark and GFM spec examples byte for byte". That is not exact: GFM is 651 of 672. Say what holds:
  - CommonMark 0.31.2 examples 652 of 652;
  - GFM 0.29 examples 651 of 672 (the differing examples are listed in `gate/data/baseline-scores.txt`);
  - the GFM extension tests 26 of 26, with footnotes excluded.
- **5. Memory during a parse (D-303, issue #94; a goal, not a gate).** `gate mem` reports, for 512 KiB, 1 MiB and
  2 MiB documents built from the spec text:
  - throughput;
  - bytes allocated per char;
  - gen2 collections per round;
  - the most data alive at a collection.

  On the reference machine, revision 7 shows 1 gen2 collection per round at 1 MiB and 66.5 MiB alive there (1.0.0:
  0 and 9.2 MiB). That costs about half the throughput at that size. The goal is no gen2 collection per round at 1 MiB
  and at most about 10 MiB alive, with every output unchanged and no other size worse. Record the before and after
  figures from your machine in an experiment record. If the goal cannot be met without risk, say so in the note
  rather than forcing it.
- **No change needed (D-252):** pins pin-0014 to pin-0028 freeze the nesting behaviour that Arun accepted. A block
  quote one level below `MaxNestingDepth` may interrupt a paragraph. Revision 7 already passes them.
- **Gate program (D-294):** the cancellation check in `gate time` now measures from the moment the token actually
  fires, still with the 0.75 s limit.
- **Trace:** each of changes 1 to 5 gets its trace entries, citing the ruling (D-303, D-309) and, for change 1, the
  CommonMark "Tabs" section. Deliver as revision 8, in the same form as before.

Patch 11 (revision 9: D-331). One small change; one similarity check follows.
- **Given library, `MarkdownNode` (D-330).** A child list of more than 8,192 entries is now stored in chunks of
  4,096, so no child array reaches the large object heap. `Children` stays the read-only view, and the public API is
  unchanged. The internal `MutableChildren` still returns a `List<MarkdownNode>`. For a node whose children are
  chunked, it first turns the storage back into a list, and the node then keeps that list. A list handed out by
  `MutableChildren` or given to `SetChildren` stays a list.
- **The rule (D-331).** A pass that only reads the tree reads each node's children through `Children`.
  `MutableChildren` is only for a pass that changes a child list.
  - In P4, `gate run` now also parses a document of 20,000 paragraphs under every parse configuration.
  - It checks that the document's children are still in chunked storage after the parse.
  - It reports "Storage (D-331): passed".
  - Revision 8 fails it: the safety-net walk after the inline phase (R-MD-0275) reads the document's children
    through `MutableChildren`.
- **Expected effect, reported by `gate mem`, which now also measures 4 MiB:**
  - every output unchanged;
  - no gen2 collection per round at 2 MiB or at 4 MiB;
  - at most about 4.2 bytes alive per input char (about 8.3 MiB at 2 MiB, 16.6 MiB at 4 MiB);
  - no size slower than revision 8.

  Measured here with the rule applied:

  | Size | Revision 8 | With the rule |
  |---|---|---|
  | 2 MiB | 1 gen2, 56.8 MiB alive | 0 gen2, 8.3 MiB |
  | 4 MiB | 1 gen2, 65.6 MiB | 0 gen2, 16.6 MiB |
  | 512 KiB, 1 MiB | as before | as before |

  Record your before and after figures in an experiment record.
- **Gate data:** unchanged (10,423 cases, 353,669 outputs). Every other check must still pass.
- **Gate program:** the `gate time` heading now says "within 0.75 s of the token firing", which is what the check
  has measured since patch 10. This is wording only.
- **Trace:** one entry for the rule, citing D-331. Deliver as revision 9, in the same form as before.

Patch 12 (D-344). No author change. The checker confirms the patch and reruns the gate on revision 9.
- **Given library, `MarkdownNode`:** a node now keeps its children in one internal `NodeChildren` object for its
  whole life, from its first child onwards, and `Children` always returns that object.
  - Why: with patch 11, a `Children` reference held while the node passed 8,192 children stopped seeing later changes.
  - `NodeChildren` holds the children in one of three forms: one array up to 8,192 entries; chunks of 4,096 after
    that; or a `List<MarkdownNode>`. The list form is used once `MutableChildren` is asked for or `SetChildren` is
    given a list, and every read then goes through to that list.
  - `MutableChildren` and `SetChildren` behave as before for the parser. The rule from patch 11 is unchanged: a pass
    that only reads the tree uses `Children`.
- **Gate program:** the Storage check (D-331) now reads the internal `IsChunked` property of the object `Children`
  returns, instead of comparing type names.
- **Expected:** revision 9 passes P4 unchanged (353,669 outputs; Spans, Storage, TIME and TRACE passed). `gate mem`
  shows the same figures as with patch 11, within run-to-run noise. Revision 8 still fails Storage.
- **Gate data:** unchanged.

Patch 13 (revision 10: D-368, with the D-249 speed rule; small documents). One small change; one similarity check follows.
- **The rule.** A parse must not allocate a buffer whose size does not depend on the input. Every buffer a parse
  allocates grows with what it holds, so a document of a few dozen characters costs about what it cost in 1.1.0. The
  memory goals of patches 10 and 11 still hold: no buffer reaches the large object heap, and nothing large stays
  alive.
- **Gate:** in P4, `gate run` now also reports "Small (D-368)".
  - It parses the inputs of the commonmark, gfm-spec and gfm-extensions sets (1,354 spec examples, about 26 chars
    each) with default options, after a warm-up.
  - It divides the bytes allocated on the thread by the input characters.
  - It passes at 175 bytes per char or less. The figure is the same on every run for a given build.
- **Measured here:**

  | Build | Bytes per char | Small |
  |---|---|---|
  | revision 7 (1.1.0's parser) | 159.4 | passed |
  | revision 9 | 702.1 | NOT PASSED |

  For revision 9, about 16.5 KB more is allocated per parse, whatever the input's size: a parse of `a` allocates
  about 19,200 bytes, against 2,688 in 1.1.0.
- **Speed rule (D-249):** no input class may be more than 25% slower than 1.0.0. Revision 9 parses the spec examples
  at 0.66 of 1.0.0's speed; with the rule met it should return to about 1.1.0's speed (about 1.35 times 1.0.0).
- **Unchanged:** every other check must still pass, `gate mem` must still show no gen2 at 1, 2 and 4 MiB, and the
  gate data does not change (353,669 outputs).
- **Trace:** one entry for the rule, citing D-368, and an experiment record with your before and after figures.
  Deliver as revision 10.

Three limit details were decided by Arun under rule 5 (D-130, D-134). The documentation in `MarkupLimits.cs` already
says this, and `gate/data/rule5-rules.txt` holds the matching gate rules:
- **R5-01, `MaxNodes`**: count the nodes of the resulting AST. The document node and link reference definitions
  are not counted. Throw `MarkupLimitExceededException` as soon as the count would pass the limit, so `Actual` is
  always the limit plus one. The frozen outputs follow this rule and are compared exactly. Temporary objects your
  parser creates and discards do not count, so keep their number bounded by the input length.
- **R5-02, `MaxParserWorkPerChar`**: the budget for Markdown inline parsing is
  `MaxParserWorkPerChar x (input length + 1024) + 1,000,000` steps; that number is the exception's `Limit` and must
  be exact. What counts as one step (one unit of delimiter or bracket processing) is yours to define. Every input
  that completes in the frozen data must still complete with identical output, and the work-limit probe must
  still throw; only its `Actual` value is not compared.
- **R5-03, `MaxOutputChars`**: exact. Output stops at exactly the limit, a `TextWriter` has received exactly
  that many characters when the exception is thrown, and `Actual` is the limit plus one. The given library's
  internal `OutputSink` (`Internal/LimitedStringWriter.cs`) already does this: write all renderer output through
  it. Compared exactly.

Conformance: the gate also prints the spec scores. They must equal today's (`gate/data/baseline-scores.txt`):
CommonMark 652 / 652; GFM spec 651 / 672 with the same 21 examples differing (the GFM 0.29 spec predates
CommonMark 0.31.2, and the library follows 0.31.2); GFM extensions 26 / 26 (footnotes are out of scope).

## 4. Rule 5: where the spec does not decide (D-127)

Arun's binding rule 5: no behaviour change without his approval. For you this means:
- When the spec decides a case, follow the spec, and the gate will agree.
- **When the spec is silent or ambiguous and only the frozen output decides**, match the frozen output, and
  record the case in `trace/rule5-cases.md` (case ids, what the spec says or "silent", what the frozen output
  shows, which rules implement it). Arun reviews every entry. Examples of areas where this is likely: the
  exact source spans of nodes, `data-sourcepos` columns, the degraded output past a nesting or table limit,
  unsafe-URL handling (`UnsafeUrlPolicy`, documented in `MarkdownHtmlOptions`), heading ids, tag-filter
  details, how the writer's input AST must look.
- If the spec and the frozen output **disagree**, do not pick one: stop on that case, record it in
  `trace/rule5-cases.md` with "conflict", and report it to the coordinator.
- Never change the given library to make a case pass.

## 5. Trace log (every rule)

- Each behaviour rule in your code carries `// TRACE R-MD-nnnn` where it is implemented, and has an entry in
  `trace/rules.md` (format in that file): location, source type (`spec`, `experiment`, `general`), citation,
  and the rule in one line in your own words.
- A spec citation names a heading of the bundled spec exactly, e.g. `CommonMark 0.31.2 "Fenced code blocks",
  examples 119-147`, or `GFM 0.29 "Tables (extension)"`.
- An experiment has a record `trace/experiments/E-nnnn.md` and points to its rule-5 case (or says
  `spec-covered` when the spec decides and the experiment only confirmed your reading).
- `general` is for plain engineering with no Markdown content.
- The log is append-only: a changed rule gets a new entry; the old one gets `- Status: superseded by R-MD-nnnn`.
- Expect roughly 300 to 400 rules in total.
- `gate trace` checks all of this. It also fails if an author file has no tag or names another
  implementation.

## 6. Phases and gates (D-127 timing)

| Phase | Scope | `gate all --phase` must pass | After the phase |
|---|---|---|---|
| P1 | Block structure (CommonMark "Leaf blocks", "Container blocks", link reference definitions, tabs, lazy continuation), block spans | P1: `blocks/P-CM` on every case, API, trace | **G2**: send the phase zip; continue with P2 while it is checked |
| P2 | Inlines and emphasis (code spans, emphasis and strong emphasis, links, images, autolinks, raw HTML, entities, escapes, breaks), inline spans; enough of the renderer for the CommonMark spec examples | P2: P1 plus `ast/P-CM`, `html/P-CM/H-Spec`, `html/P-CM/H-DataSourcepos`; CommonMark 652 / 652; pathological timing reported | **G1**: send the phase zip and **stop** until the coordinator relays a clean similarity report (or remediation items) |
| P3 | GFM: tables, task lists, strikethrough, extended autolinks, tag filter; both `MarkdownSyntax` checks | P3: P2 plus `blocks/P-GFM`, every `ast/P-*`, `html/*/H-GfmSpec`, `writer/*`, `text/*`; GFM scores equal to the baseline | **G2** |
| P4 | Every renderer option (raw HTML modes, unsafe URLs, soft breaks, heading ids, language class, task list styles, table alignment, XHTML voids), limits and degradation, contract probes, cancellation, timing, hardening | P4: every check, every probe, `gate time`, zero failures (pending rule-5 items need Arun's decision first) | **G2**, then hand-over |

Under G2 the next phase starts while the similarity check runs, with a hard stop before hand-over: findings
from any phase must be remediated and rechecked before the code is accepted.

Hardening in P4 also covers: no stack overflow at any nesting depth, no quadratic behaviour (the gate's
pathological inputs must each finish within their budget), deterministic output, thread safety of the
static entry points.

## 7. What to deliver at each phase end

A zip named `markup-cleanroom-P<n>.zip` with:
- `src/Chuvadi.Markup/Markdown/Parsing/` and `src/Chuvadi.Markup/Markdown/MarkdownHtmlRenderer.cs`;
- `trace/` (rules, sources, rule-5 cases, experiments);
- your tests, if any;
- `gate-report.txt` and the console output of `gate all --phase P<n>`;
- a short note: what the phase covers, rule counts, open rule-5 cases, anything you are unsure of.

Send it to the coordinator, who passes it on. Never include anything else.

## 8. How to run

From the bundle root (the folder with this file), with the .NET 10 SDK:

```
dotnet build gate/Chuvadi.Markdown.Gate -c Release
dotnet run -c Release --no-build --project gate/Chuvadi.Markdown.Gate -- all --phase P1
dotnet run -c Release --no-build --project gate/Chuvadi.Markdown.Gate -- run --phase P1 --set commonmark --show 20
dotnet run -c Release --no-build --project gate/Chuvadi.Markdown.Gate -- diff cm-0043 blocks/P-CM
dotnet run -c Release --no-build --project gate/Chuvadi.Markdown.Gate -- trace
dotnet run -c Release --no-build --project gate/Chuvadi.Markdown.Gate -- time
```

`README.md` describes the cases, checks and configurations.
