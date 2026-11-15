# Chuvadi.Layout.Fonts — Changelog

## 0.2.0 (2026-11-15)

One copy of Liberation in the family (D-327, L1).

- The 12 Liberation Sans, Serif and Mono files and `Liberation-OFL.txt` are no longer embedded here. `SubstituteFontPack`
  serves them from **Chuvadi.Fonts.Liberation 1.0.0** (`LiberationPack`), a new dependency. They are byte-identical to
  the files 0.1.0 carried.
- **Unchanged for callers:** the pack still lists all 29 files, in the same order, with the same names, lengths, SHA-256
  and licence text, through the same API (`Files`, `Find`, `FindFamily`, `Open`, `TryOpen`, `ReadAllBytes`,
  `GetLicenseText`, `Instance`, name "Chuvadi.Layout.Fonts"). Layout output is unchanged (88 documents compared).
- **Smaller:** the package drops about 2.4 MB. An application that also uses Chuvadi.Fonts.Liberation (for example
  through chuvadi-pdf's PDF/A fonts) no longer ships the Liberation files twice.
- **Only for code that bypassed the API:** the 12 files are no longer resources of the Chuvadi.Layout.Fonts assembly,
  and `licenses/Liberation-OFL.txt` is now in the Chuvadi.Fonts.Liberation package.

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
