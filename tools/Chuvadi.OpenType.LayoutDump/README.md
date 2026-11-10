# Chuvadi.OpenType.LayoutDump

Test tool (not packed, not in Chuvadi.slnx). Writes a canonical, sorted text dump of a font's GDEF, GSUB and GPOS as
Chuvadi.OpenType's layout views read them: script and language systems, features, lookups with flags and mark
filtering sets, and every subtable's mappings, values and anchors. `fonttools_dump.py` writes the same lines with
fontTools, run as a black box (D-206), so the two files can be compared byte for byte.

Result (2026-11-10, fontTools 4.60.1): identical dumps for all 13 pack fonts, the two oldtags fonts and the uvs font
(about 580,000 lines). Real fonts exercise GSUB types 1–6, GPOS 1, 2, 3, 4, 6, 8, Extension subtables and mark glyph
sets; GSUB 8, GPOS 5 and GPOS 7 are covered by unit tests with built tables (LayoutViewTests).

## Run (PowerShell 5, from the worktree root)

```powershell
$pack = "C:\Users\aruns\Documents\cleanroom-shaping-pack\cleanroom-shaping-pack"
dotnet build -c Release tools\Chuvadi.OpenType.LayoutDump
dotnet artifacts\bin\Chuvadi.OpenType.LayoutDump\Release\net10.0\Chuvadi.OpenType.LayoutDump.dll "$pack\fonts\LiPi-Sans-Tamil.woff2" tamil.ours.txt
py -m pip install fonttools brotli
py -I tools\Chuvadi.OpenType.LayoutDump\fonttools_dump.py "$pack\fonts\LiPi-Sans-Tamil.woff2" tamil.ft.txt
fc.exe /b tamil.ours.txt tamil.ft.txt
```
