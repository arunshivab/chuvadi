# Chuvadi.Text.Shaping.Gate (local gate)

Runs the clean-room pack corpus (29,493 lines) straight against Chuvadi.Text.Shaping, without any
Chuvadi.Pdf package. The pack's own harness (`<pack>\harness`, through the Chuvadi.Pdf.Text.Shaping
wrapper) stays the official gate; this one is for daily work and adds:

- explicit feature settings for every line (`corpus-features.txt`, the D-68 set with cpsp off, as the
  expected outputs were made; D-188 and d.2), and the language set explicitly to the fonts' default
  language system (D-182);
- `--cpsp`: also runs `<pack>\corpus\cpsp-on.tsv` (358 lines) with the library's default features (cpsp on,
  D-188), the local form of the pack harness's gate 2;
- `--oldtags`: also runs `<pack>\oldtags\corpus\*.tsv` with the fonts in `<pack>\oldtags\fonts` (fonts with
  only the original Indic script tags; 6,743 lines);
- `--parity`: a freshly loaded font from the same bytes must give identical output (byte-array path);
- `--bench <rounds>`: microseconds per call, cached font and font loaded from bytes each call;
- `--fontcheck`: font reader sanity checks on the pack fonts (glyph boxes inside the 'head' box,
  lsb = xMin when head flags bit 1 is set);
- `--expect-lines <n>`: a phase target (exactly n matching lines of the 29,493 syllables and words) instead of
  the final 100% gate. The cpsp-on and oldtags lines are reported but not counted in it.

The WOFF2 fonts are decoded by Chuvadi.OpenType itself (D-155).

## Run (PowerShell 5, from the worktree root)

```powershell
$pack = "C:\Users\aruns\Documents\cleanroom-shaping-pack\cleanroom-shaping-pack"
dotnet run -c Release --project tools\Chuvadi.Text.Shaping.Gate -- --pack $pack --oldtags --cpsp --parity --fontcheck --expect-lines 2144 --out gate-phase1.txt
dotnet run -c Release --project tools\Chuvadi.Text.Shaping.Gate -- --pack $pack --diffs 0 --bench 20
```

Other options: `--set syllables|words`, `--script <LipiScript name>`, `--diffs <n>`, `--features <file>`.
