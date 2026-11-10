# Chuvadi.Fonts.Liberation — font provenance

All files are licensed under the SIL Open Font License 1.1. The licence text ships in the package's `licenses/`
folder, in `Licenses/` in the repository, and as `LiberationPack.LicenseText`. The Reserved Font Name is "Liberation";
the fonts are shipped unmodified.

| Files | Family | Version | Source |
|---|---|---|---|
| LiberationSans-*, LiberationSerif-*, LiberationMono-* (12) | Liberation Sans / Serif / Mono | 2.1.5 | Byte-identical to the TTFs in the upstream release archive `liberation-fonts-ttf-2.1.5.tar.gz` (github.com/liberationfonts/liberation-fonts, release 2.1.5; checked 2026-11-10, see below); licence text from upstream commit 49e1358 |

The same 12 files, byte for byte, were embedded in Chuvadi.Pdf.PdfA 3.32.1 and are in Chuvadi.Layout.Fonts 0.1.0
(D-302, D-307); this package is their single shared copy.

## Verified against upstream

On 2026-11-10 all 12 files were compared, byte for byte (SHA-256), with the files in upstream's release archive
`liberation-fonts-ttf-2.1.5.tar.gz`: 12 of 12 identical. The files are therefore unmodified upstream 2.1.5 builds, not
distribution rebuilds. (Debian and Arch rebuild the fonts from source, so their files differ by a few bytes.)

Copyright lines as stored in the font files (name ID 0): "Digitized data copyright (c) 2010 Google Corporation.
Copyright (c) 2012 Red Hat, Inc." Liberation is a trademark of Red Hat, Inc.

## Metric compatibility

| Office font | Liberation face | Metric-compatible |
|---|---|---|
| Arial (Helvetica in PDF) | Liberation Sans | yes |
| Times New Roman (Times in PDF) | Liberation Serif | yes |
| Courier New (Courier in PDF) | Liberation Mono | yes |

Symbol and ZapfDingbats (the other two of the PDF Standard 14 fonts) are not part of this package.

## Files

12 files, 4,359,164 bytes. `LiberationPackTests` checks every row against the embedded file; the SHA-256 over the 12
files concatenated in file-name order is `3e72b28294f5b176a00f27f9bcdac583d112146b8d5dcda4938118107511f1cb`.

| File | Family | Style | Bytes | SHA-256 |
|---|---|---|---|---|
| LiberationMono-Bold.ttf | Liberation Mono | Bold | 307,996 | `bd62a0672d0b9b6710b01df434c80ad54fa5f0835207eb7b17b7a761463067bb` |
| LiberationMono-BoldItalic.ttf | Liberation Mono | Bold Italic | 284,068 | `79451f3c09fe25116098853b7a2ca6e2436220ccc11af022979adbcf195be130` |
| LiberationMono-Italic.ttf | Liberation Mono | Italic | 281,536 | `605c01c711b44480a7508d349dfbf3264e81fa43d69e61cfa7d10b86e764c4d1` |
| LiberationMono-Regular.ttf | Liberation Mono | Regular | 319,508 | `f2b83c763e8afd21709333370bed4774337fae82267937e2b5aea7e2fbd922c1` |
| LiberationSans-Bold.ttf | Liberation Sans | Bold | 414,456 | `788abee4c806d660e8aee46689dd8540cd4bb98da03dcc9d171ce3efd99a9173` |
| LiberationSans-BoldItalic.ttf | Liberation Sans | Bold Italic | 408,996 | `698da70fc191cc5f33ad4d6d3fe830fe4624b898ea2e3169955928b7c491f1ee` |
| LiberationSans-Italic.ttf | Liberation Sans | Italic | 415,816 | `e5bae5c4cde31f22142753855f4f8fb86da6ff39955ed3c0a11248b0d16948b0` |
| LiberationSans-Regular.ttf | Liberation Sans | Regular | 410,712 | `76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8` |
| LiberationSerif-Bold.ttf | Liberation Serif | Bold | 370,096 | `d754ba427cfe0bca54ae052384baa8f842da5bd6550ad4da024ac441e7a7d5ce` |
| LiberationSerif-BoldItalic.ttf | Liberation Serif | Bold Italic | 376,772 | `f17db8af71e24d2066b587546021d4f0b296be389512b658dec3c09affeb11a7` |
| LiberationSerif-Italic.ttf | Liberation Serif | Italic | 375,632 | `0e3dea9f8d613e006ccfa62201f33e265d19167bd0907725c3e145368b04fc2e` |
| LiberationSerif-Regular.ttf | Liberation Serif | Regular | 393,576 | `058ea80864aef09a23f45cbec2bb5400bc3dfbdea01c3f10538a21fcb497fb74` |
