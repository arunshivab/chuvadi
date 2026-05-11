# LiPi Sans v1.0

A unified multilingual type system for the Indian web.
Self-hosted. Fully offline. No CDN. No external license servers.

Built on Inter (Latin) + Noto Sans (10 Indian scripts), assembled into
a single `font-family: "LiPi Sans"` that works across all scripts
automatically via `unicode-range` routing.

---

## What's in this folder

```
lipi-sans/
├── lipi-sans.css              ← drop-in stylesheet (the only file you need to link)
├── fonts/
│   ├── LiPi-Sans-Latin.woff2          344 KB  — English + European Latin
│   ├── LiPi-Sans-Latin-Italic.woff2   380 KB  — Italic variant
│   ├── LiPi-Sans-Devanagari.woff2     260 KB  — Hindi, Marathi, Sanskrit, Nepali
│   ├── LiPi-Sans-Bengali.woff2        239 KB  — Bengali, Assamese
│   ├── LiPi-Sans-Tamil.woff2          155 KB  — Tamil
│   ├── LiPi-Sans-Telugu.woff2         292 KB  — Telugu
│   ├── LiPi-Sans-Malayalam.woff2      214 KB  — Malayalam
│   ├── LiPi-Sans-Kannada.woff2        227 KB  — Kannada
│   ├── LiPi-Sans-Gujarati.woff2       244 KB  — Gujarati
│   ├── LiPi-Sans-Gurmukhi.woff2       129 KB  — Punjabi
│   └── LiPi-Sans-Odia.woff2           227 KB  — Odia
├── LICENSE.txt                ← SIL OFL 1.1
└── README.md                  ← this file
```

**All fonts are variable fonts** — one file covers the full weight axis
(100 Thin → 900 Black). No separate Bold or SemiBold files needed.

---

## Install

Copy the `lipi-sans/` folder into your project's static assets directory.

```
your-project/
└── public/          (or wwwroot/, static/, assets/ — wherever you serve static files)
    └── lipi-sans/
        ├── lipi-sans.css
        └── fonts/
            └── *.woff2
```

---

## Use

### Blazor / ASP.NET (`App.razor` or `_Host.cshtml`)

```html
<head>
  <!-- Preload the Latin file — it's needed on every page load -->
  <link rel="preload"
        href="/lipi-sans/fonts/LiPi-Sans-Latin.woff2"
        as="font" type="font/woff2" crossorigin>

  <!-- Load the full stylesheet (all @font-face declarations) -->
  <link rel="stylesheet" href="/lipi-sans/lipi-sans.css">
</head>
```

Then in your baseline CSS:
```css
body {
  font-family: var(--lipi-sans);
  /* or: font-family: "LiPi Sans", system-ui, sans-serif; */
}
```

### React / Next.js

```jsx
// In _app.jsx or layout.tsx
import '/public/lipi-sans/lipi-sans.css';
```

```css
body {
  font-family: var(--lipi-sans);
}
```

### Plain HTML

```html
<head>
  <link rel="preload" href="lipi-sans/fonts/LiPi-Sans-Latin.woff2"
        as="font" type="font/woff2" crossorigin>
  <link rel="stylesheet" href="lipi-sans/lipi-sans.css">
  <style>
    body { font-family: var(--lipi-sans); }
  </style>
</head>
```

---

## Scripts supported

| Script     | Languages                          | File                         | Size   |
|------------|------------------------------------|------------------------------|--------|
| Latin      | English + 200 European languages   | LiPi-Sans-Latin.woff2        | 344 KB |
| Devanagari | Hindi, Marathi, Sanskrit, Nepali   | LiPi-Sans-Devanagari.woff2   | 260 KB |
| Bengali    | Bengali, Assamese                  | LiPi-Sans-Bengali.woff2      | 239 KB |
| Tamil      | Tamil                              | LiPi-Sans-Tamil.woff2        | 155 KB |
| Telugu     | Telugu                             | LiPi-Sans-Telugu.woff2       | 292 KB |
| Malayalam  | Malayalam                          | LiPi-Sans-Malayalam.woff2    | 214 KB |
| Kannada    | Kannada                            | LiPi-Sans-Kannada.woff2      | 227 KB |
| Gujarati   | Gujarati                           | LiPi-Sans-Gujarati.woff2     | 244 KB |
| Gurmukhi   | Punjabi                            | LiPi-Sans-Gurmukhi.woff2     | 129 KB |
| Odia       | Odia                               | LiPi-Sans-Odia.woff2         | 227 KB |

**The browser only downloads the script files it needs.** A pure-English
page downloads only the 344 KB Latin file. A Hindi + English page downloads
Latin + Devanagari (~604 KB). A trilingual Hindi + Tamil + Telugu page
downloads three files (~707 KB). The `unicode-range` declarations in
`lipi-sans.css` handle this routing automatically.

---

## Weights

All weights 100–900 are valid — it's a variable font:

```css
.thin     { font-weight: 100; }
.light    { font-weight: 300; }
.regular  { font-weight: 400; }
.medium   { font-weight: 500; }
.semibold { font-weight: 600; }
.bold     { font-weight: 700; }
.black    { font-weight: 900; }

/* Even fractional weights work */
.precise  { font-weight: 638; }
```

Or use the included shorthand classes: `.lipi-regular`, `.lipi-medium`,
`.lipi-semibold`, `.lipi-bold`, etc.

---

## Utility classes

### `.lipi-num` — tabular figures + slashed zero

For numeric clinical data, billing amounts, IDs, dates.
Ensures all digits have identical width so numbers align vertically in columns.
Enables slashed zero (0̸) to distinguish from letter O.

```html
<td class="lipi-num">PT-2026-004821</td>
<td class="lipi-num">₹ 14,500.00</td>
<td class="lipi-num">120/80 mmHg</td>
```

### `.lipi-display` — display heading

Tight leading + negative tracking for headings 16px+.

```html
<h1 class="lipi-display">OPD Encounter</h1>
```

### `.lipi-label` — spaced uppercase label

For table headers, section dividers, badge text.

```html
<th class="lipi-label">Patient Name</th>
```

### `.lipi-prose` — relaxed leading

For clinical notes, discharge summaries, longer text blocks.

```html
<p class="lipi-prose">Patient presented with...</p>
```

---

## CSS custom properties

The stylesheet exposes these on `:root`:

```css
--lipi-sans              /* the full font stack */
--lipi-leading-body      /* 1.45 — body text line height */
--lipi-leading-tight     /* 1.1  — heading line height */
--lipi-leading-relaxed   /* 1.65 — prose line height */
--lipi-tracking-display  /* -0.022em — heading letter spacing */
--lipi-tracking-body     /* -0.005em — body letter spacing */
--lipi-tracking-caps     /* 0.07em   — uppercase label spacing */
```

---

## OpenType features enabled by default

Applied globally on `html, body`:

| Feature | Name | Effect |
|---------|------|--------|
| `ss01`  | Open digits | Disambiguates `1` from `l` |
| `cv11`  | Straight-leg R | Cleaner for clinical codes |
| `calt`  | Contextual alternates | Smart glyph substitution |
| `kern`  | Kerning | Correct letter spacing |
| `liga`  | Common ligatures | fi, fl, etc. |

The `.lipi-num` class additionally enables `tnum` (tabular figures) and
`zero` (slashed zero) for numeric data contexts.

---

## Provenance

LiPi Sans is built on two OFL-licensed families:
- **Inter** (Latin) — © The Inter Project Authors
- **Noto Sans** (all Indian scripts) — © Google LLC

Distributed under SIL OFL 1.1. See `LICENSE.txt`.

---

## Upgrade path

When a custom-designed LiPi typeface is commissioned in a future version,
replace the `fonts/*.woff2` files with the new binaries. The CSS, the
`unicode-range` routing, and the utility classes all carry over unchanged.

---

*LiPi Sans v1.0 — Built for LiPi HIS and companion applications.*
