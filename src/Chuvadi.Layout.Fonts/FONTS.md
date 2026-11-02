# Chuvadi.Layout.Fonts — font provenance

All fonts are licensed under the SIL Open Font License 1.1. The licence texts ship in the package's
`licenses/` folder and in `Licenses/` in the repository.

| Files | Family | Version | Source |
|---|---|---|---|
| LiberationSans-*, LiberationSerif-*, LiberationMono-* (12) | Liberation Sans / Serif / Mono | 2.1.5 | Built TTFs from arunshivab/chuvadi-pdf `tools/fonts`; upstream github.com/liberationfonts/liberation-fonts (licence from commit 49e1358) |
| Carlito-* (4) | Carlito | 1.104 | github.com/googlefonts/carlito, `fonts/ttf`, commit 3a810ca |
| Caladea-* (4) | Caladea | 1.001 | github.com/huertatipografica/Caladea, `fonts/ttf`, commit 336a529 |

The LiPi Sans files (22) moved unchanged to Chuvadi.Fonts (D-113); their provenance is in that package's FONTS.md.

## Metric compatibility

| Office font | Bundled substitute | Metric-compatible |
|---|---|---|
| Arial | Liberation Sans | yes |
| Times New Roman | Liberation Serif | yes |
| Courier New | Liberation Mono | yes |
| Calibri | Carlito | yes |
| Cambria | Caladea | yes |
| Calibri Light, Aptos, Segoe UI, Georgia, Consolas, … | nearest design | no (reported as `Substitute`) |

The LiPi Sans Indic families (Chuvadi.Fonts) are script fallbacks, not metric substitutes for any Office font.
