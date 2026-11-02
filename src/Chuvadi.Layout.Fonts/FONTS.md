# Chuvadi.Layout.Fonts — font provenance

All fonts are licensed under the SIL Open Font License 1.1. The licence texts ship in the package's
`licenses/` folder and in `Licenses/` in the repository.

| Files | Family | Version | Source |
|---|---|---|---|
| LiberationSans-*, LiberationSerif-*, LiberationMono-* (12) | Liberation Sans / Serif / Mono | 2.1.5 | Built TTFs from arunshivab/chuvadi-pdf `tools/fonts`; upstream github.com/liberationfonts/liberation-fonts (licence from commit 49e1358) |
| Carlito-* (4) | Carlito | 1.104 | github.com/googlefonts/carlito, `fonts/ttf`, commit 3a810ca |
| Caladea-* (4) | Caladea | 1.001 | github.com/huertatipografica/Caladea, `fonts/ttf`, commit 336a529 |
| LiPi-Sans-* (22) | LiPi Sans, LiPi Sans &lt;Script&gt; | from LiPi Sans v1.0 | Static instances of the LiPi Sans variable WOFF2 fonts in arunshivab/chuvadi-pdf `assets/fonts/lipi-sans` (Inter + Noto Sans) |

## How the LiPi Sans static faces were made

The variable fonts were instanced with fontTools 4.66.1. Each face was instanced at wght = 400
(Regular, Italic) and wght = 700 (Bold, Bold Italic), with every other axis at its default:

```
instantiateVariableFont(font, {axis: (w if axis == 'wght' else default)}, updateFontNames=False, optimize=False)
```

The following were then set so that the face identity is correct:

- name IDs 1/2/4/6, with name IDs 16/17/25 and the Macintosh records removed
- OS/2 usWeightClass and fsSelection (BOLD / ITALIC / REGULAR)
- head.macStyle

The STAT table was removed. Glyph outlines, metrics, cmap, GSUB and GPOS are those of the
instanced design.

## Metric compatibility

| Office font | Bundled substitute | Metric-compatible |
|---|---|---|
| Arial | Liberation Sans | yes |
| Times New Roman | Liberation Serif | yes |
| Courier New | Liberation Mono | yes |
| Calibri | Carlito | yes |
| Cambria | Caladea | yes |
| Calibri Light, Aptos, Segoe UI, Georgia, Consolas, … | nearest design | no (reported as `Substitute`) |

The Indic families (Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada,
Malayalam) are script fallbacks, not metric substitutes for any Office font.
