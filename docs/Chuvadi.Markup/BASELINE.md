# Chuvadi.Markup — Baseline Invariants

Invariants the package must always satisfy. Changes to these are deliberate, documented decisions.

- **M01 — Zero dependencies.** `src/Chuvadi.Markup` declares no `<PackageReference>` and no
  `<ProjectReference>`, and does not use `shared/Chuvadi.Internal`. Only the BCL (including
  `CodePagesEncodingProvider` for legacy mail charsets). The CI grep over `src/` and `shared/` enforces
  the no-third-party rule.
- **M02 — Untrusted input is the normal case.** Every public entry point takes a `CancellationToken`
  and honours `MarkupLimits`. No input can cause a stack overflow (all tree walks are iterative; nesting
  past `MaxNestingDepth` is flattened, not rejected), super-linear time (parser work budget, linear
  delimiter algorithms, bounded active formatting list) or unbounded allocation (input, node, output,
  attribute, data-URI, table and definition caps). Malformed input never throws; only
  `ArgumentNullException`, `ArgumentOutOfRangeException` (invalid limits), `MarkupLimitExceededException`
  and `OperationCanceledException` are documented.
- **M03 — Parse like the reference.** Markdown parses and renders exactly like cmark / cmark-gfm
  (CommonMark spec 0.31.2: 652/652; GFM extensions). HTML parses into the DOM a browser builds
  (WHATWG HTML; html5lib tests). Deviations are listed in `DESIGN.md`, each with a reason.
- **M04 — Sanitizer is allowlist-only and network-free.** Nothing that can fetch, navigate or run is
  emitted unless the host's `IResourceResolver` supplies it, and even resolver output is never a
  script-capable URL. The sanitized output is a parser fixed point (re-parsing and re-sanitizing
  changes nothing); otherwise the plain-text fallback is returned.
- **M05 — Both Reader requirement profiles hold.** Reader requirements §9 (strip list, Anjal and Reader
  profiles) and §10 (Markdown feature list) are covered by `SanitizerPolicy.Anjal()`,
  `SanitizerPolicy.Reader()` and `MarkdownOptions.Gfm()`; see `SECURITY-MODEL.md` for the mapping.
- **M06 — Complete option coverage.** Every renderer, writer, sanitizer and text option is public; presets
  are only starting points (`Anjal()`, `Reader()`, `Empty()`, `CommonMark()`, `Gfm()`, `Strict()`), each
  returning a fresh mutable object.
- **M07 — Portable.** Builds and tests on Linux and Windows; no platform-specific APIs.

## Layer status

- **1.0.0 (this layer; previews 1 and 2 before it)** — Markdown parser + AST + HTML renderer + Markdown writer; WHATWG HTML
  tokenizer, tree builder, DOM, serializer, charset sniffing; CSS syntax parser and serializer;
  allowlist sanitizer with Anjal / Reader policies; plain text to HTML.
- **1.1 (planned)** — CSS cascade / computed style for Chuvadi.Layout (selector matching, specificity,
  inheritance) over the existing CSS parser; Markdown footnotes if a consumer asks.

## Build & test conventions (inherited from the monorepo)

- `TreatWarningsAsErrors=true`; the build must be warning-free.
- Tests: xUnit (`coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`),
  plain `Assert`. The shipped library never references these.
- Conformance data is vendored under `tests/Chuvadi.Markup.Tests/Conformance` with its licences:
  CommonMark spec 0.31.2 (CC BY-SA 4.0), GFM spec and extension tests (cmark-gfm, BSD-2 / CC BY-SA 4.0),
  html5lib-tests (MIT). It is test data only and never ships in the package.
- The `samples/Chuvadi.Markup.ManualTests` console exposes verification groups (consumer scenarios for
  Reader and Anjal) that `tests/Chuvadi.Markup.Tests` drives as `[Fact]`s, and which also run end-to-end
  in CI.
- `dotnet run --project samples/Chuvadi.Markup.ManualTests -- --preview <folder>` additionally writes HTML
  pages (Markdown, a newsletter under both policies, plain text, windows-1252 mail) behind a no-network
  Content-Security-Policy, for visual verification in Edge / WebView2.
