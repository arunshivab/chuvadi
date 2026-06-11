# Releasing Chuvadi.Sheets

> **Monorepo releases.** Each package releases independently via a tag prefix:
> `sheets-v1.1.1` releases Chuvadi.Sheets; `docs-v1.0.0` releases Chuvadi.Docs.
> The release workflow builds and tests the WHOLE solution (shared internals!) but packs
> and publishes only the package named by the tag prefix.


## One-time setup

1. **GPG key for signed tags** (proves releases came from you):
   ```
   gpg --full-generate-key                  # if you don't have one
   git config user.signingkey <KEY_ID>
   ```
   Upload the public key to GitHub (Settings → SSH and GPG keys) so tags show "Verified".

2. **Code-signing certificate for the NuGet package** (optional but recommended for
   public distribution). Obtain a code-signing certificate (PFX) from a CA trusted by
   NuGet.org, then add two repository secrets:
   - `SIGNING_CERT_PFX_BASE64` — `base64 -w0 your-cert.pfx`
   - `SIGNING_CERT_PASSWORD`

3. **NuGet.org publishing** (optional): create an API key on nuget.org scoped to
   `Chuvadi.Sheets` push, add it as the `NUGET_API_KEY` repository secret.

## Cutting a release

1. Update `<Version>` in `src/Chuvadi.Sheets/Chuvadi.Sheets.csproj` and add a
   `CHANGELOG.md` entry.
2. Commit, then create a **signed** tag and push it:
   ```
   git tag -s sheets-sheets-v1.1.1 -m "sheets-v1.1.1"
   git push origin main sheets-v1.1.1
   ```
3. The `Release` workflow builds, runs the full test suite, packs, signs the package
   (when the certificate secrets are configured), attaches the `.nupkg` to a GitHub
   release, and pushes to NuGet.org (when the API key is configured).

## Verifying a release (consumers)

```
git verify-tag sheets-v1.1.1                              # tag signature
dotnet nuget verify Chuvadi.Sheets.1.1.0.nupkg     # package signature
```
