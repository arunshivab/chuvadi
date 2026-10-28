# Chuvadi.Markup — Security model

## Threat model

Input is hostile: mail from anyone (Anjal), documents from anywhere (Reader). The attacker wants to run
script, make the viewer fetch something (tracking, IP disclosure, CSRF), capture credentials, overlay
the host UI, or make the server/app hang or run out of memory. The sanitizer's output is placed into the
host's own page (Anjal: server-side rendered; Reader: WebView2), so a single bypass is a host compromise.

## How the sanitizer works

1. **Parse like a browser.** The input (string or bytes, with charset sniffing) is parsed with the
   WHATWG tree builder, so the sanitizer sees exactly the tree a browser would build — no regexes.
2. **Allowlist.** Each element is kept, unwrapped (children kept) or dropped (with children) per
   `SanitizerPolicy`; unknown elements are unwrapped (`DefaultElementAction`). Attributes are kept only
   if allowed globally or for that element; every `on*` handler is always removed. SVG and MathML have
   their own allowlists and are off unless enabled (Reader enables safe subsets).
3. **URLs.** Links are scheme-checked (`http`, `https`, `mailto` by default; relative and `#fragment`
   per policy) and then kept, made inert (`data-chuvadi-href` + `title`) or rewritten through a template.
   A relative URL that would reveal a scheme once control, whitespace and format characters are removed
   (for example `\u0099javascript:`, which browsers treat as a relative path) is removed as well.
   Resource URLs (images, media, fonts, CSS `url()`, `image-set()`, SVG `href`) are allowed only as
   `data:` URIs of allowed MIME types and size, or when the host's `IResourceResolver` maps them
   (`cid:` parts, relative files). Everything else is removed and listed in `BlockedResources`.
4. **CSS.** `style` attributes and `<style>` sheets are parsed with the CSS Syntax parser and rebuilt
   from tokens: denied properties (`behavior`, `-moz-binding`, ...), denied functions (`expression()`,
   `element()`, `paint()`), fixed/sticky positioning, `@import` and other non-allowlisted at-rules,
   `@font-face` without a permitted source and every blocked URL are removed. Where a blocked image is a
   whole value item it becomes `none`, so colours survive. Selectors are scoped under the wrapper class
   (`.chuvadi-content`), `html`/`body`/`:root` map to the wrapper, and `#id` selectors follow the id
   prefix. A selector that starts with a combinator, or that puts `~`/`+` after the wrapper itself
   (`html ~ x`, `.chuvadi-content + x`, or a nested `& ~ x` under a wrapper rule), is dropped: it would
   match the wrapper's siblings in the host page. `@container` contents are scoped like any other rules.
   CSS names that are global to the document are moved into the content's namespace: every
   `@font-face` family, `@keyframes` name and `@counter-style` name a sheet declares gets the id prefix,
   and so do all references to them (`font-family`, `font`, `animation(-name)`, `list-style(-type)`,
   `counter()`/`counters()`, SVG `font-family`). Content therefore cannot replace a font, animation or
   list style the host uses. `@page` and `@layer` are not allowed, and the anchor-positioning and
   view-transition properties (`anchor-name`, `position-anchor`, `anchor()`, `view-transition-name`, ...)
   are removed. `Anjal()` keeps no web fonts at all unless `font-face` is added back to `AllowedAtRules`.
5. **Ids.** `id`, `for`, `headers`, `aria-*` id references, `usemap` and fragment links are prefixed
   (`user-content-`) so content cannot clobber host ids or globals.
6. **Fixed point.** The output is serialized, re-parsed as a fragment in body context and sanitized
   again until it no longer changes (`MaxStabilityPasses`, default 3). This closes mutation-XSS
   (namespace confusion, `<noscript>`, `<template>`, `<select>` and comment edge cases). If it never
   stabilises, the escaped plain text is returned (`UnstableOutputHandling.PlainText`).
7. **Limits.** All parsing runs under `MarkupLimits` (`Strict()` in `Anjal()`) and the caller's
   `CancellationToken`.

The test suite runs ~170 XSS, mutation-XSS and exfiltration vectors, seeded structural fuzzing and
time-boxed random-byte fuzzing through both policies, and checks every output with an independent
invariant checker that re-parses with scripting on and off.

## Reader requirements §9 — mapping

| Requirement | Anjal() | Reader() |
|---|---|---|
| `<script>`, all `on*` handlers | removed | removed |
| `javascript:`, `vbscript:`, `data:` URLs (except `data:image/*`) | removed | removed |
| `iframe`, `frame`, `frameset`, `object`, `embed`, `applet` | dropped | dropped |
| `link`, `meta` (incl. `http-equiv` refresh/CSP), `base` | dropped | dropped |
| Remote `img`, CSS `@import`, `url(http…)`, remote fonts | removed, reported | removed, reported |
| `<svg>` with script or external references | SVG dropped | safe subset, local `#` refs only |
| `expression()`, `behavior`, `-moz-binding`, external `url()` in `style` | removed | removed |
| Conditional comments | removed (all comments) | removed (all comments) |
| `<form>`, form controls, `<input>`, `<button>` | removed (form unwrapped, controls dropped, `button` unwrapped to its text) | form unwrapped (cannot submit); controls kept inert |
| Anchor navigation | inert (`data-chuvadi-href`), `#fragment` kept | kept for host interception, `rel="noopener noreferrer"` |
| Images | only `data:image/*` (no SVG), or resolver (`cid:`) | `data:image/*` incl. SVG-as-image, or resolver |
| Relative links | removed | kept (host resolves) |
| Network | none | none without the host's resolver |

## Host guidance

- Serve the page with a Content-Security-Policy that also forbids network access, for example
  `default-src 'none'; img-src data:; media-src data:; font-src data:; style-src 'unsafe-inline'`
  (add the host's own origins as needed). The library's preview pages use exactly this policy.
- Place the output inside an element that limits overflow (`contain: content; overflow: auto`) so
  sanitized but large content cannot cover the host UI.
- Handle clicks on `[data-chuvadi-href]` in the host (confirm, then open externally).
- Treat `BlockedResources` as the list for a "show remote content" prompt; to honour it, re-sanitize
  with a resolver that returns the fetched content as `data:` URIs (fetched by the host, under its own
  rules), never by loosening the policy.
- Keep the policy objects per request or immutable after setup; `Clone()` before changing a shared one.
