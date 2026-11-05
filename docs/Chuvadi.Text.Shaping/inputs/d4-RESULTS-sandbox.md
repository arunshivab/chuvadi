# Shaping timings measured in the Pdf chat's sandbox (d.4)

Measured on 2026-11-05 with `ShapingTiming` (this zip). Each engine was
built from its release tag with `-p:Version`, so the DLL sizes are those of
the shipped builds.

**Machine:** Intel Xeon @ 2.80 GHz, 2 logical CPUs (a shared cloud VM),
Ubuntu 24.04.5, .NET 10.0.7, x64. This machine is slow and noisy: Arun's
machine will give lower and steadier numbers. Compare engines only on one
machine, which is what the coordinator-only run on Arun's machine is for.

## 3.25.0 (the engine before the HarfBuzz port)

| Case | Glyphs | Shape(byte[]) µs | Shape(FontFace) µs | FontFace.Load µs |
|---|---|---|---|---|
| Latin | 56 | 488.1 | 168.3 | 129.6 |
| Tamil | 36 | 37.7 | 8.3 | 38.0 |
| Devanagari | 36 | 51.8 | 15.5 | 22.6 |

3.25 shapes Tamil and Devanagari to different glyphs (36 glyphs rather than
28 and 31), because it is the old engine. Its times are for different, less
complete work.

## 3.26.0 (the HarfBuzz port; the gate's reference engine)

| Case | Glyphs | Shape(byte[]) µs | Shape(FontFace) µs | FontFace.Load µs |
|---|---|---|---|---|
| Latin | 56 | 292.5 | 28.5 | 135.0 |
| Tamil | 28 | 75.8 | 16.7 | 25.4 |
| Devanagari | 31 | 137.2 | 69.3 | 26.0 |

In 3.26, `Shape(byte[])` loads the face and builds the plan on every call.
3.27 cached them (D-70), which is why 3.31 below is much faster in that
column.

## 3.31.0 (for reference)

| Case | Glyphs | Shape(byte[]) µs | Shape(FontFace) µs | FontFace.Load µs |
|---|---|---|---|---|
| Latin | 56 | 37.4 | 31.2 | 670.3 |
| Tamil | 28 | 36.9 | 15.7 | 58.0 |
| Devanagari | 31 | 93.5 | 67.6 | 71.9 |

`FontFace.Load` is slower in 3.31 (Latin 670 µs against 135 µs in 3.26 in
this run; the machine is noisy, and a second measurement of the character
map loader alone gave 785 µs against 444 µs). The cause is the D-169 cmap
change. 3.31 reads every Unicode subtable into a lookup table, now including
the two full-repertoire ones (format 12 under (0,4) and (3,10), about 2,850
mappings each on LiPi Sans Latin), and keeps only the best. A face is loaded
once, so the shaper is not affected. I am reporting it to the coordinator as
a 3.31.x candidate: skip subtables that cannot win.

## DLL size

| Package | `Chuvadi.Pdf.Text.Shaping.dll` |
|---|---|
| 3.25.0 | 65,536 bytes |
| 3.26.0 | **370,688 bytes** |
| 3.31.0 | 371,200 bytes |

The contract's performance targets (Latin 32 µs, Tamil 19 µs, Devanagari
47 µs) are `Shape(FontFace)` times of the 3.26 engine. I have no record of the
machine or program they were measured with. They are the same order as the
3.26 column above.
