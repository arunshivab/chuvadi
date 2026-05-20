# Fuzzing follow-ups (deferred to PR 2.1+)

This file tracks fuzzer findings that are real but intentionally not
fixed in the harness-introducing PR. They become individual PRs after
the harness is in place.

## 1. TrueType: missing bounds checks in TrueTypeLoader

**Status:** real bugs, deferred to PR 2.1.

The `truetype` target surfaced 2447 `IndexOutOfRangeException`s in a
30-second run. The exception type is undocumented (not in
`ExpectedExceptionTypes`) and indicates `TrueTypeLoader` reads from the
input byte buffer without first validating offsets against the buffer
length, so malformed font headers can drive indexed reads past the end.

Sample crash files are under `crashes/truetype/<hash>.bin` after a run.

**Action for PR 2.1:** audit `src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`
and any glyph-lookup helpers it calls. Either guard each indexed read or
route reads through a bounds-checked accessor and convert out-of-range
conditions into `InvalidFontException` (or the project's canonical
malformed-font exception type). After the audit, the IOOR exception
should no longer be reachable from public entry points.

## 2. ArgumentException leaking from PdfName

**Status:** parser paths are correct, exception type is too broad.

`PdfName.Intern(string)` throws `ArgumentException` when given a null or
empty string, and `PdfName.FromRawBytes(ReadOnlySpan<byte>)` reaches the
same throw via interning after byte-level escape decoding. Three callers
visible to the fuzzer reach these for empty input:

- `ContentStreamParser.cs` line 449: `PdfName.FromRawBytes(token.RawBytes)`
  for content-stream Name tokens (bare `/` followed by whitespace).
- `ContentStreamParser.cs` line 409: `PdfName.Intern(fontName)` for the
  `Tf` font-name operand when the operand is the empty string.
- The xref / page-tree resolution path during `PdfDocument.Open` →
  page touching, which is how `pdf-open` reaches the same throw.

All three rejections are correct (empty PDF names are malformed) but
`ArgumentException` is the wrong type for documented parser behavior.
The harness currently lists it under `ExpectedExceptionTypes` for both
`pdf-open` and `content-stream` with a TODO pointing back here.

**Action for PR 2.1:** the cleanest fix is to add a public guard helper
on `PdfName` (e.g. `PdfName.InternOrThrow(string, ParserContext)`) that
throws `PdfTokenizerException` (or `ContentException` from the
content-stream path) when input is empty. Then update the three call
sites above to use it. After the fix, remove `typeof(ArgumentException)`
from both `PdfOpenTarget.ExpectedExceptionTypes` and
`ContentStreamTarget.ExpectedExceptionTypes`.

## Workflow

For each finding above:

1. Reproduce locally with a known crash file:
   `dotnet run -c Release --project tests/Chuvadi.Pdf.Fuzz -- <target> --replay crashes/<target>/<hash>.bin`
   (if `--replay` is not yet implemented, write a one-off unit test
   loading the bin file)
2. Fix the source
3. Re-run the target for 30s; confirm crashes drop to zero (or expected)
4. Remove the workaround from `ExpectedExceptionTypes` if applicable
5. Open the PR as `fix(fuzz): <target> <short description>`
