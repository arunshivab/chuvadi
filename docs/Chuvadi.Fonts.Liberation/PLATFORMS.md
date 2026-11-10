# Chuvadi.Fonts.Liberation: platform readiness

What is verified and what is not, for a consumer that runs on more than Windows or trims or AOT-compiles its app.
Status as of the first release (1.0.0).

## Verified

| Question | Answer | How it was checked |
|---|---|---|
| Managed code only? | Yes. No P/Invoke, no native library, no `unsafe`. | Source review of `src/Chuvadi.Fonts.Liberation`. |
| Third-party packages? | None. The only reference is Chuvadi.Fonts (for `IFontFilePack`). Pure BCL. | CI scans `src/` and `shared/` for package references. |
| Disk or network access? | None. The fonts are embedded resources. | Source review. |
| Windows-only APIs? | None. | Source review. |
| Target framework | Plain `net10.0`. | `Chuvadi.Fonts.Liberation.csproj`. |
| How the fonts load | On demand, through `GetManifestResourceStream`. Nothing loads at startup; the 12 TrueType files (4,359,164 bytes) are only read when asked for. | Source review, `Chuvadi.Fonts.Liberation.Tests`. |
| Trim analyzers | 0 warnings with `IsTrimmable` and `IsAotCompatible` on; the build treats warnings as errors. | `dotnet build -c Release`. |
| Trimmed and NativeAOT use | The `trim-aot` CI job publishes a small app that reads a Liberation face and the licence through `LiberationPack` with `TrimMode=full` and as NativeAOT (linux-x64), with warnings as errors, and runs it. | CI job `trim-aot`. |
| Thread safety | Documented as thread-safe. The licence text is cached with `??=`, a benign race: two threads may both read it, and both get the same text. | Source review. |

## Size

The DLL (with the embedded fonts) is about 4.4 MB uncompressed. A trimmed app keeps the embedded resources: the trimmer does
not remove resources that code can request by name. That is why the Liberation fonts are a separate package from
Chuvadi.Fonts.

## Not yet verified

- **NativeAOT on other platforms:** `linux-x64` only (CI job `trim-aot`). `win-x64`, `osx-arm64` and Android are not run.
- **macOS and Android:** not run beyond the CI jobs; nothing platform-specific is used.
- **ReadyToRun** and **Browser WebAssembly:** not tested / out of scope.
