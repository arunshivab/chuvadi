# Chuvadi.Fonts: platform readiness

What is verified and what is not, for a consumer that runs on more than Windows or trims or AOT-compiles its app. Status as of main after the trim flags change (D-227) and the `trim-aot` CI job (PR #79); the 1.1.0 package on the release page does not yet carry the trim and AOT metadata (it ships with the next release).

## Verified

| Question | Answer | How it was checked |
|---|---|---|
| Managed code only? | Yes. No P/Invoke, no native library, no `unsafe`. | Source review of `src/Chuvadi.Fonts`. |
| Third-party packages? | None. Pure BCL. | CI scans `src/` and `shared/` for package references. |
| Disk or network access? | None. The fonts are embedded resources. | Source review. |
| Windows-only APIs? | None. | Source review. |
| Target framework | Plain `net10.0`. | `Chuvadi.Fonts.csproj`. |
| How the fonts load | On demand, through `GetManifestResourceStream`. Nothing loads at startup, and 22 TrueType files (4,083,976 bytes) are only read when asked for. | Source review, `Chuvadi.Fonts.Tests`. |
| Trim analyzers | 0 warnings with `IsTrimmable` and `IsAotCompatible` on; the build treats warnings as errors. | `dotnet build -c Release`. |
| Real trimmed publish | A self-contained `linux-x64` publish with `TrimMode=full` of a small app using Zip and Fonts built with no warnings. The Tamil font (82,452 bytes) was read from the embedded resources and returned the right bytes. | Run in the build sandbox, 6 Nov 2026. |
| NativeAOT publish | A NativeAOT publish (`PublishAot`, `linux-x64`) of the small app using Zip and Fonts builds with no warnings and runs correctly: all 22 fonts are found, their lengths and SHA-256 hashes match the catalog, and `FontPack.Instance` works. CI repeats it on every change (job `trim-aot`, `ubuntu-latest`, clang). | Run in the build sandbox on 6 Nov 2026; CI job `trim-aot` (PR #79) green. |
| Thread safety | Documented as thread-safe. The licence text is cached with `??=`, a benign race: two threads may both read it, and both get the same text. | Source review. |

## Size

The package is about 1.91 MB, of which the DLL (with the embedded fonts) is about 4.1 MB uncompressed. A trimmed app keeps the embedded resources: the trimmer does not remove resources that code can request by name.

## Not yet verified

- **NativeAOT on other platforms:** verified on `linux-x64` only (CI job `trim-aot`). `win-x64`, `osx-arm64` and Android are not run.
- **macOS and Android**: not run. Nothing platform-specific is used, and CI adds checks next (D-217 item 3).
- **ReadyToRun**: not tested.
- **Browser WebAssembly**: out of scope.
