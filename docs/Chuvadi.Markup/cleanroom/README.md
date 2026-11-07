# Chuvadi.Markup clean-room record (D-106, D-195)

Since 1.1.0 the Markdown parser (`src/Chuvadi.Markup/Markdown/Parsing/`) and the HTML renderer
(`src/Chuvadi.Markup/Markdown/MarkdownHtmlRenderer.cs`) are a clean-room implementation. Its author worked only
from the CommonMark 0.31.2 and GFM 0.29 specifications and the frozen gate. The author never saw cmark, cmark-gfm,
other Markdown implementations, or the code that 1.1.0 removes. An independent checker compared each phase
against those references. This folder and `../similarity/` keep the evidence with the code. docs/ is never packed,
so none of this is in the NuGet package.

| Path | What it is | Written by |
|---|---|---|
| `trace/rules.md` | one entry per behaviour rule (`TRACE R-MD-nnnn` in the code): location, source, citation, the rule in the author's words | the author |
| `trace/sources.md` | every source the author read, per phase | the author |
| `trace/rule5-cases.md` | cases where the specs are silent or where the frozen data decides, with the decision | the author |
| `trace/experiments/` | experiment records (hypothesis, gate runs, counts, rules created) | the author |
| `BRIEF-author.md` | the brief the author worked to (firewall, gate, rule 5, phases), final state after patch 8 | the Markup chat |
| `BRIEF-checker.md` | the brief the similarity checker worked to | the Markup chat |
| `P0-bundle-files.md` | every file of the bundle the author received, with sizes, hashes and origin | the Markup chat |
| `../similarity/markup-P1.md` to `markup-P4.md` | the checker's phase reports (P4: revision 6, CLEAN) | the checker |

The checker's annexes are for the coordinator only and are not in the repository.

## Maintenance

The test `TraceRecordMatchesTheCode` (`tests/Chuvadi.Markup.Tests/Gate/FrozenGateTests.cs`) runs the trace check on
the shipped parser and renderer files against this folder:
- every `TRACE` tag in the code must have its entry in `trace/rules.md`;
- the code must not name a forbidden implementation.

A later change to the parser or renderer adds or updates its trace entries. A change to the output is a rule-5
change and refreezes the gate data in the same PR.

## Corrections to the brief's wording (2026-11-07)

`BRIEF-author.md` is kept exactly as the author received it. Two of its sentences describe the frozen data
wrongly. In both cases the frozen data, the author's code and 1.0.0 agree; only the brief's words were wrong:
- **G6:** the brief says that a task marker with nothing after it (`- [ ] `, `1. [x] `, `- [\t] `) is plain text.
  In fact `- [ ] ` and `1. [x] ` are task items with a checkbox and no text; only `- [ ]` (no space after the
  marker) and `- [\t] ` stay text.
- **G2:** the brief lists U+2028 among the whitespace that means "no link" right after `www.` or a scheme. In fact
  U+2028 there links, as in 1.0.0 (`http://` + U+2028, `www.` + U+2028 + `a`).
