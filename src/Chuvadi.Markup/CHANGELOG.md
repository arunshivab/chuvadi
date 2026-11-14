# Chuvadi.Markup — Changelog

## 1.2.0 (2026-11-15)

- **Clean-room revision 9 (D-106, D-303, D-309, D-331).** The Markdown parser and renderer are the author's
  revision 9, with the similarity review P4 CLEAN for revisions 8 and 9 (`docs/Chuvadi.Markup/similarity/markup-P4.md`)
  and the trace record updated (R-MD-0274 to R-MD-0280). The frozen gate now has 10,423 cases and 353,669 expected
  outputs (1.1.0: 10,388 and 352,479), with the new set `pins` (35 cases) for the rulings below and for D-252. The
  public API is unchanged.
- **Behaviour change (D-309, #95): positions after a partly used tab.** When list or block-quote indentation uses
  only part of a tab at the start of a continuation line, the content after the tab now keeps its true position, and
  the soft break before the line covers the line ending and the tab. In 1.1.0 and 1.0.0 the later spans on that line
  were one or two characters too far right, and a few ended past the end of the input. `SourceSpan` values and
  `data-sourcepos` change; 157 frozen outputs in 9 cases.
- **Behaviour change (D-309): every span lies inside the input.** As a safety net, every source span is clamped to
  the input after the parse. With the fix above no frozen input needs it: the gate and the tests require that the
  clamp never fires on any frozen input under any parse or limit configuration.
- **Behaviour change (D-303): stricter `data:image/` exception for unsafe URLs.** Under the default
  `UnsafeUrlPolicy.Remove`, a `data:` URL is kept only when it starts with `data:image/png`, `gif`, `jpeg` or `webp`
  followed by `;` or `,`. `data:image/pngx;...`, `data:image/png-evil,...` and a bare `data:image/png` are now removed
  (1.1.0 kept any URL that started with one of the four prefixes). No earlier frozen output changes; new pins cover it.
- **Behaviour change (D-330, D-344): the concrete type behind `MarkdownNode.Children`.** `Children` is typed
  `IReadOnlyList<MarkdownNode>`, as before. In 1.1.0 the object behind it was the node's own `List<MarkdownNode>`;
  now it is an internal read-only list, the same object for the life of the node, and it stays a live view of the
  node's children. Code that cast `Children` to `List<MarkdownNode>` (or `IList`, `ICollection<T>`) now gets an
  `InvalidCastException` (or null with `as`); such a cast also let a caller change the children without setting
  `Parent`. Use `AppendChild`, `InsertChild` and `RemoveChild`. LINQ `ToList`, `ToArray` and `Count()` on
  `Children` enumerate instead of using the `ICollection<T>` fast path. No code in the monorepo is affected.
- **Memory and speed (D-303, D-330, D-331, #94).** A node with more than 8,192 children stores them in chunks of
  4,096, so no child array reaches the large object heap, and the parser keeps less data alive during a parse. On the
  measurement machine (2 cores, workstation GC) a parse no longer pays for a gen2 collection at 1, 2 or 4 MiB, and at
  most about 4 bytes per input char are alive at a collection (1.1.0: 66.5 MiB at 1 MiB, 70.1 MiB at 2 MiB):

  | Document | 1.1.0 | 1.2.0 |
  |---|---|---|
  | 1 MiB | 1 gen2 per round, 66.5 MiB alive | 0 gen2, 4.0 MiB |
  | 2 MiB | 1 gen2, 70.1 MiB | 0 gen2, 8.1 MiB |
  | 4 MiB | 1 gen2, 77.5 MiB | 0 gen2, 16.3 MiB |

  Speed against 1.0.0 and 1.1.0 (D-249 rule: no input class more than 25% slower than 1.0.0), parse and parse +
  render, best median of 7 rounds over two alternating runs, M chars/s:

  | Input | Mode | 1.0.0 | 1.1.0 | 1.2.0 | 1.2.0 / 1.0.0 |
  |---|---|---|---|---|---|
  | spec examples (small) | parse | 7.4 | 11.4 | 10.2 | 1.38 |
  | spec examples (small) | parse + render | 7.2 | 8.7 | 8.2 | 1.14 |
  | spec documents (medium) | parse | 45.5 | 42.1 | 44.2 | 0.97 |
  | spec documents (medium) | parse + render | 34.7 | 35.5 | 32.4 | 0.93 |
  | 1 MB x10 | parse | 22.0 | 18.0 | 23.9 | 1.09 |
  | 1 MB x10 | parse + render | 19.5 | 16.0 | 20.5 | 1.05 |
  | 10 MB | parse | 12.4 | 17.8 | 18.0 | 1.45 |
  | 10 MB | parse + render | 11.4 | 15.2 | 15.8 | 1.39 |
  | fuzz | parse | 6.9 | 7.3 | 7.5 | 1.09 |
  | fuzz | parse + render | 12.7 | 13.1 | 12.9 | 1.02 |
  | pathological | parse | 12.6 | 15.3 | 15.8 | 1.25 |
  | pathological | parse + render | 12.2 | 12.7 | 15.4 | 1.26 |

  Every class is within the rule; the slowest is 0.93 of 1.0.0. Allocation per char is unchanged from 1.1.0 on large
  inputs and about 5% higher on the spec examples (163 against 155 bytes per char).
- **Documentation:** the `MarkdownHtmlRenderer` summary states the measured conformance (CommonMark 652/652, GFM
  651/672, extension tests 26/26, footnotes excluded); `UnsafeUrlPolicy.Remove` states the `data:image/` rule exactly.

## 1.1.0 (2026-11-08)

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
  Other spans are as in 1.0.0, including a few that end past the end of the input: after a tab that list or
  block-quote indentation uses only in part, later positions on that line are one or two characters too far right
  (4 frozen cases, 60 outputs). *Corrected in 1.2.0: this line first named the document span as an example, which
  was wrong; the document span is always exact. Fixed in 1.2.0 (#95).*
- **Behaviour change (D-237): heading ids follow GitHub's rules** (`MarkdownHtmlOptions.HeadingIds`): letters,
  digits, combining marks (Mn, Mc, Me), `-` and `_` are kept, a space becomes `-`, everything else is removed, and
  duplicates get `-1`, `-2`. Headings in scripts with vowel signs and viramas keep them: `# தமிழ் மொழி` gives
  `தமிழ்-மொழி` (1.0.0 dropped the marks).
- **Documentation (D-193, R5-0030): `MaxNestingDepth` for Markdown is described exactly.** Container blocks are
  opened only below the limit; a list item may also sit at the limit, unless it would interrupt a paragraph at that
  depth; block quotes stay below it. The markers of a container that is not opened are read as text. Output is the
  same as 1.0.0 on every frozen case.
- **Behaviour change (D-252): a block quote one level below `MaxNestingDepth` can interrupt a paragraph.** With a
  limit L, a block quote that would interrupt a paragraph at depth L - 1 is opened, as the documented rule and the
  spec say (with limit 3, `> p` followed by `> > q` gives a nested block quote). 1.0.0 read its marker as text. With
  the default limit of 512 this needs more than 500 levels of nesting.
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
