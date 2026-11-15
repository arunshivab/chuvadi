# Compatibility: what is released, and what depends on what

For consumers of the Chuvadi packages (the Chuvadi Reader, other apps). **Updated at every release**, in a docs-only PR after the release is checked. Every size and SHA-256 below was computed from the downloaded release asset on 2026-11-15 and 2026-11-16 (each file downloaded from its release page and hashed again; nothing was copied from a note). The rows that earlier versions of this file listed agree with the new hashes. Last updated 2026-11-15, main `c44af96`. Chuvadi.Zip 1.0.1 is not in this file yet: its row is added after it is released.

- **How packages are delivered:** a `.nupkg` attached to the GitHub release of its tag. Nothing is on nuget.org (D-46). Use the package from a local feed. Recent release tags are signed; the packages themselves are not signed (no certificate).
- **Checksums:** releases cut after the checksum step merged (see RELEASING.md) also carry `SHA256SUMS.txt` next to the nupkgs (`sha256sum -c SHA256SUMS.txt`). Earlier releases have no such file; their digests are in this document.
- **Tags:** one tag per package, `<name>-vX.Y.Z`. The `docs-v*` tag releases Chuvadi.Docs and Chuvadi.Docs.Conversion together; the `print-v*` tag releases the four Print packages together.
- **Release page:** `https://github.com/arunshivab/chuvadi/releases/tag/<tag>`.
- **Frameworks:** every package targets plain `net10.0`.
- **Third-party dependencies:** none, in any package. The only dependencies are the Chuvadi packages named in the tables. Chuvadi.Layout.Pdf and Chuvadi.Print.Pdf.Raster depend on packages of Chuvadi.Pdf, which is our own, separate repository (arunshivab/chuvadi-pdf).
- **Licences:** MIT, except the packages that carry fonts: Chuvadi.Fonts, Chuvadi.Fonts.Liberation and Chuvadi.Layout.Fonts are `MIT AND OFL-1.1` (read from each nuspec).

## Latest release of each package

| Package | Version | Tag | Asset | Size (bytes) | SHA-256 | Depends on |
|---|---|---|---|---|---|---|
| Chuvadi.Sheets | 2.1.0 | `sheets-v2.1.0` | `Chuvadi.Sheets.2.1.0.nupkg` | 365,386 | `59b25072363b9928163918e6e2b0121258bbd92959de61b3da698b5b90d94c84` | nothing |
| Chuvadi.Docs | 2.4.0 | `docs-v2.4.0` | `Chuvadi.Docs.2.4.0.nupkg` | 415,069 | `a3fd9fe3443f993dc09ae388bfc690b719a2f69ffa77408b0b64612ba191fa8c` | nothing |
| Chuvadi.Docs.Conversion | 2.4.0 | `docs-v2.4.0` | `Chuvadi.Docs.Conversion.2.4.0.nupkg` | 70,031 | `5b09a5d20ce9d9f5bf602b1f1deee057b32ef2d37848eb10948fc979903e756b` | Chuvadi.Docs 2.4.0, Chuvadi.Markup 1.2.0 or later |
| Chuvadi.Markup | 1.2.0 | `markup-v1.2.0` | `Chuvadi.Markup.1.2.0.nupkg` | 210,834 | `4679c4b88f3d55698a64805a40b61d9f57219ba2adbc229916d006bbf69632c7` | nothing |
| Chuvadi.Zip | 1.0.1 | `zip-v1.0.1` | `Chuvadi.Zip.1.0.1.nupkg` | 51,702 | `c9dc8b358fdd57597b22c123de504c52dbd51e07bdfaacc08be81d6678c0a7b0` | nothing |
| Chuvadi.Fonts | 1.1.0 | `fonts-v1.1.0` | `Chuvadi.Fonts.1.1.0.nupkg` | 1,910,511 | `e6f5ffa6061dbab3d472c100e1e91846b33af485d2d2cb7a956902834d1f742c` | nothing |
| Chuvadi.Fonts.Liberation | 1.0.0 | `fonts-liberation-v1.0.0` | `Chuvadi.Fonts.Liberation.1.0.0.nupkg` | 2,434,535 | `6f4eb33e671dd8d9d5a2c2b4c238f333ac6b1216a8de3b8ad868a2a1d6540ba2` | Chuvadi.Fonts 1.1.0 or later |
| Chuvadi.OpenType | 0.1.0 | `opentype-v0.1.0` | `Chuvadi.OpenType.0.1.0.nupkg` | 73,387 | `f7664fdbe54e5f7122f53eb7807cc16f755e59a47221f49e9736788349b010b3` | nothing |
| Chuvadi.Layout | 0.3.0 | `layout-v0.3.0` | `Chuvadi.Layout.0.3.0.nupkg` | 247,865 | `fc2fd8691dccff89006f96f741c1d3ae089978557e02cd99855a8b45563146bf` | Chuvadi.Fonts 1.1.0 or later |
| Chuvadi.Layout.Docs | 0.3.0 | `layout-docs-v0.3.0` | `Chuvadi.Layout.Docs.0.3.0.nupkg` | 39,466 | `3e88a6c8dd7ab01868c70d64ab43ef1a7b63ba5439bd8210db87690ce89d4372` | Chuvadi.Layout 0.3.0 or later, Chuvadi.Docs 2.3.0 or later |
| Chuvadi.Layout.Sheets | 0.3.0 | `layout-sheets-v0.3.0` | `Chuvadi.Layout.Sheets.0.3.0.nupkg` | 29,097 | `29de5193c54a8943149c64efa56fb24821187b37dbdbc007881e32ed05e9e365` | Chuvadi.Layout 0.3.0 or later, Chuvadi.Sheets 2.1.0 or later |
| Chuvadi.Layout.Documents | 0.3.0 | `layout-documents-v0.3.0` | `Chuvadi.Layout.Documents.0.3.0.nupkg` | 8,001 | `9eaaf1b50ab921746dc6cf3e301e47ec0415362d6b6639089f0794cbd43509a2` | Chuvadi.Layout.Docs 0.3.0 or later, Chuvadi.Layout.Sheets 0.3.0 or later |
| Chuvadi.Layout.Fonts | 0.2.0 | `layout-fonts-v0.2.0` | `Chuvadi.Layout.Fonts.0.2.0.nupkg` | 1,795,571 | `d07bca5b7084e3c47923af7bc54286fd73cb31eaf1d9a6753b3c4e5c1068e327` | Chuvadi.Fonts 1.1.0 or later, Chuvadi.Fonts.Liberation 1.0.0 or later |
| Chuvadi.Layout.Markup | 0.1.0 | `layout-markup-v0.1.0` | `Chuvadi.Layout.Markup.0.1.0.nupkg` | 66,751 | `374ac6c0fb8da9a717c2dba58140790fbddd7c284ca8c60477a438b23f9eccf3` | Chuvadi.Layout 0.1.0 or later, Chuvadi.Markup 1.1.0 or later |
| Chuvadi.Layout.Pdf | 0.1.0 | `layout-pdf-v0.1.0` | `Chuvadi.Layout.Pdf.0.1.0.nupkg` | 20,315 | `f20601c8df31c30126f3700da076586be7321f571de8046fbec2ff5ec0e59333` | Chuvadi.Layout 0.2.0 or later; Chuvadi.Pdf.Authoring, .Documents, .Graphics, .Rendering, .Svg and .Text.Shaping 3.31.2 or later, below 4.0.0 |
| Chuvadi.Print | 1.0.0-rc.5 (pre-release) | `print-v1.0.0-rc.5` | `Chuvadi.Print.1.0.0-rc.5.nupkg` | 83,187 | `6f27b84efd4e2d5e99186c6b1c9d4c16d83c058752aca5ae308dc4200a3529e6` | nothing |
| Chuvadi.Print.Ipp | 1.0.0-rc.5 (pre-release) | `print-v1.0.0-rc.5` | `Chuvadi.Print.Ipp.1.0.0-rc.5.nupkg` | 75,633 | `21b304d2ecb00f551776c9e3703d56c49feb2db99ca53991bebcaf9056ae105d` | Chuvadi.Print 1.0.0-rc.5 or later |
| Chuvadi.Print.Remote | 1.0.0-rc.5 (pre-release) | `print-v1.0.0-rc.5` | `Chuvadi.Print.Remote.1.0.0-rc.5.nupkg` | 81,636 | `a71736110e171595927913347c9b85cc22d704589275677e19c9fa45c7c9cf57` | Chuvadi.Print 1.0.0-rc.5 or later |
| Chuvadi.Print.Windows | 1.0.0-rc.5 (pre-release) | `print-v1.0.0-rc.5` | `Chuvadi.Print.Windows.1.0.0-rc.5.nupkg` | 84,690 | `a0612d3af3f1fc009ce44b80952a7ca8e95fd379fb9c0d9ea9decedb0605866c` | Chuvadi.Print 1.0.0-rc.5 or later |
| Chuvadi.Print.Pdf.Raster | 0.2.1 | `print-pdf-raster-v0.2.1` | `Chuvadi.Print.Pdf.Raster.0.2.1.nupkg` | 18,438 | `2f8be9e59695ca3964ea6db57ebd0e969677ab677133aba8f621035abda20802` | Chuvadi.Print 1.0.0-rc.5 or later; Chuvadi.Pdf.Operations and Chuvadi.Pdf.Rendering 3.32.0 or later, below 4.0.0 |

The four Print packages are released together under one tag and one version. Chuvadi.Print.Windows loads on every platform but works only on Windows. "Depends on" is read from each package's nuspec; a version is the lowest that works.

Chuvadi.Pdf: the latest release is 3.34.0 (repository chuvadi-pdf, tag `v3.34.0`). The 34 packages of releases 3.32.0, 3.32.1, 3.33.0 and 3.34.0 were downloaded and hashed again; each hash agrees with that release's `SHA256SUMS.txt`, and the hashes are pinned in `tools/chuvadi-pdf/hashes/`.

## Not released yet

Versions are what `main` carries. A package listed here has no release page and no asset.

| Package | Version on main | Waiting for | Depends on |
|---|---|---|---|
| Chuvadi.Text.Shaping | 0.1.0 | `text-shaping-v0.1.0` (on hold; no tag exists) | Chuvadi.OpenType |

## Older releases

Each is a single `.nupkg` on its release page. Use the latest row above unless you need a specific older version.

| Tag | Asset | Size (bytes) | SHA-256 |
|---|---|---|---|
| `sheets-v2.0.5` | `Chuvadi.Sheets.2.0.5.nupkg` | 357,311 | `338cfdafdcf79fb8247a73c6d2ef2732b01f17e06a6e38817a22dc39f7fd6ad2` |
| `sheets-v2.0.4` | `Chuvadi.Sheets.2.0.4.nupkg` | 354,835 | `9b777e9518494a0531e25742c1f3fb7f8633dd9dabf44fa083cc44d0dd345d90` |
| `sheets-v2.0.3` | `Chuvadi.Sheets.2.0.3.nupkg` | 354,435 | `d881103552906ec30b1a0eada45f81b580c880dc568626c127ee2cab50589a3c` |
| `sheets-v2.0.2` | `Chuvadi.Sheets.2.0.2.nupkg` | 353,606 | `a137b5c84c431afdc6e872768fb4d79211e0fca9b0e8ee28f35c976db5944f99` |
| `sheets-v2.0.1` | `Chuvadi.Sheets.2.0.1.nupkg` | 352,515 | `417f537f74826deeb8cd14492441800243b45df9e714cd9ce6daf9138571c253` |
| `sheets-v2.0.0` | `Chuvadi.Sheets.2.0.0.nupkg` | 346,714 | `17d0d0beefc09b60da9c2a472a1a42ac5040579aebb28ac7a95926a9e2db137a` |
| `sheets-v1.8.0` | `Chuvadi.Sheets.1.8.0.nupkg` | 344,173 | `0afe6f38d7e40c7fc91606103e84ebd802021d696c3df145eeb72edd831a270a` |
| `sheets-v1.7.0` | `Chuvadi.Sheets.1.7.0.nupkg` | 343,716 | `30f260c9c72e263ebcae5f465631a154f11dc6745eefa25d5a930aedb460040e` |
| `sheets-v1.2.0` | `Chuvadi.Sheets.1.2.0.nupkg` | 158,293 | `e07b053180d3549c484ded85fa8786b246abcdbdca1502b0df2acf56f59d45bd` |
| `sheets-v1.1.2` | `Chuvadi.Sheets.1.1.2.nupkg` | 124,433 | `daaebe4702139fdd6b74ee97e225094e10f8762861a54fd6949ac43a618dc8d4` |
| `sheets-v1.1.1` | `Chuvadi.Sheets.1.1.1.nupkg` | 123,944 | `d51bb7850bb68b53a08bd1d4629d61eeb9bbd3848bd7db76f5985d199a1ad42a` |
| `docs-v2.3.0` | `Chuvadi.Docs.2.3.0.nupkg` | 409,286 | `a72323c036c09f16bd4c0ba55dd68ef472ce5897ab5320a61091ff3564a6eb92` |
| `docs-v2.2.0` | `Chuvadi.Docs.2.2.0.nupkg` | 403,868 | `69c6451c188a169adf2fbfcf694a760204800699e2bee6227b3a7a329a0ee47d` |
| `docs-v2.1.0` | `Chuvadi.Docs.2.1.0.nupkg` | 391,512 | `687703a2605f3cf2ea5eab4ac12d3efde40d85c6873a2848f72ce0d556fff67f` |
| `docs-v2.0.0` | `Chuvadi.Docs.2.0.0.nupkg` | 362,339 | `35e4ddb3d7bac2cd5d78ac90ab437e2a92038a97777f506870b12a4891e1d087` |
| `docs-v1.5.1` | `Chuvadi.Docs.1.5.1.nupkg` | 273,471 | `102b682a5820110cd547ea648db6d87dedbf57906ef33d2a1a26730c4df1c265` |
| `docs-v1.4.0` | `Chuvadi.Docs.1.4.0.nupkg` | 232,723 | `cd933e8bde259ac00f0b363898deaa4b2f105433dad9461cb272903553bc37ab` |
| `docs-v1.3.0` | `Chuvadi.Docs.1.3.0.nupkg` | 179,141 | `dc8d1c6143c6970baed8ecb02e95eb0d55d90996a3e02cef9b026e4e0fd818f9` |
| `docs-v1.2.0` | `Chuvadi.Docs.1.2.0.nupkg` | 137,599 | `e5e4d5d4471ac639bcd51dd767c98f7e2a6012cd16524a6bc794547dbad7b6f5` |
| `docs-v1.1.0` | `Chuvadi.Docs.1.1.0.nupkg` | 85,203 | `a90463271109004816167f256d24db2059fcb431653a3b5158dd9e45120ad2b9` |
| `docs-v1.0.0` | `Chuvadi.Docs.1.0.0.nupkg` | 59,146 | `9537b471c4f2ea7f203a8ad40237a301a0119934206beea5e65841dd32128f90` |
| `docs-v2.3.0` | `Chuvadi.Docs.Conversion.2.3.0.nupkg` | 70,032 | `c843612ec2c776c512f8d6fe4f40bbd6f08ff0db642beb874d8b26a4111ad3ad` |
| `docs-v2.2.0` | `Chuvadi.Docs.Conversion.2.2.0.nupkg` | 70,042 | `6c2bfcaf0e7acdf114af083169af50653b37b2c995f5ef525dc18acfa2cceb27` |
| `docs-v2.1.0` | `Chuvadi.Docs.Conversion.2.1.0.nupkg` | 70,264 | `04229f91b21c40bc54877b27d9f82247078bcd96efd900ae62c4daae1bc4372a` |
| `docs-v2.0.0` | `Chuvadi.Docs.Conversion.2.0.0.nupkg` | 70,265 | `5a4d530af2fa67264eeb2f83cb2b376222d3948199b1de800f25873efad2f1c7` |
| `markup-v1.1.0` | `Chuvadi.Markup.1.1.0.nupkg` | 207,954 | `b5abe40df907715d333eca216b26bd89f2e8688d6ff87390cb86df8cbe26950e` |
| `markup-v1.0.0` | `Chuvadi.Markup.1.0.0.nupkg` | 197,659 | `3257478005d2a159e8cf9d88899876dd75a419b8d2cd7d7d6b6754f9213a4192` |
| `fonts-v1.0.0` | `Chuvadi.Fonts.1.0.0.nupkg` | 1,908,123 | `c15a82d1caa3d70f3e2abe16dd94201ea1d3dae91764735a19ffa7398c37f5fe` |
| `layout-v0.2.0` | `Chuvadi.Layout.0.2.0.nupkg` | 248,125 | `6b763363818008a8754230fa283cc3b7e3cefa16a5283eaea3cb1d5ab717c159` |
| `layout-v0.1.0` | `Chuvadi.Layout.0.1.0.nupkg` | 243,576 | `60986a652e66e25911ec1b37cd1cc227c028fa54c678177cc204566ec0b38fb9` |
| `layout-docs-v0.2.0` | `Chuvadi.Layout.Docs.0.2.0.nupkg` | 39,526 | `8441f9bbde19f1255b0467626ade50fb13e98ab42a8113ebc48097648f766d25` |
| `layout-docs-v0.1.0` | `Chuvadi.Layout.Docs.0.1.0.nupkg` | 40,094 | `08ca2440fdc9e7250ce12aebd877c6207737eb78a15d5749c4bb185008fe3138` |
| `layout-sheets-v0.2.0` | `Chuvadi.Layout.Sheets.0.2.0.nupkg` | 29,124 | `2de81ccc8969966fedf1a8ae7143adadf8bb795463a4c7138061a7c85a488c21` |
| `layout-sheets-v0.1.0` | `Chuvadi.Layout.Sheets.0.1.0.nupkg` | 28,718 | `4f83d4e68f0c28955987de2729c5677868b176765841b0d863bbe47a3ee27497` |
| `layout-documents-v0.2.0` | `Chuvadi.Layout.Documents.0.2.0.nupkg` | 8,000 | `27d1a33639c17ed6cf7864407287af7e00c31006acaf19c27cc0f0d29cd3d400` |
| `layout-documents-v0.1.0` | `Chuvadi.Layout.Documents.0.1.0.nupkg` | 7,704 | `a0a70b5c6cb87dff7453d7a1dcc7749196e035fb58bbcaf1c499760b21f5a6da` |
| `print-v1.0.0-rc.4` | `Chuvadi.Print.1.0.0-rc.4.nupkg` | 81,305 | `97e2db629d68163887d30cde47c0ee154638720bc96d59b8d78f4ce442ba422d` |
| `print-v1.0.0-rc.3` | `Chuvadi.Print.1.0.0-rc.3.nupkg` | 78,489 | `9fb374bc2186d2cefcbd0430844270865f17faf6daec5333127b4f75fdcac039` |
| `print-v1.0.0-rc.2` | `Chuvadi.Print.1.0.0-rc.2.nupkg` | 58,543 | `252b6d10204d37069a35040ea89d58e1662a452c4e05fd90a0d8b90cd1049cbf` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.1.0.0-rc.1.nupkg` | 46,825 | `b04b706032d3d3de20afd9fbfa6a14a60846fb410aa237ba5e2c628d2dc2a321` |
| `print-v1.0.0-rc.4` | `Chuvadi.Print.Ipp.1.0.0-rc.4.nupkg` | 75,627 | `1f31f46a31b1bd06394a534d362dd7217ab8050c676e79d1e7aec080f4caf79b` |
| `print-v1.0.0-rc.3` | `Chuvadi.Print.Ipp.1.0.0-rc.3.nupkg` | 67,188 | `196df05f3babfbbcaa11da8f22ab59b34e25a32967e5ca46ceaac8edf11173db` |
| `print-v1.0.0-rc.2` | `Chuvadi.Print.Ipp.1.0.0-rc.2.nupkg` | 65,855 | `82304b782ce23ac5dcb3895e1fee31898abf3c22222f9f9c65c423fba68aa562` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.Ipp.1.0.0-rc.1.nupkg` | 46,199 | `ebbf36f247e57fd58fb23f3603bbce9ab9752594f24c5385f5eb2cb9f8931f65` |
| `print-v1.0.0-rc.4` | `Chuvadi.Print.Remote.1.0.0-rc.4.nupkg` | 77,577 | `a2984be603e6a5cf948e8d803287603156266fa2572fcf7ab81b1a62f9ec60b3` |
| `print-v1.0.0-rc.3` | `Chuvadi.Print.Remote.1.0.0-rc.3.nupkg` | 77,310 | `894255e354efb3d00fc40c200f240f3ab3bce8e8cf44e4570fa3031d6c5d711b` |
| `print-v1.0.0-rc.2` | `Chuvadi.Print.Remote.1.0.0-rc.2.nupkg` | 55,302 | `973885310888f3ec683633e1e94db1950d30ab0a7c0c2a07df11678794e7ebbf` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.Remote.1.0.0-rc.1.nupkg` | 55,185 | `873544780f51b49f05083b140d1ff83af65f6b96ac5209db11fd1f5cf451c8fb` |
| `print-v1.0.0-rc.4` | `Chuvadi.Print.Windows.1.0.0-rc.4.nupkg` | 84,667 | `19eacd1334a575ecddbffaa769a543f8f4fa873466fd80485a4e157cc4ecc5da` |
| `print-v1.0.0-rc.3` | `Chuvadi.Print.Windows.1.0.0-rc.3.nupkg` | 82,604 | `98234eee541a31cf7fb9ab457569689cbfb58cc37c673e5bd92586e4470104a7` |
| `print-v1.0.0-rc.2` | `Chuvadi.Print.Windows.1.0.0-rc.2.nupkg` | 31,231 | `df1ab37a48786ca5f1822e033b2f0fd45ba7339d9850140365360defb527b375` |
| `print-v1.0.0-rc.1` | `Chuvadi.Print.Windows.1.0.0-rc.1.nupkg` | 29,072 | `d1ca4b997fa18774c9db92f0a3710dce21ed451afaa93364071c054e08c7a3a9` |
| `print-pdf-raster-v0.2.0` | `Chuvadi.Print.Pdf.Raster.0.2.0.nupkg` | 18,310 | `1df9993bb08fad002ee65ab39ec8118cc5b7eb79ed10731145f82a52357df5dd` |
| `print-pdf-raster-v0.1.0` | `Chuvadi.Print.Pdf.Raster.0.1.0.nupkg` | 17,848 | `b67b533afe3b108dfd1e32b44a2b71bdf86223ad13851f6799a43685c7e4cec0` |

Notes:

- `docs-v1.1.0` is a lightweight tag, not a signed one; its release and asset are as listed.
- `docs-v1.5.1` is the last Docs release before 2.0.0 (`docs-v1.6.0` was never tagged; its content ships in 2.0.0). Chuvadi.Docs.Conversion has no release before `docs-v2.0.0`.
- `sheets-v1.1.1`, `sheets-v1.1.2` and `docs-v1.0.0`/`docs-v1.1.0` date from June 2026; the rest from late October and November 2026.
- The `layout-pdf-v0.1.0` and `print-pdf-raster-v*` releases attach one nupkg each; the Chuvadi.Pdf packages they need come from the chuvadi-pdf release named in the release notes.

## Platforms

- **Windows and Linux:** built and tested in CI on every change (`ubuntu-latest`, `windows-latest`). Windows-only code (Chuvadi.Print.Windows, the Xps tests) runs on Windows only.
- **macOS and Android:** CI also runs the tests on `macos-latest` and builds a smoke library for the Android target framework. The Android build now fails the workflow when it fails (it passed in the last six main runs). The macOS job still does not: it failed once in those six runs, on a memory-size check in the Sheets manual tests (222 bytes per cell against a limit of 200), so it stays non-gating until that check is made robust (D-233). Neither is a required branch-protection check, which is a GitHub setting. Nothing has run on a device or emulator. Browser WebAssembly is out of scope.
- **Trimming and NativeAOT:**
  - The released DLLs of Chuvadi.Docs 2.4.0, Chuvadi.Docs.Conversion 2.4.0, Chuvadi.Markup 1.2.0, Chuvadi.Sheets 2.1.0, Chuvadi.Zip 1.0.1, Chuvadi.Layout 0.3.0, Chuvadi.Layout.Docs 0.3.0, Chuvadi.Layout.Sheets 0.3.0, Chuvadi.Layout.Documents 0.3.0, Chuvadi.Layout.Fonts 0.2.0, Chuvadi.OpenType 0.1.0, Chuvadi.Print 1.0.0-rc.5 and Chuvadi.Fonts.Liberation 1.0.0 carry the `IsTrimmable` and `IsAotCompatible` metadata (checked in the DLLs). **Chuvadi.Fonts 1.1.0 and Chuvadi.Print.Pdf.Raster 0.2.1 do not**: Chuvadi.Fonts carries the flags on `main` and they come with its next release. The other released DLLs were not checked.
  - CI publishes a small app that uses Zip, Fonts, Fonts.Liberation and the four Print packages with `TrimMode=full` and as NativeAOT (linux-x64), warnings as errors, and runs it. The other packages are not yet covered that way in CI.
  - **ReadyToRun: verified on `linux-x64` only.** The non-gating CI job `r2r-smoke` publishes the same app with `PublishReadyToRun=true` (warnings are errors) and runs it. It was green on the main branch (run 43760060337, 16 November 2026) and on the pull request before it. Other platforms are not run, and the packages not used by that app are not covered.
- See `docs/Chuvadi.Zip/PLATFORMS.md` and `docs/Chuvadi.Fonts/PLATFORMS.md` (when present) for what is verified for those two packages.

## Keeping this file correct

At every release, after the release run and asset check:

1. Move the package from "Not released yet" to "Latest release", with its tag, asset name, size and SHA-256. Download the asset and hash it yourself; where the release has a `SHA256SUMS.txt`, check that they agree.
2. Move the previous latest row of that package to "Older releases".
3. Update "Depends on" (read it from the nuspec) and the date line at the top.
| `zip-v1.0.0` | `Chuvadi.Zip.1.0.0.nupkg` | 51,674 | `e019f9b61d48d705e9b8e81e3ca8bcbb3114056b2127f4e9ea2af0aa51110104` |
| `layout-fonts-v0.1.0` | `Chuvadi.Layout.Fonts.0.1.0.nupkg` | 4,213,184 | `6634b45667083354a7505da3d2ade4c8bcde951ff26b1e2f530ebd015ac03cc4` |
