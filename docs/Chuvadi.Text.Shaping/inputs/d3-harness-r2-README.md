# Gate harness, revision 2 (d.3, D-182 and D-188)

This replaces these parts of `cleanroom-shaping-pack`:
- `harness/Program.cs` and `harness/Chuvadi.Shaping.Gate.csproj`;
- `GATE.md`.

It adds one file:
- `corpus/cpsp-on.tsv`.

Copy them over the old ones. `corpus/syllables.tsv`, `corpus/words.tsv` and
the fonts are unchanged. This is test tooling and data only, with no HarfBuzz
code. The contract change that goes with it (D-188) is in the separate
contract zip.

## The two gates

| Gate | Corpus | Features | Lines |
|---|---|---|---|
| 1 | `syllables.tsv`, `words.tsv` | The D-68 set, passed explicitly tag by tag: `ccmp`, `locl`, `calt`, `liga`, `kern`, `mark`, `mkmk` on; `cpsp` and every optional feature off | 29,493 |
| 2 | `cpsp-on.tsv` | `ShapingFeatures.Default`, the engine's own | 358 |

- **Gate 1** is option (A) of D-188. It does not depend on what any engine's
  `Default` means.
- **Gate 2** tests the `Default`. The harness first checks the engine's
  `ShapingFeatures.Default` tag by tag against the contract (the D-68 set with
  `cpsp` on). It then shapes each line with `Default`, and once more with no
  features argument, which must give the same result.
- **Common to both gates:**
  - The language is the fonts' default language system.
  - Every line is checked for `Shape(byte[])` against `Shape(FontFace)` parity.
  - Both call forms are timed.
  - The exit code is 0 only when both gates pass.

## `corpus/cpsp-on.tsv`

- 358 Latin lines from `words.tsv`: 182 in Carlito and 176 in LiPi Sans Latin.
- **How it was made:** every one of the 29,493 corpus lines was regenerated
  with HarfBuzz 15.0.0 (uharfbuzz 0.57.0), with the d.2 revision 2 settings
  (direction LTR, script set, no language) plus `cpsp` on.
- **Check of the method:** with `cpsp` off, the same run reproduces all
  29,493 expected lines exactly.
- **What changed:** with `cpsp` on, exactly these 358 lines change, all in x
  advance and x offset of capital letters. The other 29,135 lines are
  identical, so `Default` needs no other expected outputs.
- **Format:** the same four columns as the other corpus files.

## Proof (Pdf chat's sandbox, with parity on)

| Engine | Gate 1 | Gate 2 | Result |
|---|---|---|---|
| 3.31.0, published-shaper mode (`-p:ChuvadiVersion=3.31.0`) | 29,493 of 29,493 | `Default` tags match; 358 of 358 | `GATE PASSED` (`gate-report-3.31.0.txt`) |
| 3.26.0, published-shaper mode | 29,493 of 29,493 | `Default` has `cpsp` off, reported as differing from the contract; 0 of 358 | `GATE FAILED`, as it should (`gate-report-3.26.0.txt`) |
| The engine skeleton with the D-188 contract change | 2,144 of 29,493 (7.27%, as before) | `Default` tags match; 0 of 358 | `GATE FAILED`, as expected for a skeleton |
| `--self-test` | — | — | `SELF-TEST PASSED (22 checks)` |

The 3.31.0 result agrees with the earlier finding: shaping the main corpus
with 3.31.0's `Default` failed exactly 358 Latin lines on x advance, and
those lines now pass gate 2.
