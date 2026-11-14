# Similarity report: Chuvadi.Markup clean room, phase P4 (revision 10, gate patch 13)

## Revision 10 update (2026-11-13)
- Zip: markup-cleanroom-P4_10.zip (revision 10), SHA-256 7dd6b61678cddf7cfaf7ff288e7671c0a18e71ac565308877a15dbbba888f0cd,
  119,874 bytes.
- Code change against revision 9: 2 files, 17 lines added and 1 removed. I read all of it.
  1. ChunkedList.Add (MarkdownEngine.cs:89-107): the first chunk of the inline work list starts at 4 entries and
     doubles up to 1,024. Later chunks stay at 1,024 entries, below the large-object size (R-MD-0279 unchanged).
     - The index arithmetic still holds: entries 0 to 1,023 are in chunk 0, which is 1,024 long by the time entry 1,023
       is written; the next chunk is added at entry 1,024.
  2. InlineParser.ProcessEmphasis: returns before allocating its 14-entry table when there is no delimiter run after
     `bottom`.
     - The early return is exact. In that case the matching loop does not run, and the final cleanup (R-MD-0130) has no
       runs to remove.
  - One new trace entry, R-MD-0281 (general, cites D-368 and D-249), and one new experiment, E-0034. Every other source
    and trace file is unchanged.
- Similarity: simcheck 0.3.0 rerun on all 18 files. Regions (9), control-flow pairs of 0.5 or more, constant lists (4)
  and comment shingles (7) are unchanged. There is one new identifier, `array` (generic). No new candidate and no new
  item.
- Trace: 278 entries (268 active). R-MD-0281 and E-0034 match the code. E-0034 records gate runs and the author's
  own probes and allocation census only, with no outside source. Discrepancies: 0.
- Gate rerun, full bundle with patches 2 to 13: **353,669 / 353,669**, Spans passed, Storage passed,
  **Small (D-368) passed, 163.0 bytes per char** (at most 175; revision 7 gives 159.4), API unchanged, TRACE PASSED,
  TIME PASSED, **GATE P4 PASSED**.
  - All per-check lines are identical to the author's report.
  - Small gave 163.0 in all three of my runs, and two more `gate time` runs passed.
- Bytes allocated per parse (my probe, default options):

  | input | revision 7 | revision 9 | revision 10 |
  |---|---|---|---|
  | `a` | 2,752 | 19,208 | **2,752** |
  | `# h` LF | 2,760 | 19,216 | 2,760 |
  | `- a` LF `- b` LF | 5,424 | 21,808 | 5,216 |
  | `` `c` *e* [l](u) `` LF | 5,560 | 21,872 | 5,416 |
  | `a\|b` table, 3 lines | 9,320 | 25,608 | 8,744 |
  | 1,000 chars | 12,856 | 29,312 | 12,856 |
  | 200 paragraphs | 395,640 | 389,560 | 354,248 |

  - Revision 10 allocates the same as or less than revision 7 on every input, and the fixed 16.4 KB is gone. These
    figures match E-0034's to within 64 to 136 bytes.
- `gate mem`, revisions 9 and 10 alternately on bundle 13, twice each:

  | size | revision 9 | revision 10 |
  |---|---|---|
  | 512 KiB | 33.7-35.7 M chars/s, 0 gen2, 1.9 MiB alive | 36.2-36.6, 0 gen2, 1.9 MiB |
  | 1 MiB | 27.8-30.4, 0 gen2, 4.0 MiB | 28.8-31.2, **0 gen2**, 4.0 MiB |
  | 2 MiB | 26.5-27.0, 0 gen2, 8.1 MiB | 24.1-25.8, **0 gen2**, 8.1 MiB |
  | 4 MiB | 25.3-25.5, 0 gen2, 16.3 MiB | 23.6-25.8, **0 gen2**, 16.3 MiB |

  - No gen2 at 1, 2 or 4 MiB, as required. Alive memory and allocation per char are identical.
  - Throughput is the same within this machine's noise. At 2 MiB revision 10 was 4 to 11% lower in both runs, but that
    is inside the spread E-0034 also reports for large sizes. Nothing changed for large inputs except the first chunk,
    so I do not read this as a regression.
- Speed on the 1,354 spec examples (the D-249 point), parse only, best of 7:
  - Six alternating rounds from the same prebuilt binaries give medians of revision 7 10.07, revision 9 9.87 and
    revision 10 10.03 M chars/s. Revision 10 is at 1.00 of revision 7 (1.1.0's parser). Taking BRIEF.md's 1.35 for
    1.1.0 against 1.0.0, that is about 1.35 times 1.0.0, well inside the D-249 rule.
  - I cannot confirm E-0034's 1.84 times speed-up over revision 9. Under `dotnet run`, revision 9 came out at 0.59 of
    revision 7 (6.3 against 10.8 M chars/s) and revision 10 at 0.94. From the prebuilt binaries, revision 9 came out
    at 0.98.
  - So on this machine revision 9's slowdown depends on run conditions, most likely the extra gen0 collections from
    16 KB per parse. Revision 10 is level with revision 7 under both.
  - I have no 1.0.0 build here, so I have not measured the D-249 ratio against 1.0.0 directly.
- Verdict: **CLEAN** (0 must-fix, no new review item). P1 to P4 have no open item.

## Patch 13 confirmation (2026-11-13)

- Gate patch 13 (D-368): chuvadi-markdown-cleanroom-p0-patch13.zip, 32,893 bytes, SHA-256
  df077bb39f0983c6a7ccc1999bc38fbb7fcb9a52ecd7e1bbef3dfce05be9ae96. **Confirmed.**
  - It holds exactly the 4 files the note lists: GateCore.cs (b1e636d7…), Program.cs (57fb8f7d…), BRIEF.md and
    README.md. No frozen data change, and no given-library change (MarkdownNode.cs as in patch 12).
- Full bundle with patches 2 to 13: chuvadi-markdown-cleanroom-p0.zip, 6,358,179 bytes, SHA-256
  87c9e86b6490a49b54512e6e83cadb7809b3302449c72b99d2fb4651d2326af8, 67 files. **Confirmed.** All 67 files, the author
  stubs included, are byte-identical to my patch-12 bundle with patch 13 applied.
- The Small check (GateSmall): default options over the 1,354 inputs of the three spec sets, two warm-up passes and
  one measured pass, using the thread's allocation counter. It passes at 175 bytes per char or less, and GATE P4
  requires it.
- Controls on bundle 13:

  | revision | outputs | Spans | Storage | Small (D-368) | TIME | GATE P4 |
  |---|---|---|---|---|---|---|
  | 9 | 353,669 / 353,669 | passed | passed | **NOT PASSED, 702.1 B/char** | passed | NOT PASSED |
  | 7 (1.1.0's parser) | 280 failing (patch-10 refreeze) | NOT PASSED (no counter) | passed | **passed, 159.4 B/char** | passed | NOT PASSED |

  - Both match the note's table.
  - Revision 9's figure is 702.1 in all three of my runs, so the check is deterministic as BRIEF.md says.
- My own probe, bytes allocated per parse with default options:

  | input | revision 7 | revision 9 |
  |---|---|---|
  | `a` | 2,752 | 19,208 |
  | `# Hi` | 2,776 | 19,232 |
  | 1,000 chars | 12,856 | 29,312 |
  | 200 paragraphs | 395,640 | 389,560 |

  - Revision 9 carries a fixed overhead of about 16.4 KB per parse, whatever the input's size, as the note says.
  - On larger documents revision 9 is level with revision 7 or slightly lower.
  - My `a` figure for revision 7 is 2,752 bytes, not the note's 2,688 for 1.1.0. Revision 7 here runs on the patch-12
    given library (NodeChildren), which may account for the 64 bytes; I have not checked against 1.1.0 itself.
- Two small wording points in the gate, neither of which affects the result:
  - The GateSmall doc comment says 1.1.0 allocates "about 155 bytes per char". BRIEF.md and the gate measure 159.4 for
    revision 7.
  - README.md still says `gate mem` covers "patches 10 and 11".
- Revision 10 is awaited. When it arrives I will review its diff, rerun simcheck, and run the gate with patches 2 to 13
  (P4 including Small, `gate time`, and `gate mem` checking for no gen2 at 1, 2 and 4 MiB). I will also check the D-249
  speed point BRIEF.md names.

# Similarity report: Chuvadi.Markup clean room, phase P4 (revision 9, gate patch 12)

## Patch 12 update (2026-11-11)
- Gate patch 12 (D-344): chuvadi-markdown-cleanroom-p0-patch12.zip, 28,455 bytes, SHA-256
  6cdf63d1190f6f3e4eaab6ea7274f227d9e8d9fe1110073d2b525a5fa866107c. **Confirmed.**
  - It holds exactly the 4 files the note lists: MarkdownNode.cs (bb168567…), GateCore.cs (467ef598…), BRIEF.md and
    README.md. There is no data change and no other gate program change.
- Full bundle with patches 2 to 12: chuvadi-markdown-cleanroom-p0.zip, 6,356,793 bytes, SHA-256
  3d2fe9082eb5fe5bab7e75b1be9ef24fb37be9b251dccb9c5b4a9612171e2d8e, 67 files. **Confirmed.**
  - All 60 non-author files are byte-identical to my own tree: the original bundle with patches 2 to 11 applied in
    order, then patch 12.
  - The other 7 are the author's stubs and templates.
- No author change: revision 9 (bd3ac58a…87d8) is checked unchanged, so the similarity results and verdict below
  stand.
- Given library, `MarkdownNode` (D-344):
  - Each node now keeps one `NodeChildren` object for its whole life. Storage is one array up to 8,192 entries, then
    chunks of 4,096, or a `List` once `MutableChildren` or `SetChildren` is used.
  - My probe on the built library:
    - 30,000 random appends, inserts and removes across the switch give the same contents, order and parents as a
      `List` model.
    - A `Children` view held from the first child stays the same object and stays live through the switch to chunks,
      through `MutableChildren` and through `SetChildren`.
    - An enumerator open across the switch to a list throws "Collection was modified", as a `List` does.
    - Exception types, parameter names and messages match `List`, in both the array and the chunk forms.
  - **The stale view reported with patch 11 is fixed.**
  - One edge remains, and the new remark documents it: a view taken while the node has no children is the shared
    empty array and stays empty.
- Gate on the patch-12 bundle:

  | revision | outputs | Spans | Storage (D-331) | TIME | TRACE | API | GATE P4 |
  |---|---|---|---|---|---|---|---|
  | 9 | 353,669 / 353,669 | passed | passed | passed | passed | unchanged | **PASSED** |
  | 8 (control) | 353,669 / 353,669 | passed | NOT PASSED ("not chunked") | passed | passed | unchanged | NOT PASSED |

  - Revision 9's per-check lines are identical to its patch-11 run.
  - Two more `gate time` runs passed, and the heading now reads "of the token firing".
- `gate mem`, revision 9 on patch 12 against patch 11, run alternately, twice each:

  | size | patch 11 | patch 12 |
  |---|---|---|
  | 512 KiB | 36.2-38.3 M chars/s, 27.0 B/char, 0 gen2, 2.0 MiB alive | 34.9-36.7 M chars/s, 27.1 B/char, 0 gen2, 1.9 MiB |
  | 1 MiB | 29.4-29.5, 26.9, 0 gen2, 4.1 MiB | 28.9-29.3, 27.0, 0 gen2, 4.0 MiB |
  | 2 MiB | 25.3-26.8, 26.7, 0 gen2, 8.3 MiB | 25.7-25.8, 26.8, 0 gen2, 8.1 MiB |
  | 4 MiB | 23.6-24.6, 26.6, 0 gen2, 16.6 MiB | 23.8-24.9, 26.7, 0 gen2, 16.3 MiB |

  - The alive and allocation figures repeated exactly and match the note's table: 1.9, 4.0, 8.1 and 16.3 MiB, and
    about 0.1 byte per char more allocated.
  - Throughput is the same within noise. 512 KiB came out about 3% lower in both runs, which is inside the spread.
- Verdict: **CLEAN**, unchanged (0 must-fix, no review item). P1 to P4 have no open item.

## Revision 9 report (unchanged below)

## Revision 9 update (2026-11-11)
- Zip: markup-cleanroom-P4_8.zip (revision 9), SHA-256 bd3ac58adee889b830e32f3c899eaa4678322968ac6552602f892652179987d8,
  116,188 bytes.
- Gate patch 11: SHA-256 f728dc177b971700e4a311658b1d929a6516efc4d1d9a2590db550b00902fa2f, 34,823 bytes, confirmed on
  2026-11-11. It changes the given MarkdownNode (chunked child storage above 8,192 children, D-330), adds the gate's
  Storage check (D-331), adds 4 MiB to `gate mem`, and fixes the `gate time` heading wording. No data change. Revision
  8 as a control: Storage NOT PASSED, as BRIEF.md says.
- Code change against revision 8: two files (MarkdownEngine.cs and EmailAutolinks.cs). I read all of it. Four passes
  now read children through `Children`, by index: MarkdownEngine.ClampTree, InlineTree.MergeAdjacentText,
  InlineTree.FlattenDeepEmphasis and EmailAutolinks.Apply. `MutableChildren` or `SetChildren` is taken only when there
  is a change to make: text nodes to merge, emphasis to flatten, an email address to split. One new trace entry,
  R-MD-0280 (general, cites D-331), and one new experiment, E-0033. Every other source and trace file is unchanged.
  - None of these passes keeps a `Children` view while adding children to the same node, so the stale-view gap in the
    given library (reported with patch 11) does not affect the author's code.
- Similarity: simcheck 0.3.0 rerun on all 18 files. Regions (9), identifiers (388, same 25 candidates), control-flow
  pairs of 0.5 or more, constant lists (4) and comment shingles (7) are unchanged. No new item.
- Trace: 277 entries (267 active, 10 superseded). I read R-MD-0280 and E-0033; both match the code. E-0033 records gate
  runs and measured figures only, and no outside source. Discrepancies: 0.
- Gate rerun, bundle with patches 2 to 11: **353,669 / 353,669**, Spans passed, **Storage (D-331) passed**, API
  unchanged (802 lines), TRACE PASSED, TIME PASSED, GATE P4 PASSED. All per-check lines are identical to the author's
  report.
- `gate mem` on this machine (2 cores), revisions 8 and 9 alternately with patch 11 under both, twice each. Alive and
  gen2 figures repeated exactly:

  | size | revision 8 | revision 9 |
  |---|---|---|
  | 512 KiB | 36.6-39.0 M chars/s, 0 gen2, 2.0 MiB alive | 35.1-37.7 M chars/s, 0 gen2, 2.0 MiB alive |
  | 1 MiB | 31.8-32.7 M chars/s, 0 gen2, 4.1 MiB | 31.6-32.3 M chars/s, 0 gen2, 4.1 MiB |
  | 2 MiB | 19.0-20.4 M chars/s, **1 gen2**, **56.8 MiB** | 27.7-28.5 M chars/s, **0 gen2**, **8.3 MiB** |
  | 4 MiB | 18.6-19.6 M chars/s, **1 gen2**, **65.6 MiB** | 25.1-26.8 M chars/s, **0 gen2**, **16.6 MiB** |

  - These match the BRIEF.md table exactly: 8.3 and 16.6 MiB alive, about 4.1 bytes per input char.
  - Every patch-11 target is met: no gen2 at 2 or 4 MiB, at most about 4.2 bytes alive per input char, output
    unchanged.
  - Throughput is about 40% higher at 2 and 4 MiB. 512 KiB and 1 MiB are within 4%, inside the run-to-run spread.
  - The author's machine showed no gen2 collection for revision 8 at any size (E-0033). Mine, like the reference
    machine, shows 1 at 2 and 4 MiB. The difference is the machine (the gen0 budget follows the CPU cache), not the
    code.
- Verdict: **CLEAN** (0 must-fix, no new review item). P1 to P4 have no open must-fix or review item.

## Revision 8 report (unchanged below)

## Revision 8 update (2026-11-10)
- Zip: markup-cleanroom-P4_7.zip (revision 8), SHA-256 5680e02e46e801d42640cc1635263980bf9574889fed9840c6b81453c18d64e2,
  114,046 bytes.
- Gate patch 10: SHA-256 8c0e0a7e45eb23e0d25d06fe3972f7525ec6a7199e220a36f5913918aa7974a9, 6,096,210 bytes, confirmed
  on 2026-11-10. It changes 9 cases (157 outputs) and adds the set pins (35 cases, 1,190 outputs), for 10,423 cases and
  353,669 outputs. On the gate side it adds the span check (D-309), `gate mem` (reported only) and cancel timing from
  the token's firing (D-294). Revision 7 as a control: 280 failing outputs in exactly the 18 cases BRIEF.md names,
  Spans NOT PASSED.
- Code change against revision 7: 6 files, 103 lines added and 27 removed. I read all of it.
  1. Tab positions (D-309): BlockParser.AddParagraphLine (BlockStarts.cs:282-305) drops the revision-6 shift. Content
     after a partly used tab keeps its true position, and the line ends at its line end. ContentLine loses `Shift`
     (BlockState.cs); the inline join no longer adds it. R-MD-0274 (spec, CommonMark "Tabs") supersedes R-MD-0266.
  2. Span safety net (D-309): MarkdownEngine.ClampSpan and ClampTree (MarkdownEngine.cs:12-43) with the
     `[ThreadStatic] internal static int SpanClampsOnThisThread` counter. ClampTree walks the tree after the inline
     phase with one stack frame per level and polls cancellation. LinkReferenceReader.Read clamps definition spans
     through the same function. R-MD-0275. The renderer's guard in HtmlWriter.SourcePos stays, for trees a caller
     builds or changes (R-MD-0276 supersedes R-MD-0272).
  3. data:image (D-303): UrlSafety.IsDangerous (MarkdownHtmlRenderer.cs:633-660) now needs ";" or "," right after
     png, gif, jpeg or webp. R-MD-0277 supersedes R-MD-0234.
  4. The renderer's class summary gives the measured scores (652/652, 651/672, 26/26 with footnotes excluded).
     R-MD-0278.
  5. Memory (D-303, issue #94): InlineBlocks becomes a ChunkedList (MarkdownEngine.cs:79-110, chunks of 1,024
     entries, kept off the large object heap), and each entry is cleared once its block is parsed. R-MD-0279, E-0032.
- Similarity: simcheck 0.3.0 rerun on all 18 files (4,755 lines). Regions, constant lists and comment shingles are
  unchanged (9, 4 and 7). There are no new identifiers (388; one fewer, `shift`) and no new candidate. The functions
  with control-flow shape of 0.5 or more are the same as before; none of the new functions (ClampSpan, ClampTree,
  ChunkedList, IsDangerous) reaches 0.5. The largest derived-code overlap, for MarkdownHtmlRenderer.cs, goes down
  slightly (10.0 to 9.7). No new item.
- Trace: 276 entries (266 active, 10 superseded in the D-198 form). I read the 6 new entries R-MD-0274 to R-MD-0279
  and E-0032. Each cites the ruling or the spec section BRIEF.md asks for. The 3 supersessions (R-MD-0234, R-MD-0266,
  R-MD-0272) are marked. E-0032 records gate runs, the author's own probes and GC settings used for diagnosis only.
  It records no outside source. Discrepancies: 0.
- Gate rerun, bundle with patches 2 to 10: **353,669 / 353,669**, Spans (D-309) passed (10,422 inputs, every parse and
  limit configuration, no span clamped), API unchanged (802 lines), TRACE PASSED, TIME PASSED, GATE P4 PASSED. All 473
  per-check lines are identical to the author's report.
- My own checks:
  - By reflection: ClampSpan leaves an in-range span alone, clamps 4+6 in a 5-char input to 4+1 and 9+2 to 5+0, and
    counts 2. The count stays 0 on another thread, so the counter is per thread as D-309 asks.
  - On the tab and lazy-line inputs from E-0029 and pin-0001 (` - - ~` LF TAB `|`, `- >o` LF TAB `[`, the lazy
    definition cases, `\t:-<b>[X] `), the counter stays 0, every span is inside the input, and data-sourcepos
    is in range.
- `gate mem` on this machine (2 cores), revisions 7 and 8 run alternately, twice each. Alive and gen2 figures repeated
  exactly; throughput is from the two runs:

  | size | revision 7 | revision 8 |
  |---|---|---|
  | 512 KiB | 39.7 M chars/s, 0 gen2, 2.4 MiB alive | 42.6 M chars/s, 0 gen2, 2.1 MiB alive |
  | 1 MiB | 19.2-21.5 M chars/s, **1 gen2**, **66.5 MiB** alive | 33.1-33.6 M chars/s, **0 gen2**, **4.1 MiB** alive |
  | 2 MiB | 21.1-22.0 M chars/s, 1 gen2, 70.1 MiB alive | 19.7-20.8 M chars/s, 1 gen2, 56.8 MiB alive |

  - This machine reproduces the reference machine's revision-7 figures (1 gen2 and 66.5 MiB at 1 MiB).
  - The 1 MiB goal (no gen2, about 10 MiB alive at most) is met.
  - At 2 MiB less data is alive, but one gen2 collection per round remains, and throughput was about 5% lower than
    revision 7 in both runs. That is within the run-to-run spread E-0032 reports (up to about 20%), so I cannot call it
    worse. The author's note says the remaining 2 MiB holder is the document's child list in the given MarkdownNode
    class, which the author left alone as not theirs to change.
- Verdict: **CLEAN** (0 must-fix, no new review item). P1 to P4 have no open must-fix or review item.
- For the coordinator, not similarity matters:
  - Whether to change the given MarkdownNode child store for the 2 MiB case is Arun's call. The author did not touch
    the given library.
  - `gate time`'s heading line still says "within 0.75 s of the cancel" after D-294. This is cosmetic, in the gate
    program, not the author's code.

## Revision 7 report (unchanged below)

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
