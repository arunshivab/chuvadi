# Chuvadi.Text.Shaping.UcdGen

Generates `src/Chuvadi.Text.Shaping/Unicode/UnicodeData.g.cs` from the pinned Unicode Character
Database files in `tools/ucd/18.0.0/` (S7: one pinned UCD version, our own generator). Tool only;
nothing here ships.

The output depends only on the input files: running it again on the same files gives the same bytes,
and the generated file's header lists the SHA-256 of every input.

Tables produced: General_Category, Canonical_Combining_Class, Indic_Syllabic_Category,
Indic_Positional_Category, Default_Ignorable_Code_Point, Script (ISO 15924 codes), canonical
decompositions and primary compositions. About 100 KB of table data in the DLL.

## Regenerate (PowerShell 5, from the worktree root)

```powershell
dotnet run -c Release --project tools\Chuvadi.Text.Shaping.UcdGen -- tools\ucd\18.0.0 src\Chuvadi.Text.Shaping\Unicode\UnicodeData.g.cs
git diff --stat -- src\Chuvadi.Text.Shaping\Unicode\UnicodeData.g.cs
```

`git diff` must show no change unless the UCD version was changed on purpose.

## Changing the Unicode version

Add a new `tools/ucd/<version>/` folder with the same file names, regenerate into the same output,
and run the Chuvadi.Text.Shaping tests (they check every code point against the files and run the
whole of NormalizationTest.txt). A version change is a behaviour change and goes to Arun first (rule 5).
