# Similarity report: Chuvadi.Markup clean room, phase P2
- Zip: markup-cleanroom-P2.zip, SHA-256 33019b3c048f9ef362d376335ce1016013cd2eb88abd0d615e0dc655d8665e42
- Clean-room bundle: chuvadi-markdown-cleanroom-p0.zip, SHA-256 21ca3d757cc4b7fcd684f97ce76c8d1427024f7d14ffae57f3c53f18fec65cab
- References: cmark 0.31.2 eec0eeba6d31189fd828314576494566d539b1e3; cmark-gfm 0.29.0.gfm.13 587a12bb54d95ac37241377e6ddc93ea0e45439b;
  derived code at 87c3f4c (identical at 349ee73); commonmark.js 0.31.2 cb2c2303d3550ec6ef28ceb2841f148e8761eebf;
  markdown-it 15.0.3 a8c00d474ec9b304fad9b71c16d6fa8b4909279e
- Tools and parameters: simcheck 0.1.0 (sources SHA-256 e35df071…645f4c); token fingerprints k=25, winnowing w=4,
  regions >= 50 tokens; identifier normalisation with spec-term, given-library and BCL lists; control-flow outlines
  (functions with >= 8 control tokens, pairs >= 0.75); constant lists (set overlap >= 0.6); comment shingles of 6
  words. Review triggers: the brief's three, plus D-140 (control-flow shape >= 0.5, constant-list overlap >= 0.6).
  Gate rerun: gate all --phase P2 built from the bundle plus this zip.
- Scope: all 14 source files in the zip. P2 is G1, so every P2 file was read in full (InlineParser.cs,
  InlineParser.Links.cs, InlineParser.Scanning.cs, MarkdownHtmlRenderer.cs, MarkdownEngine.cs). The P1 files
  changed in P2 were run through the tools here; they are also reported in markup-P1.md.
- Verdict: **CLEAN** (0 must-fix). One item is marked review for the coordinator (S-P2-001).

| Metric | Value |
|---|---|
| author files / lines | 14 / 3,505 (19,406 tokens) |
| largest file overlap (%) per reference | cmark 1.0; cmark-gfm 0.4; derived code 12.7 (author file MarkdownHtmlRenderer.cs; see S-P2-001 and S-P2-002); commonmark.js 0.0; markdown-it 0.0 |
| regions >= 50 tokens (total / spec-mandated / findings) | 7 / 5 / 2 regions in one review item (S-P2-001), 0 must-fix |
| identifier matches (total / spec terms / findings) | 337 / 39 spec terms (203 given library, 61 generic, 11 BCL, 23 reviewed by hand) / 0 |
| control-flow pairs, shape >= 0.5 (D-140) | 4 functions / 4 spec-mandated / 0 |
| constant lists, overlap >= 0.6 (D-140) | 3 lists / 3 spec-mandated / 0 |
| comment shingles shared | 7 / 7 contract wording or generic / 0 |
| trace entries checked / discrepancies | 75 P2 entries checked for location and tag; the 30 emphasis, bracket, link and autolink entries (R-MD-0120 to R-MD-0149) read against the cited spec text / 0 |
| sources log | no forbidden source; only the bundle, the bundled specs and the .NET SDK installer |
| gate rerun | identical to the author's gate report: 21,866 / 22,064, API unchanged, TIME passed, TRACE 98 errors (all the known heading defect) |

| ID | Author location | Category | Class | Trace entry | Action |
|---|---|---|---|---|---|
| S-P2-001 | Markdown/MarkdownHtmlRenderer.cs:399-433, HtmlWriter.Url | logic | review | R-MD-0181 | Coordinator decides. Checker recommends no action (reasons in the annex). If a change is wanted: the author rewrites the UTF-8 percent-encoding part from the spec, with a new trace entry. |
| S-P2-002 | Markdown/MarkdownHtmlRenderer.cs:3-43, MarkdownHtmlRenderer.Render (both overloads) and HtmlWriter fields | structure | spec-mandated | R-MD-0167 | none: fixed by the given public contract and the given OutputSink |
| S-P2-003 | Markdown/Parsing/InlineParser.Links.cs:242-285, InlineParser.UriAutolink and InlineParser.EmailAutolink | structure | spec-mandated | R-MD-0148, R-MD-0149 | none: direct reading of the spec's scheme, URI and email definitions |
| S-P2-004 | Markdown/MarkdownHtmlRenderer.cs:288-329, HtmlWriter.Leave | structure | spec-mandated | R-MD-0170 to R-MD-0178 | none: the closing tags are fixed by the required HTML output |
| S-P2-005 | Markdown/Parsing/HtmlSyntax.cs:14 and :116; Markdown/Parsing/InlineParser.cs:109, InlineParser.ParseInto | constant | spec-mandated | R-MD-0012, R-MD-0020, R-MD-0110 | none: the spec's HTML block tag names (in the spec's order), the five HTML block end conditions, and the inline special characters |

Notes for the coordinator (author-safe):
- E-0017 was run on a throwaway copy that is not in the zip, so only its "delivered code" numbers could be rerun;
  they match.
- The two D-140 control-flow hits in P1 files (CharacterReferences.Resolve, LinkSyntax.Destination) are covered in
  markup-P1.md.
- The renderer parts the author marks as later-phase placeholders (unsafe-URL removal, heading ids, tag filter,
  task-list markers, tables) will be checked again when they are written (P3/P4).
