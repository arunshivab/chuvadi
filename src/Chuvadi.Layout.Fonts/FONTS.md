# Chuvadi.Layout.Fonts — font provenance

All fonts are licensed under the SIL Open Font License 1.1. The licence texts ship in the package's
`licenses/` folder and in `Licenses/` in the repository.

| Files | Family | Version | Source |
|---|---|---|---|
| LiberationSans-*, LiberationSerif-*, LiberationMono-* (12) | Liberation Sans / Serif / Mono | 2.1.5 | Built TTFs from arunshivab/chuvadi-pdf `tools/fonts`; upstream github.com/liberationfonts/liberation-fonts (licence from commit 49e1358) |
| Carlito-* (4) | Carlito | 1.104 | github.com/googlefonts/carlito, `fonts/ttf`, commit 3a810ca |
| Caladea-* (4) | Caladea | 1.001 | github.com/huertatipografica/Caladea, `fonts/ttf`, commit 336a529 |
| Gelasio-Regular, -Bold, -Italic, -BoldItalic (4) | Gelasio | 1.008 | github.com/SorkinType/Gelasio, `fonts/ttf`, commit 7ab20e7 (licence `OFL.txt` as Gelasio-OFL.txt) |
| selawk, selawkb, selawkl, selawksl, selawksb (5) | Selawik, Selawik Light / Semilight / Semibold | 1.01 | github.com/microsoft/Selawik, release 1.01, `Selawik_Release.zip` (licence `LICENSE.txt` as Selawik-OFL.txt; Reserved Font Name "Selawik", shipped unmodified) |

The LiPi Sans files (22) moved unchanged to Chuvadi.Fonts (D-113); their provenance is in that package's FONTS.md.

## Metric compatibility

| Office font | Bundled substitute | Metric-compatible |
|---|---|---|
| Arial | Liberation Sans | yes |
| Times New Roman | Liberation Serif | yes |
| Courier New | Liberation Mono | yes |
| Calibri | Carlito | yes |
| Cambria | Caladea | yes |
| Georgia | Gelasio | stated by its designer; not yet measured, so reported as `Substitute` |
| Segoe UI (Light, Semilight, Semibold) | Selawik (Light, Semilight, Semibold) | no (reported as `Substitute`) |
| Calibri Light, Aptos, Consolas, … | nearest design | no (reported as `Substitute`) |

`ManualTests --compare-metrics` compares the installed fonts with these substitutes per codepoint (run on Windows).
Measured here: Noto Sans Tamil 2.004 and Noto Sans Devanagari 2.006 against LiPi Sans Tamil / Devanagari (Chuvadi.Fonts),
Regular and Bold: identical advance widths for every Noto codepoint (three Devanagari Bold widths differ by 1/1000 em)
and identical vertical metrics, so they are metric-compatible substitutes.

The LiPi Sans Indic families (Chuvadi.Fonts) are script fallbacks, not metric substitutes for any Office font.

## Files

29 files, 8,264,644 bytes.

| File | Bytes | SHA-256 |
|---|---|---|
| Caladea-Bold.ttf | 84,492 | `ae3cb2dcbc925809dd29d2a44e9802211cab66be541bacbfc9c08c74b27c3742` |
| Caladea-BoldItalic.ttf | 83,356 | `ccabaa7b7e2fdf253d2b1a5fa699dd8a3df8d835a9eb285ad82631a677eb76c0` |
| Caladea-Italic.ttf | 83,780 | `4359a8e24f748b6447b1ff6d7a174febe70961d29f8bb8634b56dacd740a3deb` |
| Caladea-Regular.ttf | 81,600 | `f1e899278b7b4491aba5b6a8253c4b04c050cc59b21865be5c37559a775153cd` |
| Carlito-Bold.ttf | 682,468 | `bb5d20f79b82599ec72983597437373a80f2d2085fa91fc144fd74e876a594db` |
| Carlito-BoldItalic.ttf | 808,508 | `b32928186c119599e03ca6a1ffc680fdcb7fac95772f4b95d989cf6cd3861517` |
| Carlito-Italic.ttf | 615,236 | `0b019225e58d702bfedcbd35c21696769f8ee115cb6343f84c2f240312450d1c` |
| Carlito-Regular.ttf | 628,032 | `f6418f708baede9789daef5d458c0f53d2a888af9820e8062934e504fedc6595` |
| Gelasio-Bold.ttf | 148,380 | `e0ef3addf1acf35f5c6aef2be00d0a2c01363bf70a5950e16650976051a0c462` |
| Gelasio-BoldItalic.ttf | 160,812 | `aa87e21b46aef3dcb50a68dc6e1946f0c0a9819d067e4be6a98a6daa8f5b3c16` |
| Gelasio-Italic.ttf | 160,268 | `1d8bda4ff258bb8251eae4f7d7500c8d97d084136192bb264bc2305168765d1b` |
| Gelasio-Regular.ttf | 147,572 | `48c797fbe0e07c48a18cb962e7bdfa23f19618327dddf54093265328dc9eb39d` |
| LiberationMono-Bold.ttf | 307,996 | `bd62a0672d0b9b6710b01df434c80ad54fa5f0835207eb7b17b7a761463067bb` |
| LiberationMono-BoldItalic.ttf | 284,068 | `79451f3c09fe25116098853b7a2ca6e2436220ccc11af022979adbcf195be130` |
| LiberationMono-Italic.ttf | 281,536 | `605c01c711b44480a7508d349dfbf3264e81fa43d69e61cfa7d10b86e764c4d1` |
| LiberationMono-Regular.ttf | 319,508 | `f2b83c763e8afd21709333370bed4774337fae82267937e2b5aea7e2fbd922c1` |
| LiberationSans-Bold.ttf | 414,456 | `788abee4c806d660e8aee46689dd8540cd4bb98da03dcc9d171ce3efd99a9173` |
| LiberationSans-BoldItalic.ttf | 408,996 | `698da70fc191cc5f33ad4d6d3fe830fe4624b898ea2e3169955928b7c491f1ee` |
| LiberationSans-Italic.ttf | 415,816 | `e5bae5c4cde31f22142753855f4f8fb86da6ff39955ed3c0a11248b0d16948b0` |
| LiberationSans-Regular.ttf | 410,712 | `76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8` |
| LiberationSerif-Bold.ttf | 370,096 | `d754ba427cfe0bca54ae052384baa8f842da5bd6550ad4da024ac441e7a7d5ce` |
| LiberationSerif-BoldItalic.ttf | 376,772 | `f17db8af71e24d2066b587546021d4f0b296be389512b658dec3c09affeb11a7` |
| LiberationSerif-Italic.ttf | 375,632 | `0e3dea9f8d613e006ccfa62201f33e265d19167bd0907725c3e145368b04fc2e` |
| LiberationSerif-Regular.ttf | 393,576 | `058ea80864aef09a23f45cbec2bb5400bc3dfbdea01c3f10538a21fcb497fb74` |
| selawk.ttf | 44,224 | `e9d98518d8ac2817782a9a382430463a2e0793ea68350b695bb727d9a830ee1c` |
| selawkb.ttf | 44,068 | `f0db5e174a90e0956ad7d2844bdca1d5e6da92ec65b2c04e57ba9b180668c904` |
| selawkl.ttf | 44,232 | `8e19d073091a1e869b5b0d48b925e605c0a4c6ece7df7b22a364fb065314c4c5` |
| selawksb.ttf | 44,192 | `0a9e9d0549a10f24bef9b3a29e06fe6e0b5c21e7a784c503b048a307841a7783` |
| selawksl.ttf | 44,260 | `8620960344f12093482cded984a1aafa5a57d24cbf7e9299da125ccf0e9d4102` |
