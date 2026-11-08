# Similarity report: Chuvadi.Markup clean room, phase P4 (revision 7)

## Revision 7 update (2026-11-08)
- Zip: markup-cleanroom-P4_5.zip (revision 7), SHA-256 599f5f2df11d7d41924047f76c7a8a28cc885fab3c9d123f487c49433d54c1ac,
  109,135 bytes.
- Gate patch 9: chuvadi-markdown-cleanroom-p0-patch9.zip, SHA-256
  4f9d696e4205d04b93c2d055a344de295dfde6bf926c7952da4981d11075dc82, 25,205 bytes. **Confirmed.** It changes only
  BRIEF.md, README.md and the gate program (GateCore.cs adds GateCancel, 12 shapes cancelled at 20, 120 and 300 ms with
  a 0.75 s limit, plus a line-ending fix for exception messages; Program.cs runs it under `gate time`). No frozen data
  and no library code changed.
- Code change against revision 6: two files, a few lines. WorkBudget (InlineParser.Scanning.cs:184-207) now holds the
  given library's CancellationPoll and ticks it in Step; MarkdownEngine.Parse passes the token in. WorkBudget is a
  class, so the mutable poll in its field works. One new trace entry, R-MD-0273 (general, cites the BRIEF's
  cancellation contract), and one new experiment, E-0031. Every other source and trace file is byte-identical.
- Similarity: simcheck 0.3.0 rerun on all 18 files gives exactly the revision-6 results (9 regions, 389
  identifiers with the same 25 candidates, 6 control-flow functions, 4 constant lists, 7 comment shingles). The only
  difference is where the given-library name CancellationPoll is used. No new item.
- Gate rerun, bundle with patches 2 to 9:
  - Revision 7: 352,479 / 352,479, API unchanged, TRACE PASSED (263 tagged rules, 270 entries), TIME PASSED with all 12
    cancellation shapes ok, GATE P4 PASSED. All 439 per-check lines are identical to the author's report. Two more
    `gate time` runs also passed.
  - Revision 6 as a control: TIME NOT PASSED, LATE on emphasis `*a_ `, brackets `[`, code spans, www autolinks and
    `a\n` lines, the shapes BRIEF.md names. So the gate check is real and revision 7 fixes it.
- My own probe (cancel at 20, 120, 300 ms on brackets, code spans, www links and emails): revision 6 ran on for 1 to 2 s
  after the token fired; revision 7 stopped within 0 to 45 ms of it. Where the cancel timer itself fired late, it was
  the timer, as E-0031 says.
- Verdict: **CLEAN** (0 must-fix, no new review item). P1 to P4 have no open must-fix or review item.

## Revision 6 report (unchanged below)
- Zip: markup-cleanroom-P4_4.zip (the author's revision 6), SHA-256
  1d402a9d39560257635ec69be786f16bd5718ca94905f8c943024fe8e04237c7, 107,334 bytes.
- Earlier revisions: revision 2 (b1b97334…) was reported CLEAN. Revisions 3 (6a580200…) and 5 (88536b2d…) left every
  source file byte-identical to revision 2 and changed only the trace docs, the note and the gate files.
- Gate patch 8: chuvadi-markdown-cleanroom-p0-patch8.zip, SHA-256
  f6250633199376b4f256f8a83ee2883116391d374e9f795654386aa45eb9a0bc, 6,065,597 bytes. **Confirmed.**
  - It holds only BRIEF.md, README.md and three gate/data files (expected.jsonl.gz, NOTICE.txt, rule5-rules.txt).
    No code, no gate program change.
  - The data adds exactly 1,336 cases, dd-0001 to dd-1336, all in the new set diff-domain (45,424 expected outputs).
    Every one of the 9,052 patch-7 cases is unchanged and none is removed. Totals: 10,388 cases, 352,479 outputs,
    as README.md says.
  - The BRIEF.md, NOTICE.txt and rule5-rules.txt changes only describe D-242. The frozen outputs agree with the
    ruling's examples: `www..a`, `http://.c` and `www..` are text; `http://-`, `HTTP://-`, `http://intranet/path`
    and `http://localhost:8080/x` link.
- References: cmark 0.31.2 eec0eeba; cmark-gfm 0.29.0.gfm.13 587a12bb; commonmark.js 0.31.2 cb2c2303; markdown-it
  15.0.3 a8c00d47; derived code at 87c3f4c (identical at 349ee73).
- Tools and parameters: simcheck 0.3.0 (same output as 0.1.0 on the P4 run), the same parameters and triggers as
  before (the brief's three plus D-140).
- Scope (G2, all 18 files): all 12 source files changed since revision 5 were diffed and every changed function was
  read in full: the renderer (HtmlWriter.Url, Slugs.Make, PlainTextOf, TaskMarkerFor, SourcePos), BlockParser,
  BlockStarts, BlockState, BlockTables, CharacterReferences, EmailAutolinks, ExtendedAutolinks,
  InlineParser.Autolinks, InlineParser.Links, LineCursor and LinkReferenceReader (about 384 diff lines). The six
  unchanged files were rerun through the tools with the rest.
- Verdict: **CLEAN** (0 must-fix). No new review item. The two existing review items are closed under D-186.
- Release status: no phase (P1 to P4) has an open must-fix or an open review item.

| Metric | Value |
|---|---|
| author files / lines | 18 / 4,692 (revision 5: 4,426) |
| largest file overlap (%) per reference | cmark 2.8; cmark-gfm 2.8; commonmark.js 0.0; markdown-it 0.5; derived code 13.0 (MarkdownSyntax.cs, unchanged since P3, see S-P3-004), otherwise 10.0 (MarkdownHtmlRenderer.cs, revision 5: 11.4) |
| regions >= 50 tokens (total / spec-mandated / findings) | 9 / 6 / 3 regions in the two closed items S-P4-001 and S-P4-002, 0 must-fix. The S-P4-001 region shrank from 97 to 54 tokens because Slugs.Make changed |
| identifier matches (total / spec terms / findings) | 389 / 47 spec terms (236 given library, 65 generic, 16 BCL, 25 reviewed by hand, the same 25 as before) / 0. The 9 new names are BCL, given-library or generic |
| control-flow pairs, shape >= 0.5 (D-140) | 6 functions, the same 6 as revision 5 / 6 spec-mandated / 0. No changed function reaches 0.5 |
| constant lists, overlap >= 0.6 (D-140) | 4 lists, unchanged / 4 spec-mandated / 0 |
| comment shingles shared | 7, unchanged / 7 contract wording or generic / 0 |
| trace entries checked / discrepancies | 269 entries (262 active, 7 superseded in the D-198 form) pass the gate's tag and location check; all 26 new entries (R-MD-0247 to R-MD-0272) read against their ruling, citation or experiment; E-0029, E-0030, R5-0033 and R5-0034 read / 0 |
| sources log | unchanged; no forbidden source. E-0029 and E-0030 record gate runs and measured counts only |
| gate rerun | bundle with patches 2 to 8, revision 6 overlaid: **352,479 / 352,479**, GATE P4 PASSED, API unchanged (802 lines), TRACE PASSED (262 tagged rules, 269 entries, 34 rule-5 cases), TIME PASSED. All 439 per-check lines identical to the author's gate report |
| behaviour probes (my own harness on the gate build) | stray `%` kept in links, autolinks, images and www links (`%zz%41%`, `a%2`); heading ids keep Tamil and Hindi vowel signs and viramas and the NFD accent of `café`; repeats get -1; the D-242 domain examples as frozen. All as Arun ruled |

| ID | Author location | Category | Class | Trace entry | Action |
|---|---|---|---|---|---|
| S-P4-001 | Markdown/MarkdownHtmlRenderer.cs:706-736, Slugs.Make | logic | review, closed (D-186) | R-MD-0271 (supersedes R-MD-0236), R-MD-0237 | none. The rewrite for the marks rule (D-237) shortened the match |
| S-P4-002 | Markdown/MarkdownHtmlRenderer.cs:428-471, HtmlWriter.Url | logic | review, closed (D-186) | R-MD-0181, R-MD-0255 | none. The `%` change (F7, D-232 N5) adds no match |
| S-P4-003 | Markdown/MarkdownHtmlRenderer.cs:3-43, MarkdownHtmlRenderer.Render (both overloads) | structure | spec-mandated | R-MD-0167 | none (as S-P2-002) |
| S-P4-004 | Markdown/MarkdownHtmlRenderer.cs:658-705, TagFilter | structure, constant | spec-mandated | the tag filter rule | none (as S-P3-003) |
| S-P4-005 | Markdown/Parsing/BlockStarts.cs:142-163, BlockParser.IsThematicBreak | logic | spec-mandated | R-MD-0088, R-MD-0243 | none (unchanged) |
| S-P4-006 | Markdown/Parsing/LineCursor.cs:84-106, LineCursor.MeasureSpaces | logic | spec-mandated | R-MD-0244 | none (unchanged) |
| S-P4-007 | carried, unchanged: InlineParser.UriAutolink, HtmlWriter.Leave, CharacterReferences.Resolve, LinkSyntax.Destination | various | spec-mandated | as in P1-P3 | none |

Notes for the coordinator (author-safe):
- The revision-6 code is the author's own structure throughout: the extended-autolink domain rules
  (SchemeLength, IsDomainChar, ValidDomain, DomainEnd, ScanEmailDomain), the email floor, the table edges, the lazy-line
  spans and the task-marker move have no region, no identifier candidate and no control-flow pair against any reference.
- E-0029's starting count (30,824 failing checks against patch 7) matches my own run of revision 5 against patch 7.
- R-MD-0264 is fitted to a single frozen case (dc-1337), as the entry says. That is a coverage note, not a
  similarity matter.
- No annex update is needed beyond one line on the shorter S-P4-001 region (sent with this report).
