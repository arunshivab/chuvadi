# Similarity report: Chuvadi.Markup clean room, phase P3 (revision 2)
- Zip: markup-cleanroom-P3.zip revision 2, SHA-256 5e89be41ccb8d13743d8218378c9a9187740a5dee45f9791a8643a22480aaa95
  (it replaces revision 1, SHA-256 e3ace0678fa6c412fa9fd3abcc186f0dc7d3f8a56bcda1162618209a88c3219d)
- References: cmark 0.31.2 eec0eeba6d31189fd828314576494566d539b1e3; cmark-gfm 0.29.0.gfm.13 587a12bb54d95ac37241377e6ddc93ea0e45439b
  (core and extensions/: table, strikethrough, autolink, tagfilter, tasklist); derived code at 87c3f4c (identical at
  349ee73); commonmark.js 0.31.2 cb2c2303d3550ec6ef28ceb2841f148e8761eebf; markdown-it 15.0.3 a8c00d474ec9b304fad9b71c16d6fa8b4909279e
- Tools and parameters: simcheck 0.1.0 (sources SHA-256 e35df071…645f4c), with the same parameters and review
  triggers as markup-P2.md (the brief's three plus D-140). Gate rerun: gate all --phase P3 on the original bundle
  gate (patch2 and patch3 not applied, the same as the author).
- Scope: all 18 source files (G2). The four new files were read in full: BlockTables.cs, ExtendedAutolinks.cs,
  EmailAutolinks.cs and InlineParser.Autolinks.cs. So were the new renderer parts (tag filter, tables, task
  markers). The P3 trace sample covers 12 of the 46 new entries (more than 20%).
- Revision 2 changes one source file: Markdown/Parsing/BlockTables.cs, BlockParser.DelimiterRow (3 lines,
  R-MD-0233). It adds one experiment (E-0024, with pass and fail counts) and one rule-5 case (R5-0027). I
  re-ran every tool on all 18 files and read the change. Nothing new matched: regions, identifiers, constants
  and comments are identical to revision 1, and the control-flow pairs differ only by shifted line numbers.
- Verdict: **CLEAN** (0 must-fix). Two items are marked review: S-P3-001 (still open from P2) and S-P3-006
  (process).
- P1 review item S-P1-001 is resolved: the variable is renamed in BlockParser.ProcessLine. I rechecked
  BlockParser.cs; it has no remaining identifier finding.

| Metric | Value |
|---|---|
| author files / lines | 18 / 4,293 |
| largest file overlap (%) per reference | cmark 1.5; cmark-gfm 1.5; commonmark.js 0.0; markdown-it 0.6; derived code 13.0 (author file MarkdownSyntax.cs, 69 fingerprints, see S-P3-004), otherwise 10.4 (author file MarkdownHtmlRenderer.cs, see S-P3-001 to S-P3-003) |
| regions >= 50 tokens (total / spec-mandated / findings) | 8 / 6 / 2 regions in one review item (S-P3-001), 0 must-fix |
| identifier matches (total / spec terms / findings) | 375 / 47 spec terms (228 given library, 64 generic, 12 BCL, 24 reviewed by hand) / 0 |
| control-flow pairs, shape >= 0.5 (D-140) | 4 functions (none in the new P3 files) / 4 spec-mandated / 0 |
| constant lists, overlap >= 0.6 (D-140) | 4 lists / 4 spec-mandated / 0 |
| comment shingles shared | 7 / 7 contract wording or generic / 0 |
| trace entries checked / discrepancies | 230 entries checked for location and tag; 13 new P3 entries (every fourth, plus R-MD-0233) read against the GFM spec or their experiment / 0 discrepancies, 1 process item (S-P3-006) |
| sources log | unchanged; no forbidden source |
| gate rerun | revision 1 on the original gate: identical to the author's revision-1 report (114,401 / 115,472). Revision 2 on the original gate: 114,401 / 115,472, as expected without the refreeze. Not yet rerun with gate patches 2 and 3, which I do not have; the author reports 115,472 / 115,472 and TRACE passed with them. |

| ID | Author location | Category | Class | Trace entry | Action |
|---|---|---|---|---|---|
| S-P3-001 | Markdown/MarkdownHtmlRenderer.cs:400-434, HtmlWriter.Url | logic | review (open since S-P2-001) | R-MD-0181 | Coordinator decides, as for S-P2-001. Checker still recommends no action. |
| S-P3-002 | Markdown/MarkdownHtmlRenderer.cs:3-43, MarkdownHtmlRenderer.Render (both overloads) | structure | spec-mandated | R-MD-0167 | none (as S-P2-002) |
| S-P3-003 | Markdown/MarkdownHtmlRenderer.cs:594-635, TagFilter.Write and TagFilter.Filtered | structure, constant | spec-mandated | the tag filter rule | none: the nine tag names in the GFM spec's order, and the spec's "replace the leading < with &lt;" |
| S-P3-004 | Markdown/Parsing/MarkdownSyntax.cs:15-26, MarkdownSyntax.IsUriAutolinkBody | logic | spec-mandated | R-MD-0231 | none: the CommonMark absolute-URI definition; a small file, so one match gives a high percentage |
| S-P3-005 | Markdown/Parsing/InlineParser.Links.cs:242-285, InlineParser.UriAutolink and InlineParser.EmailAutolink | structure | spec-mandated | R-MD-0148, R-MD-0149 | none (as S-P2-003) |
| S-P3-006 | trace/experiments/E-0018, E-0019, E-0021, E-0023 | process | review | R5-0023, R5-0024 | Coordinator decides. Checker recommends that the author records pass and fail counts for E-0021 at least: it carries the main design decisions of the extended autolinks. The author says these numbers were not recorded. The new E-0024 does record its counts. |

Notes for the coordinator (author-safe):
- The new P3 files have no matching region, no control-flow pair of shape 0.5 or more, and no distinctive
  identifier against any reference. The cmark-gfm extension sources were checked by name, control flow and
  constants. The only GFM-extension identifier matches are spec terms (cell, delimiter row) and TableColumns,
  which comes from the given MaxTableColumns limit.
- Several autolink rules reproduce behaviour the GFM spec does not state (trailing quotes and a trailing
  non-entity ';' trimmed; the email pass after inline parsing). Each is traced to an experiment on the frozen
  outputs (E-0021, R5-0024), which is the black-box oracle the brief allows. The reasoning recorded in E-0021
  and R5-0024 explains why the data forces these choices.
- R5-0025 (U+000B before www.) is a rule-5 decision for Arun, not a similarity matter.
