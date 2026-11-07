# Sources log

Every document you read for this work, one line each: date, what, URL or bundle path, why.
The bundled files under `specs/` are the primary sources. Fetching anything else is rarely needed; if you do,
log it here first. `gate trace` fails on any source that names another Markdown implementation and warns on
any host outside the permitted list (spec.commonmark.org, github.github.com/gfm, the WHATWG HTML and URL
standards, unicode.org, learn.microsoft.com for .NET API documentation, RFCs).

| Date | What | Where | Why |
|---|---|---|---|
| (start) | CommonMark Spec 0.31.2 | specs/commonmark/spec.txt | primary |
| (start) | GitHub Flavored Markdown Spec 0.29 | specs/gfm/gfm-spec.txt | primary (extensions) |
| (start) | GFM extensions test file | specs/gfm/extensions.txt | conformance examples |
| 2026-11-04 | Bundle brief and readme | BRIEF.md, README.md | rules and gate description |
| 2026-11-04 | Given library (contract, read only) | src/Chuvadi.Markup/ (AST, options, limits, internal helpers, parser entry, writer, plain text) | the AST and helpers the parser fills and calls |
| 2026-11-04 | Gate source and frozen data | gate/Chuvadi.Markdown.Gate/*.cs, gate/data/ | dump format, checks, trace rules; frozen outputs as the black-box oracle |
| 2026-11-04 | .NET 10 SDK (tool, not a source; approved by the coordinator) | dotnet-install script from builds.dotnet.microsoft.com | build and run the gate |
