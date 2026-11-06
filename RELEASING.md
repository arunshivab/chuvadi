# Releasing Chuvadi packages

> **Monorepo releases.** Each package releases independently via a tag prefix. The release
> workflow builds and tests the WHOLE solution (shared internals!) but packs only the
> package(s) named by the tag prefix, and attaches them to a GitHub release.

## Tag prefixes

| Tag prefix | Releases | Version is read from |
|---|---|---|
| `sheets-v*` | Chuvadi.Sheets | `src/Chuvadi.Sheets/Chuvadi.Sheets.csproj` |
| `docs-v*` | Chuvadi.Docs **and** Chuvadi.Docs.Conversion, at the same version | `src/Chuvadi.Docs/Chuvadi.Docs.csproj` and `src/Chuvadi.Docs.Conversion/Chuvadi.Docs.Conversion.csproj` |
| `print-v*` | Chuvadi.Print **and** Chuvadi.Print.Windows, Chuvadi.Print.Ipp and Chuvadi.Print.Remote, at the same version | the four `.csproj` files under `src/Chuvadi.Print*/` |
| `zip-v*` | Chuvadi.Zip | `src/Chuvadi.Zip/Chuvadi.Zip.csproj` |
| `markup-v*` | Chuvadi.Markup | `src/Chuvadi.Markup/Chuvadi.Markup.csproj` |
| `fonts-v*` | Chuvadi.Fonts | `src/Chuvadi.Fonts/Chuvadi.Fonts.csproj` |
| `layout-v*` | Chuvadi.Layout | `src/Chuvadi.Layout/Chuvadi.Layout.csproj` |
| `layout-docs-v*` | Chuvadi.Layout.Docs | `src/Chuvadi.Layout.Docs/Chuvadi.Layout.Docs.csproj` |
| `layout-sheets-v*` | Chuvadi.Layout.Sheets | `src/Chuvadi.Layout.Sheets/Chuvadi.Layout.Sheets.csproj` |
| `layout-documents-v*` | Chuvadi.Layout.Documents | `src/Chuvadi.Layout.Documents/Chuvadi.Layout.Documents.csproj` |
| `layout-fonts-v*` | Chuvadi.Layout.Fonts | `src/Chuvadi.Layout.Fonts/Chuvadi.Layout.Fonts.csproj` |
| `layout-markup-v*` | Chuvadi.Layout.Markup | `src/Chuvadi.Layout.Markup/Chuvadi.Layout.Markup.csproj` |
| `opentype-v*` | Chuvadi.OpenType | `src/Chuvadi.OpenType/Chuvadi.OpenType.csproj` |
| `text-shaping-v*` | Chuvadi.Text.Shaping | `src/Chuvadi.Text.Shaping/Chuvadi.Text.Shaping.csproj` |

Chuvadi.Docs.Conversion has no tag of its own: it rides on `docs-v*`. Chuvadi.Print.Windows, .Ipp and
.Remote ride on `print-v*`. The workflow refuses a `docs-v*` or `print-v*` tag when any `<Version>` of the
packages it releases differs from the tag version. The same check runs for `opentype-v*` and `text-shaping-v*`
(one package each).

**Release notes.** The GitHub release body is the `## <version>` section of each released package's
`src/<package>/CHANGELOG.md` (the heading must start with the exact tag version, for example `## 1.0.0 (date)`),
not the generated list of every pull request since the previous tag. If no section is found, the body is a one-line
pointer to the changelog. Write the section before you tag.

A longer prefix is listed before a shorter one that it starts with (`layout-documents-v*`,
`layout-docs-v*`, `layout-sheets-v*`, `layout-fonts-v*` and `layout-markup-v*` before `layout-v*`), so each tag
resolves to exactly one package set. (`layout-docs-v*` cannot match `layout-documents-v…`, and `fonts-v*` cannot
match `layout-fonts-v…`: a case pattern matches from the start.)

## Release order

A package must be released after the packages it depends on. Chuvadi.Layout references
Chuvadi.Fonts only; Chuvadi.Layout.Docs references Chuvadi.Layout and Chuvadi.Docs, Chuvadi.Layout.Sheets
Chuvadi.Layout and Chuvadi.Sheets, Chuvadi.Layout.Documents both of those; Chuvadi.Layout.Fonts and
Chuvadi.Layout.Markup reference Chuvadi.Layout (Layout.Markup also Chuvadi.Markup). Chuvadi.OpenType has no
dependencies; Chuvadi.Text.Shaping references Chuvadi.OpenType only:

1. `zip-v*`
2. `markup-v*`
3. `docs-v*` (version pending Arun; Docs.Conversion needs Chuvadi.Markup released first)
4. `fonts-v*`
5. `layout-v*`
6. `layout-docs-v*`, `layout-sheets-v*`, `layout-fonts-v*`, `layout-markup-v*` (any order)
7. `layout-documents-v*`
8. `opentype-v*`, then `text-shaping-v*` (OpenType first; both after `fonts-v*`, and independent of the Layout packages)

(`zip-v1.0.0` and `sheets-v2.0.1` are already released. `print-v*` is not scheduled: the Print packages are in progress.
Chuvadi.Print.Windows, .Ipp and .Remote depend on Chuvadi.Print, and they release with it.)
Push one tag, wait for its Release workflow to finish green, then push the next. Create each tag
on a commit whose `release.yml` already knows the prefix: a tag runs the workflow file that exists
AT the tagged commit.

## One-time setup

1. **GPG key for signed tags** (proves releases came from you):
   ```
   gpg --full-generate-key                  # if you don't have one
   git config user.signingkey <KEY_ID>
   ```
   Upload the public key to GitHub (Settings → SSH and GPG keys) so tags show "Verified".

2. **Code-signing certificate for the NuGet package** (optional). Obtain a code-signing
   certificate (PFX) from a CA trusted by NuGet.org, then add two repository secrets:
   - `SIGNING_CERT_PFX_BASE64` — `base64 -w0 your-cert.pfx`
   - `SIGNING_CERT_PASSWORD`

   Without these secrets the signing step is skipped; the release does not fail.

3. **NuGet.org publishing: disabled for now.** Releases are GitHub release attachments only.
   The workflow has no nuget.org push step and needs no `NUGET_API_KEY`. It is added back
   (with the key as a repository secret) once the licence is decided.

## Cutting a release

1. Update `<Version>` in the package's `.csproj` (for `docs-v*`: in BOTH Docs and
   Docs.Conversion) and add a `CHANGELOG.md` entry.
2. Commit and merge through a pull request, then create a **signed** tag on main and push it:
   ```
   git tag -s <prefix>-vX.Y.Z -m "<prefix>-vX.Y.Z"
   git push origin <prefix>-vX.Y.Z
   ```
3. The `Release` workflow builds, runs the full test suite, packs, signs the package(s)
   (when the certificate secrets are configured) and attaches the `.nupkg` file(s) to a
   GitHub release.

## Verifying a release (consumers)

```
git verify-tag sheets-v1.7.0                       # tag signature
dotnet nuget verify Chuvadi.Sheets.1.7.0.nupkg     # package signature (when signed)
```
