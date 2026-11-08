# Similarity report: Chuvadi.Markup clean room, phase P1
- Zip: markup-cleanroom-P1.zip, SHA-256 334c0e4aa57e5c91e5b5c26cc5e4d761e479165e5cfa5619f5f58d2659dcd4d1
- References: cmark 0.31.2 eec0eeba6d31189fd828314576494566d539b1e3; cmark-gfm 0.29.0.gfm.13 587a12bb54d95ac37241377e6ddc93ea0e45439b;
  derived code at 87c3f4c (identical at 349ee73); commonmark.js 0.31.2 cb2c2303d3550ec6ef28ceb2841f148e8761eebf;
  markdown-it 15.0.3 a8c00d474ec9b304fad9b71c16d6fa8b4909279e
- Tools and parameters: simcheck 0.1.0 (sources SHA-256 e35df071…645f4c). Same parameters as markup-P2.md:
  k=25, w=4, regions >= 50 tokens, control-flow pairs >= 0.75, D-140 review triggers (control-flow shape >= 0.5,
  constant-list overlap >= 0.6). Gate rerun: gate all --phase P1 built from the bundle plus this zip.
- Scope: all 11 source files in the zip, two of them still stubs (G2: tool checks, manual review of every
  flagged region, and a 20% trace sample). The five P1 files changed in P2 were also run through the tools in
  the P2 check, with the same result.
- Verdict: **CLEAN** (0 must-fix). One item is marked review for the coordinator (S-P1-001).

| Metric | Value |
|---|---|
| author files / lines | 11 / 1,802 (9,796 tokens) |
| largest file overlap (%) per reference | cmark 0.0; cmark-gfm 0.0; commonmark.js 0.0; markdown-it 0.0; derived code 15.0 (author file MarkdownHtmlRenderer.cs, still the unchanged 31-line bundle stub); next highest 7.2 (author file BlockState.cs) |
| regions >= 50 tokens (total / spec-mandated / findings) | 1 / 1 / 0 |
| identifier matches (total / spec terms / findings) | 197 / 18 spec terms (107 given library, 53 generic, 5 BCL, 14 reviewed by hand) / 1 review (S-P1-001) |
| control-flow pairs, shape >= 0.5 (D-140) | 2 functions / 2 spec-mandated / 0 |
| constant lists, overlap >= 0.6 (D-140) | 2 lists / 2 spec-mandated / 0 |
| comment shingles shared | 7 / 7 contract wording (the stub) or generic / 0 |
| trace entries checked / discrepancies | 108 entries checked for location and tag; 22 (every fifth) read against the cited spec text / 0 |
| sources log | no forbidden source |
| gate rerun | identical to the author's gate report: 5,501 / 5,516, API unchanged, TRACE 50 errors (the known heading defect) |

| ID | Author location | Category | Class | Trace entry | Action |
|---|---|---|---|---|---|
| S-P1-001 | Markdown/Parsing/BlockParser.cs:90-176, BlockParser.ProcessLine, local variable `tip` | name | review | R-MD-0049 | Coordinator decides. Checker recommends a rename to any name of the author's own choosing (trivial). |
| S-P1-002 | Markdown/Parsing/BlockStarts.cs:1-14, file header and BlockParser.IsSpaceOrTab | structure | spec-mandated | — | none: using lines, namespace and the spec's space-or-tab test |
| S-P1-003 | Markdown/Parsing/CharacterReferences.cs:67-94, CharacterReferences.Resolve | logic | spec-mandated | R-MD-0009 | none: backslash escapes and character references resolved as the spec defines them |
| S-P1-004 | Markdown/Parsing/LinkSyntax.cs:48-101, LinkSyntax.Destination | logic | spec-mandated | R-MD-0033, R-MD-0034 | none: the spec's two forms of link destination |
| S-P1-005 | Markdown/Parsing/HtmlSyntax.cs:14 and :116 | constant | spec-mandated | R-MD-0012, R-MD-0020 | none: the spec's HTML block tag names (spec order) and block types 1-5 |

Notes for the coordinator (author-safe):
- The trace file's format example (an "R-MD-0001" block inside the code fence at the top of rules.md) is the
  template, not an entry; the real R-MD-0001 is correct.
- The other block-parser names (the last matched container, the first unmatched block, the deepest open block,
  "interrupt a paragraph", first non-space position) follow the spec's own wording in its appendix "A parsing
  strategy"; no action.
