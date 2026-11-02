# Chuvadi.Fonts — font provenance

All files are licensed under the SIL Open Font License 1.1. The licence text ships in the package's `licenses/`
folder, in `Licenses/` in the repository, and as `FontPack.LicenseText`.

| Files | Family | Version | Source |
|---|---|---|---|
| LiPi-Sans-* (22) | LiPi Sans, LiPi Sans &lt;Script&gt; | from LiPi Sans v1.0 | Static instances of the LiPi Sans variable WOFF2 fonts in arunshivab/chuvadi-pdf `assets/fonts/lipi-sans` (Inter + Noto Sans) |

The files moved here unchanged from Chuvadi.Layout.Fonts (D-113); the hashes below are those of the files that
package carried.

## How the static faces were made

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

## Files

22 files, 4,083,976 bytes. `FontPackTests` checks every row against the embedded file.

| File | Family | Style | Bytes | SHA-256 |
|---|---|---|---|---|
| LiPi-Sans-Bengali-Bold.ttf | LiPi Sans Bengali | Bold | 141,308 | `e475bf3fbcd6158aa52b418fea9028ec23e44752c6fe090b990351383bf02b69` |
| LiPi-Sans-Bengali-Regular.ttf | LiPi Sans Bengali | Regular | 140,844 | `67379428c24fede9480fdb6e5b74567dc3c2f1f3274dc01b0f0309542969a6a9` |
| LiPi-Sans-Devanagari-Bold.ttf | LiPi Sans Devanagari | Bold | 223,216 | `e80ad5f470c95ae27ed9ebbafdc19cd9647127a23353c0ea34a3571029084487` |
| LiPi-Sans-Devanagari-Regular.ttf | LiPi Sans Devanagari | Regular | 222,588 | `e93b3b6d8029e837dac7ef6d4b018a7f87614c3929772ad0f0d7cdd95c16c1e2` |
| LiPi-Sans-Gujarati-Bold.ttf | LiPi Sans Gujarati | Bold | 179,124 | `f16b4cda2d0a19d038b152c3dbe55529ed59157f550cee40b02c2c39e51fd634` |
| LiPi-Sans-Gujarati-Regular.ttf | LiPi Sans Gujarati | Regular | 178,880 | `18812df221d9f137c954d87eff8f1c1df92f33d613bdc7b6af46705e7135a219` |
| LiPi-Sans-Gurmukhi-Bold.ttf | LiPi Sans Gurmukhi | Bold | 67,220 | `142d822f6ef87a419b4454840254c8899b2dae760bc177e133babbef317cadbd` |
| LiPi-Sans-Gurmukhi-Regular.ttf | LiPi Sans Gurmukhi | Regular | 67,092 | `ca60a7ecd0d02d71a3ffce5e0353a291787ab439ff42cf15367a80ac9a0eda6e` |
| LiPi-Sans-Kannada-Bold.ttf | LiPi Sans Kannada | Bold | 169,096 | `883feb0bf2a153f974714a0c43171a51fdb7fafe5f47e00f9e9d0df0ec62bb58` |
| LiPi-Sans-Kannada-Regular.ttf | LiPi Sans Kannada | Regular | 168,936 | `b9ec4801a0a888ec352953efa966b248060668e7953d2bca65f38a550dadd1ce` |
| LiPi-Sans-Latin-Bold.ttf | LiPi Sans | Bold | 343,372 | `38e5e317dfc405b3da9247232c1891c7224d1dc0c1b877bdda1ab9540ac50776` |
| LiPi-Sans-Latin-BoldItalic.ttf | LiPi Sans | Bold Italic | 347,012 | `357737ae3791ad9966e7b81b4dbb872269480ec72b0f22be2f9f25be305ff968` |
| LiPi-Sans-Latin-Italic.ttf | LiPi Sans | Italic | 345,104 | `5b7923ab2ec3b748e1e73c2ae377fedc29712f056f50d0e83e893501a0dcd343` |
| LiPi-Sans-Latin-Regular.ttf | LiPi Sans | Regular | 341,664 | `f6e023cfaced23f7a61e99818947801f048724ddb7d5996e69a99681596df1ce` |
| LiPi-Sans-Malayalam-Bold.ttf | LiPi Sans Malayalam | Bold | 111,332 | `fe829414007f77699fb55fa837144eff7147d1e79323419bf9ce5af962581e5d` |
| LiPi-Sans-Malayalam-Regular.ttf | LiPi Sans Malayalam | Regular | 111,316 | `275c72f27c82423e0dd7775fb93e2ba267c93e10878368f85a440b2a9f24cd7c` |
| LiPi-Sans-Odia-Bold.ttf | LiPi Sans Odia | Bold | 171,624 | `ed61280a537baa4a0f3310d108c8dc748d5076b9e2eb872dc03eb7dbdbb20f6f` |
| LiPi-Sans-Odia-Regular.ttf | LiPi Sans Odia | Regular | 171,304 | `ef9994066b7ac2abbe920bbff146394289c9051c99f94cb8969e261a66dede53` |
| LiPi-Sans-Tamil-Bold.ttf | LiPi Sans Tamil | Bold | 82,532 | `5da75b9591c4e3afadedd9bda91d11c89814af6764b1bfebe9b0d82feb65288f` |
| LiPi-Sans-Tamil-Regular.ttf | LiPi Sans Tamil | Regular | 82,452 | `a5d9fb05ca958067823bea30eec091bd47051c540f505c2c4fb8e693345906d7` |
| LiPi-Sans-Telugu-Bold.ttf | LiPi Sans Telugu | Bold | 209,248 | `003fa8317915832f090a9e139ec321f6218fd40f4ea4dca8642960b55e7208d7` |
| LiPi-Sans-Telugu-Regular.ttf | LiPi Sans Telugu | Regular | 208,712 | `b39f0daff7c7c118e1c2a050136a4a3564509c244ad4983c0b7e31601df99ae7` |
