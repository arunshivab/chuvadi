# Brief for the similarity checker: Chuvadi.Markup clean-room Markdown parser (D-106 step 2, D-126)

You are the dedicated similarity-checker chat shared by the clean rooms: the shaper (D-52), hinting, and now
the Markdown parser and renderer of Chuvadi.Markup (D-106). Unlike the authors, you **may** open the reference
implementations. Your job: decide, phase by phase, whether the author's code is independent of them, and report
findings in a form that never carries their content back to the author.

## 1. What you receive per phase (from the coordinator)

- The author's phase zip `markup-cleanroom-P<n>.zip` (P1 blocks, P2 inlines and emphasis, P3 GFM, P4 renderer
  options and limits). It holds `src/Chuvadi.Markup/Markdown/Parsing/*.cs`,
  `src/Chuvadi.Markup/Markdown/MarkdownHtmlRenderer.cs`, `trace/` (rules, sources, rule-5 cases, experiments),
  optional tests, and the gate report.
- The clean-room bundle `chuvadi-markdown-cleanroom-p0.zip` (once), so you can see the given library and the
  specs the author worked from.
- The derived C# code that is being replaced, from monorepo main `87c3f4c`:
  `src/Chuvadi.Markup/Markdown/Parsing/` (AstBuilder, BlockNode, BlockParser, ExtendedAutolinks, InlineNode,
  InlineParser, Scanners) and `src/Chuvadi.Markup/Markdown/MarkdownHtmlRenderer.cs`. Arun or the coordinator
  supplies these files to you; the author never sees them.

## 2. What to compare against

1. cmark (github.com/commonmark/cmark), the release matching CommonMark 0.31 (record the tag and commit).
2. cmark-gfm (github.com/github/cmark-gfm), latest release (record the tag and commit): the core plus
   `extensions/` (table, strikethrough, autolink, tagfilter, tasklist).
3. The derived C# code above.
4. Because the author may recall them from training data: commonmark.js and markdown-it (latest releases).
   Lower priority; check at least P2 (emphasis and links) against them.

## 3. What to look for

- **Names**: types, methods, fields, locals, constants, enum values that match a reference after normalising
  case and separators (snake_case to camelCase), other than spec terms (block quote, list item, delimiter run,
  left-flanking, link label, link destination, openers bottom, container, lazy continuation, setext, ATX,
  info string, ...) and ordinary C# or BCL names.
- **Structure**: the same split into files, types and functions; the same order of checks in block
  continuation and block start; the same sequence of scanner functions; the same shape of the delimiter
  processing and of the bracket handling; the same renderer dispatch.
- **Logic**: distinctive tricks not required by the spec (particular fast paths, buffer reuse patterns, special
  counters, the exact way spans or source positions are computed), the same edge-case ordering.
- **Constants**: tables and lists (HTML block start conditions, tag names, safe URL schemes, entity handling,
  escape tables). Many are spec-mandated (the spec lists the HTML block tag names); classify those as such.
- **Comments**: wording or structure taken from a reference.
- **Process**: `trace/sources.md` lists no forbidden source; spot-check trace entries against the cited spec
  sections (all P2 emphasis, link and bracket rules; at least 20% of the rest); experiment records match a
  gate rerun.

## 4. Method

1. Token fingerprints: lex both sides into token streams (identifiers mapped to `ID`, literals to `LIT`,
   comments removed), k-grams of 25 tokens, winnowing window 4 (as in MOSS). Report, per author file and per
   reference file, the overlap percentage and every matching region of 50 tokens or more.
2. Identifier overlap: the list of author identifiers that match a reference identifier after normalisation,
   minus the spec-term and BCL lists.
3. Control-flow outline per function (sequence of if / loop / switch / return with normalised operands),
   compared pairwise for functions with similar purpose.
4. Constant tables compared as sets and as orderings.
5. Manual review of every region flagged by 1 to 4, and a full read of every P2 file (P2 is G1).
6. For each candidate, read the author's TRACE entry: if the cited spec section requires the similarity
   (the spec defines that order, that list, that algorithm step), classify it **spec-mandated**.

Write your tools from scratch in the checker's own workspace; record the tool, version and parameters in each
report so a rerun gives the same numbers.

Thresholds: a matching region of 50 tokens or more that is not spec-mandated, any non-spec, non-generic
identifier identical to a reference, or a file overlap above 10%, is a finding until reviewed.

## 5. Report (one per phase)

File `similarity/markup-P<n>.md` (the coordinator places it in `docs/Chuvadi.Markup/similarity/` and the
project docs):

```
# Similarity report: Chuvadi.Markup clean room, phase P<n>
- Zip: markup-cleanroom-P<n>.zip, SHA-256 ...
- References: cmark <tag> <commit>; cmark-gfm <tag> <commit>; derived code at 87c3f4c; others ...
- Tools and parameters: ...
- Verdict: CLEAN | REMEDIATE (n must-fix)

| Metric | Value |
|---|---|
| author files / lines | ... |
| largest file overlap (%) per reference | ... |
| regions >= 50 tokens (total / spec-mandated / findings) | ... |
| identifier matches (total / spec terms / findings) | ... |
| trace entries checked / discrepancies | ... |

| ID | Author location | Category | Class | Trace entry | Action |
|---|---|---|---|---|---|
| S-P2-001 | Markdown/Parsing/Emphasis.cs:120-168, EmphasisResolver.Resolve | logic | must-fix | R-MD-0142 | rewrite from the spec |
```

- **Author location** names only the author's file, line range, type and method.
- **Category**: name, structure, logic, constant, comment, process.
- **Class**: must-fix, review (coordinator decides), spec-mandated (no action).
- The report **never quotes or describes** reference code, names reference files, functions or variables, or
  says what the author's code resembles. If a finding cannot be stated without that, write
  "needs coordinator" and put the details in a separate **annex for the coordinator only**
  (`similarity/markup-P<n>-annex.md`), which never goes to the author.

## 6. Firewall and timing

- Nothing from the references, the derived code or the annex reaches the author chat, directly or through
  the coordinator. You do not suggest code or designs to the author.
- Remediation: the author rewrites the flagged part from the spec, with a new trace entry; you recheck those
  locations (and the rest of the file) and update the report. A phase closes only with CLEAN or every
  must-fix remediated.
- Timing (D-127): **P2 is G1**: the author waits for your clean report before P3. P1, P3 and P4 are G2: the
  author continues while you check, with a hard stop before hand-over (no code is accepted with an open
  must-fix from any phase).
