# Chuvadi.Layout.Viewer

The reference host for `docs/Chuvadi.Layout/INTEGRATION.md`. It opens a .docx, .xlsx, .txt or .md (through Chuvadi.Layout.Markup) with Chuvadi.Layout and
writes:
- each page as SVG (the desktop-viewer path, `LayoutSvg`) and/or PNG (the server-side preview path, through Chuvadi.Layout.Pdf);
- `index.html`, which shows the pages lazily;
- `viewer-report.txt`, with timings, font substitutions and warnings.

The project is not in `Chuvadi.slnx` or CI, because it uses the chuvadi-pdf packages from the local feed (D-29).

```
dotnet run -c Release --project staging\samples\Chuvadi.Layout.Viewer -- <file> [options]
```

| Option | Meaning |
|---|---|
| `--out <folder>` | Output folder (default `<file>-pages` next to the file) |
| `--format svg\|png\|both` | Default both |
| `--dpi <n>` | PNG resolution, default 150 |
| `--pages 1-3,7` | Only these pages (1-based) |
| `--password <pw>` | For an encrypted docx or xlsx |
| `--culture <name>` | Number formats and the D-30 paper default (default: the current culture) |
| `--system-fonts` | Also use installed fonts (desktop viewers do; server-side previews do not) |
| `--untrusted` | `LayoutLimits.ForUntrustedInput()` |
| `--timeout <s>` | Cancel after this many seconds, counted from when the file is in memory |
| `--paper <code>` | xlsx paper code to use when a sheet states none |

The file is read into memory first, and its read time is logged separately: on Windows, Microsoft Defender scans an
encrypted docx/xlsx on open by trying the default password, which can take seconds before any library code runs, so
neither "opened in" nor `--timeout` includes it (D-44).

Exit codes:
- 0: done;
- 1: the document could not be read or laid out;
- 2: bad arguments;
- 3: cancelled or timed out;
- 4: a limit was exceeded.
