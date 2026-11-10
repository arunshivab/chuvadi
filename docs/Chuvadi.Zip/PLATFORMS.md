# Chuvadi.Zip: platform readiness

What is verified and what is not, for a consumer that runs on more than Windows or trims or AOT-compiles its app. Status as of main after the trim flags change (D-227) and the `trim-aot` CI job (PR #79); the 1.0.0 package on the release page does not yet carry the trim and AOT metadata (it ships with the next release).

## Verified

| Question | Answer | How it was checked |
|---|---|---|
| Managed code only? | Yes. No P/Invoke, no native library, no `unsafe`. | Source review of `src/Chuvadi.Zip` and `shared/Chuvadi.Internal`. |
| Third-party packages? | None. Pure BCL. | CI scans `src/` and `shared/` for package references. |
| Cryptography | BCL only (`System.Security.Cryptography`: AES, HMAC, PBKDF2). .NET uses the operating system's crypto library for these, so they work wherever .NET does. | Source review. |
| Windows-only APIs? | None. `OperatingSystem.IsWindows()` only selects Unix file-mode handling and the case rule for names. | Source review. |
| Target framework | Plain `net10.0`, no `-windows` suffix. | `Chuvadi.Zip.csproj`. |
| Trim analyzers | 0 warnings with `IsTrimmable` and `IsAotCompatible` on; the build treats warnings as errors. | `dotnet build -c Release`. |
| Real trimmed publish | A self-contained `linux-x64` publish with `TrimMode=full` of a small app using Zip and Fonts built with no warnings and ran correctly. | Run in the build sandbox, 6 Nov 2026. |
| NativeAOT publish | A NativeAOT publish (`PublishAot`, `linux-x64`) of the same small app using Zip and Fonts builds with no warnings and runs correctly (zip round trip with plain, stored and AES-256 entries, `Verify`, wrong-password rejection). CI repeats it on every change (job `trim-aot`, `ubuntu-latest`, clang). | Run in the build sandbox on 6 Nov 2026; CI job `trim-aot` (PR #79) green. |
| Thread safety | One archive object is safe to share: each archive takes a per-archive lock, and there is no mutable static state. Separate archives are independent. | Source review. |
| Memory | Bounded: entries stream, and the decompression limits apply. | `HardeningTests`, `LargeEntryTests`. |

## Not yet verified

- **NativeAOT on other platforms:** verified on `linux-x64` only (CI job `trim-aot`). `win-x64`, `osx-arm64` and Android are not run.
- **macOS and Android**: the tests have never run there. CI adds macOS and an Android build check next (D-217 item 3). Until then, treat both as expected to work (nothing platform-specific is used) but not proven.
- **ReadyToRun**: not tested.
- **Browser WebAssembly**: out of scope.

## Startup cost (one measurement)

A trimmed hello-world started in 50 to 60 ms and 19 MB; a trimmed app using Zip and Fonts started in 131 to 144 ms and 26 to 27 MB. Most of the difference is JIT time for the first use, not the library's own work. This is one machine, one run; do not read it as a benchmark.
