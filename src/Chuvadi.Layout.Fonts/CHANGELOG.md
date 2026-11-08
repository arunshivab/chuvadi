# Chuvadi.Layout.Fonts — Changelog

## 0.1.0 (2026-11-09)

First release: substitute fonts for documents, as a pure data pack (D-195, D-197).

- 29 SIL OFL 1.1 faces, embedded as resources and listed in FONTS.md: the metric-compatible substitutes Liberation Sans,
  Serif and Mono (for Arial, Times New Roman and Courier New) and Carlito (for Calibri); Caladea, for Cambria; Gelasio,
  for Georgia; and Selawik, for Segoe UI. Caladea, Gelasio and Selawik are close designs, not metric-compatible.
- `SubstituteFontPack` implements Chuvadi.Fonts' `IFontFilePack` (`Files`, `Find`, `FindFamily`, `Open`, `TryOpen`,
  `ReadAllBytes`, `GetLicenseText`, `Instance`). It parses nothing, references no layout engine (only Chuvadi.Fonts
  1.1.0) and reads nothing from disk or the network, so Layout, a PDF library or a print library can use the same files.
- With Chuvadi.Layout: `options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance))`.
- Trim- and NativeAOT-compatible (`IsTrimmable`, `IsAotCompatible`; D-217, D-254).
