# Compatibility: what is released, and what depends on what

For consumers of the Chuvadi packages (the Chuvadi Reader, other apps). **Updated at every release**, in a docs-only PR after the release is checked. Every row below comes from the GitHub release page and the asset digest GitHub shows for it, or from the release record of that tag. Last updated 2026-11-06, main `3b54293`.

- **How packages are delivered:** a `.nupkg` attached to the GitHub release of its tag. Nothing is on nuget.org (D-46). Use the package from a local feed. Recent release tags are signed; the packages themselves are not signed (no certificate).
- **Tags:** one tag per package, `<name>-vX.Y.Z`. The `docs-v*` tag releases Chuvadi.Docs and Chuvadi.Docs.Conversion together.
- **Release page:** `https://github.com/arunshivab/chuvadi/releases/tag/<tag>`.
- **Frameworks:** every package targets plain `net10.0`, except where a row says otherwise.
- **Third-party dependencies:** none, in any package. The only dependencies are the Chuvadi packages named in the tables.

## Latest release of each package

| Package | Version | Tag | Asset | Size (bytes) | SHA-256 | Depends on |
|---|---|---|---|---|---|---|
| Chuvadi.Sheets | 2.0.1 | `sheets-v2.0.1` | `Chuvadi.Sheets.2.0.1.nupkg` | 352,515 | `417f537f74826deeb8cd14492441800243b45df9e714cd9ce6daf9138571c253` | nothing |
| Chuvadi.Docs | 1.5.1 | `docs-v1.5.1` | `Chuvadi.Docs.1.5.1.nupkg` | 273,471 | `102b682a5820110cd547ea648db6d87dedbf57906ef33d2a1a26730c4df1c265` | nothing |
| Chuvadi.Markup | 1.0.0 | `markup-v1.0.0` | `Chuvadi.Markup.1.0.0.nupkg` | 197,659 | `3257478005d2a159e8cf9d88899876dd75a419b8d2cd7d7d6b6754f9213a4192` | nothing |
| Chuvadi.Zip | 1.0.0 | `zip-v1.0.0` | `Chuvadi.Zip.1.0.0.nupkg` | 51,674 | `e019f9b61d48d705e9b8e81e3ca8bcbb3114056b2127f4e9ea2af0aa51110104` | nothing |
| Chuvadi.Fonts | 1.1.0 | `fonts-v1.1.0` | `Chuvadi.Fonts.1.1.0.nupkg` | 1,910,511 | `e6f5ffa6061dbab3d472c100e1e91846b33af485d2d2cb7a956902834d1f742c` | nothing |
| Chuvadi.Print | 1.0.0-rc.2 (pre-release) | `print-v1.0.0-rc.2` | `Chuvadi.Print.1.0.0-rc.2.nupkg` | 58,543 | `252b6d10204d37069a35040ea89d58e1662a452c4e05fd90a0d8b90cd1049cbf` | nothing |
| Chuvadi.Print.Ipp | 1.0.0-rc.2 (pre-release) | `print-v1.0.0-rc.2` | `Chuvadi.Print.Ipp.1.0.0-rc.2.nupkg` | 65,855 | `82304b782ce23ac5dcb3895e1fee31898abf3c22222f9f9c65c423fba68aa562` | Chuvadi.Print 1.0.0-rc.2 or later |
| Chuvadi.Print.Remote | 1.0.0-rc.2 (pre-release) | `print-v1.0.0-rc.2` | `Chuvadi.Print.Remote.1.0.0-rc.2.nupkg` | 55,302 | `973885310888f3ec683633e1e94db1950d30ab0a7c0c2a07df11678794e7ebbf` | Chuvadi.Print 1.0.0-rc.2 or later |
| Chuvadi.Print.Windows | 1.0.0-rc.2 (pre-release) | `print-v1.0.0-rc.2` | `Chuvadi.Print.Windows.1.0.0-rc.2.nupkg` | 31,231 | `df1ab37a48786ca5f1822e033b2f0fd45ba7339d9850140365360defb527b375` | Chuvadi.Print 1.0.0-rc.2 or later |

The four Print packages are released together under one tag and one version. Chuvadi.Print.Windows loads on every platform but works only on Windows.

## Not released yet

Versions are what `main` carries. A package listed here has no release page and no asset.

| Package | Version on main | Waiting for | Depends on |
|---|---|---|---|
| Chuvadi.Docs | 2.0.0 | `docs-v2.0.0`, after `markup-v1.1.0` (`docs-v1.6.0` was skipped and never tagged; its content ships in 2.0.0) | nothing |
| Chuvadi.Docs.Conversion | 2.0.0 | `docs-v2.0.0` (first release; same tag as Docs) | Chuvadi.Docs 2.0.0, Chuvadi.Markup 1.1.0 |
| Chuvadi.Markup | 1.1.0 | `markup-v1.1.0` (the clean-room swap, D-192); must come before `docs-v2.0.0` | nothing |
| Chuvadi.Layout | 0.1.0 | `layout-v0.1.0` | Chuvadi.Fonts |
| Chuvadi.Layout.Fonts | 0.1.0 | `layout-fonts-v0.1.0` | Chuvadi.Fonts 1.1.0 or later (it implements `IFontFilePack`) |
| Chuvadi.Layout.Docs | 0.1.0 | `layout-docs-v0.1.0` | Chuvadi.Layout, Chuvadi.Docs |
| Chuvadi.Layout.Sheets | 0.1.0 | `layout-sheets-v0.1.0` | Chuvadi.Layout, Chuvadi.Sheets |
| Chuvadi.Layout.Documents | 0.1.0 | `layout-documents-v0.1.0` | Chuvadi.Layout.Docs, Chuvadi.Layout.Sheets |
| Chuvadi.Layout.Markup | 0.1.0 | `layout-markup-v0.1.0` | Chuvadi.Layout, Chuvadi.Markup |
| Chuvadi.OpenType | 0.1.0 | `opentype-v0.1.0` (on hold) | nothing |
| Chuvadi.Text.Shaping | 0.1.0 | `text-shaping-v0.1.0` (on hold) | Chuvadi.OpenType |
| Chuvadi.Print (next) | 1.0.0-rc.3 | the rest of Phase 4 (XPS submission) | nothing |
| Chuvadi.Layout.Pdf | not in the solution | no release path yet (D-214); built in `staging/` against Chuvadi.Pdf | Chuvadi.Layout and Chuvadi.Pdf packages |

Chuvadi.Pdf is a separate repository and has its own releases. Nothing in the packages above depends on it, except Layout.Pdf (staging).

Exact minimum versions for the Layout packages are fixed when they are released; until then they build against what is on `main` (Fonts 1.1.0, Docs 2.0.0, Sheets 2.0.1, Markup 1.1.0).

## Older releases

Each is a single `.nupkg` on its release page. Use the latest row above unless you need a specific older version.

| Tag | Asset | Size (bytes) | SHA-256 |
|---|---|---|---|
| `sheets-v2.0.0` | `Chuvadi.Sheets.2.0.0.nupkg` | 346,714 | `17d0d0beefc09b60da9c2a472a1a42ac5040579aebb28ac7a95926a9e2db137a` |
| `sheets-v1.8.0` | `Chuvadi.Sheets.1.8.0.nupkg` | 344,173 | `0afe6f38d7e40c7fc91606103e84ebd802021d696c3df145eeb72edd831a270a` |
| `sheets-v1.7.0` | `Chuvadi.Sheets.1.7.0.nupkg` | 343,716 | `30f260c9c72e263ebcae5f465631a154f11dc6745eefa25d5a930aedb460040e` |
| `sheets-v1.2.0` | `Chuvadi.Sheets.1.2.0.nupkg` | 158,293 | `e07b053180d3549c484ded85fa8786b246abcdbdca1502b0df2acf56f59d45bd` |
| `sheets-v1.1.2` | `Chuvadi.Sheets.1.1.2.nupkg` | 124,433 | `daaebe4702139fdd6b74ee97e225094e10f8762861a54fd6949ac43a618dc8d4` |
| `sheets-v1.1.1` | `Chuvadi.Sheets.1.1.1.nupkg` | 123,944 | `d51bb7850bb68b53a08bd1d4629d61eeb9bbd3848bd7db76f5985d199a1ad42a` |
| `docs-v1.4.0` | `Chuvadi.Docs.1.4.0.nupkg` | 232,723 | `cd933e8bde259ac00f0b363898deaa4b2f105433dad9461cb272903553bc37ab` |
| `docs-v1.3.0` | `Chuvadi.Docs.1.3.0.nupkg` | 179,141 | `dc8d1c6143c6970baed8ecb02e95eb0d55d90996a3e02cef9b026e4e0fd818f9` |
| `docs-v1.2.0` | `Chuvadi.Docs.1.2.0.nupkg` | 137,599 | `e5e4d5d4471ac639bcd51dd767c98f7e2a6012cd16524a6bc794547dbad7b6f5` |
| `docs-v1.1.0` | `Chuvadi.Docs.1.1.0.nupkg` | 85,203 | `a90463271109004816167f256d24db2059fcb431653a3b5158dd9e45120ad2b9` |
| `docs-v1.0.0` | `Chuvadi.Docs.1.0.0.nupkg` | 59,146 | `9537b471c4f2ea7f203a8ad40237a301a0119934206beea5e65841dd32128f90` |
| `fonts-v1.0.0` | `Chuvadi.Fonts.1.0.0.nupkg` | 1,908,123 | `c15a82d1caa3d70f3e2abe16dd94201ea1d3dae91764735a19ffa7398c37f5fe` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.1.0.0-rc.1.nupkg` | 46,825 | `b04b706032d3d3de20afd9fbfa6a14a60846fb410aa237ba5e2c628d2dc2a321` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.Ipp.1.0.0-rc.1.nupkg` | 46,199 | `ebbf36f247e57fd58fb23f3603bbce9ab9752594f24c5385f5eb2cb9f8931f65` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.Remote.1.0.0-rc.1.nupkg` | 55,185 | `873544780f51b49f05083b140d1ff83af65f6b96ac5209db11fd1f5cf451c8fb` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.Windows.1.0.0-rc.1.nupkg` | 29,072 | `d1ca4b997fa18774c9db92f0a3710dce21ed451afaa93364071c054e08c7a3a9` |

Notes:

- `docs-v1.1.0` is a lightweight tag, not a signed one; its release and asset are as listed.
- `sheets-v1.1.1`, `sheets-v1.1.2` and `docs-v1.0.0`/`docs-v1.1.0` date from June 2026; the rest from late October and November 2026.
- Releases and their assets were checked from the GitHub release pages by the repository owner on 2026-11-06. Where a release record exists in the project docs, its hash agrees with the table.

## Platforms

- **Windows and Linux:** built and tested in CI on every change (`ubuntu-latest`, `windows-latest`). Windows-only code (Chuvadi.Print.Windows, the Xps tests) runs on Windows only.
- **Trimming and NativeAOT:** Chuvadi.Zip, Chuvadi.Fonts, Chuvadi.Docs and Chuvadi.Docs.Conversion carry `IsTrimmable` and `IsAotCompatible` on `main`; the metadata is in the package from the next release of each, not in the released Zip 1.0.0, Fonts 1.1.0 and Docs 1.5.1. CI publishes a small app using Zip and Fonts with `TrimMode=full` and as NativeAOT (linux-x64), with warnings as errors, and runs it. The other packages are not yet checked that way in CI.
- **macOS, Android, browser WebAssembly:** not tested. macOS and an Android build check are planned (D-217); browser WebAssembly is out of scope.
- See `docs/Chuvadi.Zip/PLATFORMS.md` and `docs/Chuvadi.Fonts/PLATFORMS.md` (when present) for what is verified for those two packages.

## Keeping this file correct

At every release, after the release run and asset check:

1. Move the package from "Not released yet" to "Latest release", with its tag, asset name, size and SHA-256 from `gh release view <tag> --json assets`.
2. Move the previous latest row of that package to "Older releases".
3. Update "Depends on" and the date line at the top.
