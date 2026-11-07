# Chuvadi.Markup — Changelog

## 1.1.0 (unreleased)

- **Clean-room Markdown engine (D-106).** The Markdown parser, the HTML renderer and the Markdown syntax helpers
  are a new implementation written from the CommonMark 0.31.2 and GFM 0.29 specifications only, and checked by an
  independent similarity review (reports in `docs/Chuvadi.Markup/similarity/`, the author's trace record in
  `docs/Chuvadi.Markup/cleanroom/`). The package no longer contains `THIRD-PARTY-NOTICES.txt`: Chuvadi.Markup is MIT
  licensed and contains no third-party code. The public API, defaults and exception types are unchanged. Output is
  unchanged except for the behaviour changes below, which are pinned by a frozen gate of 10,388 cases and 352,479
  expected outputs that now runs as a test (`tests/Chuvadi.Markup.Tests/Gate`).
- **Trimming and NativeAOT (D-217):** the assembly is marked `IsAotCompatible` (trim, AOT and single-file analyzers
  on, warnings as errors) and carries the `IsTrimmable` metadata. No behaviour change.
- **Behaviour change (D-127, R5-03): `MaxOutputChars` is exact.** Output stops at exactly the limit, in every
  renderer and serializer (`MarkdownHtmlRenderer`, `MarkdownWriter`, `HtmlSerializer` and so `HtmlSanitizer`,
  `PlainText.ToHtml`). A caller writing to a `TextWriter` has received exactly `MaxOutputChars` characters when
  `MarkupLimitExceededException` is thrown, and its `Actual` is always the limit plus one. Before, `Actual` was the
  length including the whole write that crossed the limit, a `TextWriter` had received only the output before that
  write, and `MarkdownWriter` reported the full text length and wrote nothing. Output below the limit is unchanged.
- **Behaviour change (D-130, R5-01): `MaxNodes` for Markdown counts the nodes of the resulting tree**, not the
  document node and not link reference definitions, and throws as soon as the count would pass the limit (`Actual` is
  the limit plus one). 1.0.0 counted the parser's internal nodes, so documents close to the limit can now succeed
  where 1.0.0 threw, or the reverse.
- **Behaviour change (D-130, R5-02): `MaxParserWorkPerChar` counts the new parser's steps.** The budget is unchanged
  (`MaxParserWorkPerChar` x (input length + 1024) + 1,000,000, the exception's `Limit`); what counts as one step is
  implementation-defined, so an input close to the budget can throw at a different point and `Actual` differs.
- **Behaviour change (D-141): U+000B, U+000C, U+00A0 and U+2028 are not blank.** A paragraph made only of them is
  kept (1.0.0 dropped it), and an ATX heading keeps a leading or trailing U+000B or U+000C, as the spec says. A list
  item whose content is only U+000B keeps it (`- `, U+000B).
- **Behaviour change (D-143): paragraph and setext heading content is stripped of spaces and tabs only**, so a
  leading or trailing U+000C stays.
- **Behaviour change (D-157): an extended `www.` autolink starts after U+000B** as well as after U+000C (GFM 0.29).
- **Behaviour change (D-157): a link label made only of characters other than spaces, tabs and line endings is
  valid** (for example U+00A0 alone); 1.0.0 did not treat it as a label.
- **Behaviour change (D-174): link label matching follows the CommonMark spec.**
  `LinkReferenceDefinition.NormalizeLabel` trims and collapses only spaces, tabs and line endings; U+000B, U+000C and
  U+00A0 now stay in the label as they are. 1.0.0 treated U+000C like a space, so a definition labelled "a", U+000C,
  "b" also matched `[a b]`. This also changes the keys of `MarkdownDocument.LinkDefinitions`.
- **Behaviour change (D-232, D-242): extended autolinks need a valid domain (GFM 0.29).**
  - No link when `www.` or the scheme is followed by punctuation or whitespace instead of a domain: `www.&`,
    `www.>`, `www.\`, `www.%`, `www.` followed by U+00A0, `http://` followed by U+000B. U+2028 there still links,
    as in 1.0.0.
  - No link when the last two segments of the domain contain `_`, checked to its last character: `http://a_`
    (1.0.0 linked `http://a` and left `_` as text), `www.a_`. Periods at the very end of the domain do not start a
    segment.
  - No link when the domain starts with a period: `www..a`, `www..`, and the email form `b@.c`.
  - http://, https:// and ftp:// links need no period in the host, so `http://-` and `HTTP://-` link like
    `http://intranet` (for www. links the period after `www` counts).
- **Behaviour change (D-237): no extended http(s) or ftp link straight after an ASCII letter or digit**
  (`0http://a.b` is text; 1.0.0 linked `http://a.b`). After any other character, including non-ASCII letters,
  `<`, `"` and `-`, it links as before.
- **Behaviour change (D-237): extended email autolinks follow GFM 0.29.** A domain label may start with `-` or `_`
  (`a@b._x` links), and an empty xmpp resource is not part of the link (`xmpp:a@b.f/` links `xmpp:a@b.f` and leaves
  `/` as text; 1.0.0 did not link it).
- **Behaviour change (D-237): a task marker may hold any whitespace character** (`- [\t] x` is a task item; 1.0.0
  left `[\t] x` as text). A task marker with nothing after it is unchanged: `- [ ] ` and `1. [x] ` are task items
  with a checkbox and no text, `- [ ]` and `- [\t] ` stay text.
- **Behaviour change (D-232): U+000B and U+000C at the end of a table row are cell content.** A header cell made
  only of them counts as a cell, so `a|`, U+000B, then `-|-` is a two-column table (1.0.0: a paragraph), and a body
  cell made only of U+000C keeps it. In a delimiter row they are whitespace, as in 1.0.0.
- **Behaviour change (D-237): code spans after an unmatched backtick string.** A backtick string that never closes
  no longer stops later code spans: ``` ``;`>`-`>` ``` has two code spans (1.0.0 found one).
- **Behaviour change (D-237): U+000B and U+000C are not whitespace inside an HTML tag** (as D-141), so `<r`, U+000B,
  `>` is text, not an HTML block.
- **Behaviour change (D-237): list looseness.** A blank line between an item that ends in a thematic break and the
  next item makes the list loose (`0. ___`, blank line, `0.`; 1.0.0 said tight). A blank line inside an unclosed
  fence still does not, as in 1.0.0.
- **Behaviour change (D-237): source span of a link reference definition.** The span covers the whole line ending
  (CR LF included) and, with no line ending, ends at the end of the input (`[f]:r` gives 0+5; 1.0.0 gave 0+6).
  Other spans are as in 1.0.0, including some that end past the end of the input (the document span of some
  inputs, for example); clamping them is a possible later change.
- **Behaviour change (D-237): heading ids follow GitHub's rules** (`MarkdownHtmlOptions.HeadingIds`): letters,
  digits, combining marks (Mn, Mc, Me), `-` and `_` are kept, a space becomes `-`, everything else is removed, and
  duplicates get `-1`, `-2`. Headings in scripts with vowel signs and viramas keep them: `# தமிழ் மொழி` gives
  `தமிழ்-மொழி` (1.0.0 dropped the marks).
- **Documentation (D-193, R5-0030): `MaxNestingDepth` for Markdown is described exactly.** Container blocks are
  opened only below the limit; a list item may also sit at the limit, unless it would interrupt a paragraph at that
  depth; block quotes stay below it. The markers of a container that is not opened are read as text. Output is the
  same as 1.0.0 on every frozen case.
- **Documentation (D-199):** `MaxNestingDepth` notes the cost of very deep block quotes (possible only with a limit
  far above the default): under a microsecond per `>` marker to parse.

## 1.0.0 (2026-11-03)

First stable release.

- **Third-party notices (D-106)**: the package now contains `THIRD-PARTY-NOTICES.txt` with the cmark and cmark-gfm licence (BSD 2-Clause, and MIT for the houdini href escaper), because part of the Markdown parser and HTML renderer follows their code; the README says so. No code or behaviour change.

- **Sanitizer policy without presets (D-83 to D-86)**: `new SanitizerPolicy()` is now the safest configuration, and `HtmlSanitizer.Sanitize` without a policy uses it. The policy has no preset methods (the named presets of the previews are removed); capabilities are plain switches: `Links` (default `Inert`), `AllowRelativeLinks`, `AllowFormControls` (now also covers `label`, `progress` and `meter`), new `AllowMedia` (audio, video, source, track, picture), `AllowSvg` and `AllowMathML` (now with built-in safe subsets), new `AllowSvgDataImages`, new `AllowEmbeddedFonts` (`@font-face`; no longer in the default `AllowedAtRules`), `StyleSheets`, `AllowStyleAttribute` (default true, every declaration filtered) and `ResourceResolver`; `Limits` defaults to `MarkupLimits.Strict()`. `LinkHandling.Keep` is renamed `LinkHandling.Active`.
- **Default change (D-90): plain-text links are inert by default.** `TextToHtmlOptions.Links` now defaults to `LinkHandling.Inert` (it was `Keep`, now `Active`), the same safe default as `SanitizerPolicy.Links`; detected links keep their destination in `data-chuvadi-href` and `title`. Applications choose separately for HTML (`SanitizerPolicy.Links`) and plain text (`TextToHtmlOptions.Links`), with the same `LinkHandling` values (`Inert`, `Active`, `Rewrite`). Set `Links = LinkHandling.Active` to get the previous output. With the switches set as the preview presets set them, output is byte-identical to those presets.

Changes since preview.2 come from the independent security review (2026-10-28):
no script execution, network egress or mXSS breakout was found; the hardening items below were fixed.

- **Markdown (F2, F5)**: emphasis resolution is linear for contiguous delimiter runs (a million-character `*`, `_` or `~` run each side now takes well under a second; it was quadratic). `openers_bottom` is keyed by delimiter character x closer can-open x length mod 3. Markdown inline parsing now counts work against `MaxParserWorkPerChar` (throwing `MarkupLimitExceededException`) and polls cancellation inside the opener walk.
- **Sanitizer (F1)**: `@font-face` families are moved into the content's namespace (the id prefix, or `ScopeClass-` / `chuvadi-` when it is empty), together with every reference in the same sheets, style attributes and SVG `font-family` attributes, so content fonts cannot restyle host UI. The default policy drops `@font-face` unless `AllowEmbeddedFonts` is set.
- **Sanitizer (F3)**: `@keyframes` and `@counter-style` names are namespaced the same way (with `animation`, `animation-name`, `list-style`, `list-style-type`, `counter()`/`counters()`, `system` and `fallback` references). `@page` and `@layer` are no longer in the default `AllowedAtRules`. `@container` contents are scoped like other rules. Anchor-positioning and view-transition names (`anchor-name`, `position-anchor`, `anchor-scope`, `position-try*`, `position-visibility`, `view-transition-name`, `view-transition-class`, `anchor()`, `anchor-size()`) are denied by default.
- **Sanitizer (F4)**: selectors that start with a combinator, or that put `~` / `+` after the wrapper itself (including `html`, `body`, `:root` and nested `& ~ x` under them), are dropped.

## 1.0.0-preview.2 (not released separately; included in 1.0.0)

- **HTML**: `<selectedcontent>` mirroring of the customizable `<select>`: the selected option's contents are cloned into the select's `<selectedcontent>` while parsing (counted against `MaxNodes`). The html5lib tree-construction suite now passes in full.
- **Sanitizer**: style rules with an empty selector or an empty selector-list item are removed instead of being left unscoped (found by fuzzing; browsers drop such rules, so it was not exploitable). Relative links that would reveal a scheme once control, whitespace and format characters are removed (such as `\u0099javascript:`) are removed as defence in depth.
- **Robustness**: time-boxed random-input fuzz tests for every entry point; timing-sensitive tests run in their own serial collection; globalization analyzers enforced (no culture-sensitive string operations).

## 1.0.0-preview.1 (not released separately; included in 1.0.0)

First preview.

- **Markdown**: `MarkdownParser` (CommonMark 0.31.2, GFM tables, task lists, strikethrough, extended autolinks), public AST (`MarkdownDocument`, block and inline node types, `MarkdownVisitor`, source spans and positions, link reference definitions), `MarkdownHtmlRenderer` / `MarkdownConverter` (`RawHtmlMode` Escape/Strip/Passthrough, tag filter, unsafe URL policy, soft-break modes, heading ids, code language class prefix, task list and table alignment styles, XHTML void elements, `data-sourcepos`), `MarkdownWriter` (AST to Markdown, configurable bullets, emphasis characters, fences, heading style).
- **HTML**: `HtmlTokenizer` (WHATWG, all states, character references, parse errors with positions), `HtmlParser` (document and fragment parsing, string/bytes/span/stream input, charset sniffing via `HtmlEncoding`), DOM (`HtmlDocument`, `HtmlDocumentFragment`, `HtmlElement`, `HtmlText`, `HtmlComment`, `HtmlDocumentType`, namespaces, template contents), `HtmlSerializer`.
- **CSS**: `CssTokenizer`, `CssParser` (style sheets, declaration lists, nested rules), `CssSerializer` (safe inside `<style>` and attributes).
- **Sanitizer**: `HtmlSanitizer`, `SanitizerPolicy` with every knob public; scoped style sheets, id prefixing, inert / rewritten links, `IResourceResolver` for `cid:` and relative URLs, `BlockedResources` and `Actions` reports, title extraction, body style transfer, blocked CSS images replaced with `none` so colours survive, fixed-point stability passes with a plain-text fallback.
- **Plain text**: `PlainText.ToHtml` with `TextLayout` PreWrap / Preformatted / Paragraphs, tab expansion, autolinks (live, inert, rewritten), quote levels, `format=flowed` and `DelSp`.
- **Limits**: `MarkupLimits` (input, nodes, nesting depth, parser work, attributes, output, data URIs, table size, link definitions, recorded errors) with a `Strict()` preset; `MarkupLimitExceededException`.
