# Chuvadi.Fonts: platform readiness

What is verified and what is not, for a consumer that runs on more than Windows or trims or AOT-compiles its app. Status as of main after the trim flags change (D-227) and the `trim-aot` CI job (PR #79). The trim and AOT metadata ships from 1.2.0; the 1.1.0 package does not carry it.

## Verified

| Question | Answer | How it was checked |
|---|---|---|
| Managed code only? | Yes. No P/Invoke, no native library, no `unsafe`. | Source review of `src/Chuvadi.Fonts`. |
| Third-party packages? | None. Pure BCL. | CI scans `src/` and `shared/` for package references. |
| Disk or network access? | None. The fonts are embedded resources. | Source review. |
| Windows-only APIs? | None. | Source review. |
| Target framework | Plain `net10.0`. | `Chuvadi.Fonts.csproj`. |
| How the fonts load | On demand, through `GetManifestResourceStream`. Nothing loads at startup, and 23 TrueType files (4,970,604 bytes since 1.2.0) are only read when asked for. | Source review, `Chuvadi.Fonts.Tests`. |
| Trim analyzers | 0 warnings with `IsTrimmable` and `IsAotCompatible` on; the build treats warnings as errors. | `dotnet build -c Release`. |
| Real trimmed publish | A self-contained `linux-x64` publish with `TrimMode=full` of a small app using Zip and Fonts built with no warnings. The Tamil font (82,452 bytes) was read from the embedded resources and returned the right bytes. | Run in the build sandbox, 6 Nov 2026. |
| NativeAOT publish | A NativeAOT publish (`PublishAot`, `linux-x64`) of the small app using Zip and Fonts builds with no warnings and runs correctly: all the fonts are found (23 since 1.2.0; the trim smoke app checks the count), their lengths and SHA-256 hashes match the catalog, and `FontPack.Instance` works. CI repeats it on every change (job `trim-aot`, `ubuntu-latest`, clang). | Run in the build sandbox on 6 Nov 2026; CI job `trim-aot` (PR #79) green. |
| Thread safety | Documented as thread-safe. The licence text is cached with `??=`, a benign race: two threads may both read it, and both get the same text. | Source review. |

## Size

The package is about 2.5 MB since 1.2.0 (1.1.0: 1.91 MB), of which the DLL (with the embedded fonts) is about 5 MB uncompressed. A trimmed app keeps the embedded resources: the trimmer does not remove resources that code can request by name.

## Not yet verified

- **NativeAOT on other platforms:** verified on `linux-x64` only (CI job `trim-aot`). `win-x64`, `osx-arm64` and Android are not run.
- **macOS and Android**: not run. Nothing platform-specific is used, and CI adds checks next (D-217 item 3).
- **ReadyToRun**: not tested.
- **Browser WebAssembly**: out of scope.
