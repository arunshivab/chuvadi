# Releasing Chuvadi packages

> **Monorepo releases.** Each package releases independently via a tag prefix. The release
> workflow builds and tests the WHOLE solution (shared internals!) but packs only the
> package(s) named by the tag prefix, and attaches them to a GitHub release.

## Tag prefixes

| Tag prefix | Releases | Version is read from |
|---|---|---|
| `sheets-v*` | Chuvadi.Sheets | `src/Chuvadi.Sheets/Chuvadi.Sheets.csproj` |
| `docs-v*` | Chuvadi.Docs **and** Chuvadi.Docs.Conversion, at the same version | `src/Chuvadi.Docs/Chuvadi.Docs.csproj` and `src/Chuvadi.Docs.Conversion/Chuvadi.Docs.Conversion.csproj` |
| `print-v*` | Chuvadi.Print | `src/Chuvadi.Print/Chuvadi.Print.csproj` |
| `zip-v*` | Chuvadi.Zip | `src/Chuvadi.Zip/Chuvadi.Zip.csproj` |
| `markup-v*` | Chuvadi.Markup | `src/Chuvadi.Markup/Chuvadi.Markup.csproj` |
| `layout-v*` | Chuvadi.Layout | `src/Chuvadi.Layout/Chuvadi.Layout.csproj` |
| `layout-fonts-v*` | Chuvadi.Layout.Fonts | `src/Chuvadi.Layout.Fonts/Chuvadi.Layout.Fonts.csproj` |
| `layout-markup-v*` | Chuvadi.Layout.Markup | `src/Chuvadi.Layout.Markup/Chuvadi.Layout.Markup.csproj` |

Chuvadi.Docs.Conversion has no tag of its own: it rides on `docs-v*`. The workflow refuses a
`docs-v*` tag when the two `<Version>` values differ from the tag version.

A longer prefix is listed before a shorter one that it starts with (`layout-fonts-v*` and
`layout-markup-v*` before `layout-v*`), so each tag resolves to exactly one package set.

## Release order

A package must be released after the packages it depends on. Chuvadi.Layout references
Chuvadi.Docs and Chuvadi.Sheets; Chuvadi.Layout.Fonts and Chuvadi.Layout.Markup reference
Chuvadi.Layout (Layout.Markup also Chuvadi.Markup):

1. `zip-v*`
2. `markup-v*`
3. `docs-v*` (version pending Arun; Docs.Conversion needs Chuvadi.Markup released first)
4. `layout-v*`
5. `layout-fonts-v*`
6. `layout-markup-v*`

(`sheets-v1.7.0` is already released. `print-v*` is not scheduled: Chuvadi.Print is in progress.)
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
