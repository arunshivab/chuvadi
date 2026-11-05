# Chuvadi.Text.Shaping.Gate (local gate)

Runs the clean-room pack corpus (29,493 lines) straight against Chuvadi.Text.Shaping, without any
Chuvadi.Pdf package. The pack's own harness (`<pack>\harness`, through the Chuvadi.Pdf.Text.Shaping
wrapper) stays the official gate; this one is for daily work and adds:

- explicit feature settings for every line (`corpus-features.txt`, D-68: the library default has cpsp
  on, the expected outputs were made with cpsp off). The file is provisional until the Pdf chat sends
  the exact generation settings;
- `--parity`: a freshly loaded font from the same bytes must give identical output (byte-array path);
- `--bench <rounds>`: microseconds per call, cached font and font loaded from bytes each call;
- `--fontcheck`: font reader sanity checks on the pack fonts (glyph boxes inside the 'head' box,
  lsb = xMin when head flags bit 1 is set);
- `--expect-lines <n>`: a phase target (exactly n matching lines) instead of the final 100% gate.

The WOFF2 fonts are decoded by Chuvadi.OpenType itself (D-155).

## Run (PowerShell 5, from the worktree root)

```powershell
$pack = "C:\Users\aruns\Documents\cleanroom-shaping-pack\cleanroom-shaping-pack"
dotnet run -c Release --project tools\Chuvadi.Text.Shaping.Gate -- --pack $pack --parity --fontcheck --expect-lines 2144 --out gate-phase0.txt
dotnet run -c Release --project tools\Chuvadi.Text.Shaping.Gate -- --pack $pack --diffs 0 --bench 20
```

Other options: `--set syllables|words`, `--script <LipiScript name>`, `--diffs <n>`, `--features <file>`.
