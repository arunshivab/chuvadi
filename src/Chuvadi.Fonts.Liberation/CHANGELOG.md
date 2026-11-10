# Chuvadi.Fonts.Liberation — Changelog

## 1.0.0 (unreleased, tag fonts-liberation-v1.0.0)

First release: the Liberation fonts as one shared, separate package (D-302, D-307, D-311).

- 12 SIL OFL 1.1 faces, embedded as resources and listed in FONTS.md: Liberation Sans, Serif and Mono in Regular, Bold,
  Italic and Bold Italic (version 2.1.5). The same files, byte for byte, that Chuvadi.Pdf.PdfA 3.32.1 and
  Chuvadi.Layout.Fonts 0.1.0 carried.
- `LiberationPack` implements Chuvadi.Fonts' `IFontFilePack` (`Files`, `Find`, `FindFamily`, `Open`, `TryOpen`,
  `ReadAllBytes`, `LicenseText`, `Instance`, and `GetLicenseText` through `Instance`). It parses nothing, references no
  layout engine (only Chuvadi.Fonts 1.1.0) and reads nothing from disk or the network.
- Verified on 2026-11-10: all 12 files are byte-identical to the upstream release archive `liberation-fonts-ttf-2.1.5.tar.gz`
  (see FONTS.md).
- Trim- and NativeAOT-compatible (`IsTrimmable`, `IsAotCompatible`; D-217, D-254).
