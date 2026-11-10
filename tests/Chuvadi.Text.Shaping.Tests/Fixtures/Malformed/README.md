# Malformed-font fixtures (d.6, D-190)

Received from the Pdf chat on 2026-11-05 (docs/Chuvadi.Text.Shaping/inputs/d6-EXPECTED.md). Committed here:
- the two base fonts, unchanged: `M1-base-Carlito-Regular.ttf` (Carlito, OFL 1.1: `Carlito-OFL.txt`) and
  `M3-base-LiPi-Sans-Tamil.ttf` (LiPi Sans Tamil as plain sfnt, OFL 1.1: `LiPi-Sans-LICENSE.txt`);
- `M3-corrupt-layout-patches.tsv` and `SHA256.tsv`, as received;
- `recipes.tsv`: how M1, M2 and M4 to M7 are rebuilt from a base font;
- `expected.tsv`: the one-line expected outcome of each fixture, as data.

`MalformedFontTests` rebuilds all twelve fixture files and the 180 M3 variants in memory, checks each file against
`SHA256.tsv`, and checks its outcome. The modified fonts are never written to disk or distributed; only the
unmodified base fonts are (the OFL's Reserved Font Names apply to modified versions).
