# Chuvadi.Sheets — Full Audit Report & Remediation Record

**Subject:** `github.com/arunshivab/chuvadi` (C# / .NET 10, pure-BCL xlsx + zip library)
**Report date:** 2026-06-11 (updated same day for v1.1.0)
**Scope:** Security, compliance, IP (patent/trademark/copyright), cybersecurity, capability
assessment, and code-comment review — followed by implementation of all remediations
(v1.0.0) and of the audit's engineering recommendations (v1.1.0), with re-verification
and release packaging.

**Method:** Cloned and built from source (.NET 10 SDK, `TreatWarningsAsErrors`, 0 warnings /
0 errors), read all 55 source files (~11,000 lines), executed the full verification suite,
and independently cross-validated encrypted output with `msoffcrypto-tool` (a third-party
implementation of Microsoft's [MS-OFFCRYPTO] specification) and `openpyxl`.

---

## 1. Security, Compliance & IP Audit

### 1.1 Overview

Chuvadi.Sheets writes and reads `.xlsx` files and zip archives using **only the .NET base
class library — zero NuGet dependencies**. MIT licensed. The verification suite covers
write/read round-trips, a 100,000-row streaming stress test, zip-slip attack tests, and
encryption round-trips including wrong-password and missing-password paths.

### 1.2 Cybersecurity — strengths

- **Zero-dependency supply chain.** No NuGet packages → no transitive CVEs, no
  dependency-confusion or typosquatting exposure. The attack surface is the .NET runtime.
- **Zip-slip protection on both sides.** Extraction canonicalizes via `Path.GetFullPath`
  and refuses entries resolving outside the target; the writer rejects `..` segments,
  absolute paths, and drive letters. Verified by attack tests in the suite.
- **No XML injection.** All workbook XML is emitted through `XmlWriter` (automatic
  escaping). The one hand-built XML stream (EncryptionInfo) embeds only base64/integers.
- **No XXE.** All parsing uses `XmlReader` with modern .NET defaults (DTD prohibited,
  no external resolver).
- **Sound cryptography, correctly sourced.** AES-256-CBC, iterated SHA-512 KDF
  (100,000 iterations), HMAC-SHA512 — all BCL primitives; CSPRNG randomness
  (`RandomNumberGenerator`); constant-time comparisons
  (`CryptographicOperations.FixedTimeEquals`). No homemade ciphers.
- **Honest threat modelling in docs** — sheet protection explicitly documented as
  cooperative ("polite") locking, not confidentiality.

### 1.3 Cybersecurity — findings and resolution status

| # | Severity | Finding | Status |
|---|---|---|---|
| 1 | **High** | HMAC computed on encrypt but **never verified on decrypt** — tampered ciphertext decrypted without integrity detection. | **RESOLVED (v1.0.0).** `AgileEncryption.Decrypt` now decrypts the stored HMAC key/value and verifies the HMAC-SHA512 over the encrypted segment in constant time before returning plaintext; failure throws a clear integrity error. Regression test added (tampered-ciphertext rejection + clean control). Independently confirmed: `msoffcrypto-tool`'s own integrity verification passes on library output. |
| 2 | **Medium** | Content-key derivation hard-coded the default spin count — files from other producers with non-default spin counts would pass password verification then decrypt to garbage. | **RESOLVED (v1.0.0).** `DecryptKeyValue`/`EncryptKeyValue`/`EncryptVerifier` now take and honor the file's spin count. |
| 3 | **Medium** | `EncryptionOptions.SpinCount` silently ignored on save (dead parameter in `EncryptedPackageWriter`). | **RESOLVED (v1.0.0).** Plumbed end-to-end; regression test writes with `SpinCount = 150,000`, asserts the value appears in EncryptionInfo, and round-trips. Cross-validated: msoffcrypto decrypts the 150k-iteration file with integrity OK. |
| 4 | Low | Plaintext staging: streaming writer spools sheet XML to OS temp files; encrypted save buffers the plaintext package in memory; passwords are non-zeroable .NET strings. | **Documented (v1.0.0).** Now disclosed in README "Security notes for integrators." Architecture change (streaming encryption, SecureString-style handling) deferred as a design decision; standard for the .NET ecosystem. |
| 5 | Low | Hostile-input robustness: CFB directory `nameLen` unbounded against its 64-byte field; no DIFAT-extension support (files beyond ~7 MB of FAT coverage unreadable — and the comment claimed "~55 MB," an 8× miscalculation); no decompression-bomb limits on untrusted archives. | **PARTIALLY RESOLVED (v1.0.0).** `nameLen` now bounds-checked; DIFAT sector chains now followed (large encrypted files readable); capacity comment corrected. Decompression-size limits remain caller responsibility — documented in README. FAT/MiniFAT cycle protection already existed (verified). |
| 6 | Low | Process gaps: no CI, no unit-test framework (manual console suite), no SECURITY.md, single squashed commit, unsigned artifacts. | **Open (recommendation).** Add GitHub Actions running the suite per push; port the manual tests to xUnit; add SECURITY.md; sign tags/packages before public NuGet publication. |

No malicious code, network calls, telemetry, obfuscation, or committed secrets were found.

### 1.4 Compliance

- **Licensing:** MIT — permissive, commercial-friendly. Zero dependencies → no third-party
  notices, no copyleft exposure, a one-component SBOM. MIT carries no express patent grant;
  consider Apache-2.0 if downstream patent assurance matters. Per-file headers absent
  (not required by MIT).
- **Encryption export control:** the library implements AES-256, making it cryptographic
  software under several export regimes (e.g., US EAR Cat. 5 Pt. 2 if distributed from the
  US; publicly available open-source typically qualifies for the open-source publication
  route, sometimes with a one-time notification). Indian law is permissive for standard
  crypto. Obtain legal review before commercial distribution — this report is not legal advice.
- **Data protection (GDPR/DPDP):** the library processes no personal data itself and makes
  no network calls; the temp-file staging (Finding 4) should be noted in any DPIA where
  personal data flows through it.

### 1.5 Patent, trademark, copyright

- **Patents — low risk.** OOXML (ECMA-376/ISO 29500), [MS-OFFCRYPTO], and [MS-CFB] are
  published Microsoft open specifications covered by Microsoft's Open Specification
  Promise (an irrevocable promise not to assert necessary patent claims against conforming
  implementations). The zip format is freely implementable per PKWARE's APPNOTE with
  foundational patents long expired. AES/SHA-512/HMAC/PBKDF2 are open standards.
- **Copyright — original work.** No verbatim copying detected from EPPlus, ClosedXML,
  NPOI, or similar; shared byte constants are spec-defined facts of the file format, not
  copyrightable expression. Internal artifacts indicating AI-assisted generation and a
  prior internal codename were present in the public text; both have been scrubbed in
  v1.0.0. Note: under current US Copyright Office guidance, purely AI-generated material
  is not itself copyrightable (human selection/arrangement may be); this does not affect
  users' rights under the MIT license.
- **Trademark — low conflict risk, unverified formally.** "Chuvadi" is a generic Tamil
  word (சுவடி, palm-leaf manuscript); searches found no software product using the name
  (chuvadi.com is an unrelated literary site). A web search is not a clearance search —
  run formal checks (Indian Trade Marks Registry classes 9/42; USPTO) before commercial
  branding. References to "Excel" are nominative compatibility statements and acceptable;
  do not use Microsoft logos or imply endorsement.

---

## 2. Capability Assessment vs. Requirements

| Requirement | Verdict | Detail |
|---|---|---|
| Export all kinds of tables to Excel | **Yes** | `IEnumerable<T>` (reflection + attribute/lambda config), `DataTable`, multi-sheet; one-liner, fluent, and streaming tiers; verified at 100,000 rows. Styles, formats, formulas, merges, autofilter, structured tables with filter UI, freeze panes, hyperlinks, comments, data validation, conditional formatting, defined names. Not supported: charts, pivots, images, rich text. |
| …with header & footer | **Yes (v1.0.0)** | Column header rows were already supported. **Page (print) headers/footers were absent and have been implemented:** `SheetWriter.SetHeaderFooter`, `Sheet.SetHeaderFooter`, and `PageHeaderFooter(...)` on both export configs emit the OOXML `<headerFooter>` element with full Excel code support (`&L/&C/&R`, `&P`, `&N`, `&D`, `&T`, `&F`, `&A`). Verified: openpyxl parses the emitted header/footer sections correctly. |
| Zip a set of files | **Yes** | `ZipWriter` (file/text/bytes/stream, sync + async), `ZipDirectory`, listing, extraction with zip-slip protection. All verified by the suite. |
| Open & export Excel with password | **Yes** | `wb.SaveTo(path, new EncryptionOptions { Password })` produces genuine AES-256 agile-encrypted files; `Workbook.Load(path, password)` opens them; wrong/missing password throws `XlsxPasswordRequiredException`. Independently cross-validated with msoffcrypto-tool (decryption **and** integrity verification pass). Sheet/workbook protection also available. Limit: encryption goes through the in-memory `Workbook` model (not the streaming writer). |
| Password-protect zip files | **No (by design)** | Deliberately excluded: classic ZipCrypto is cryptographically broken and AES-zip is not in the .NET BCL (adding it would break the zero-dependency rule). Use workbook encryption, or pair with a third-party AES-zip library if hard-required. Documented in README. |

---

## 3. Code-Comment Review & Edits Applied

**Overall:** comment quality is well above open-source norms — spec-citing
([MS-OFFCRYPTO] §refs, ECMA schema constraints), "why"-oriented, with honest documentation
of sharp edges (KDF iteration-order warnings, Excel's reserved style slots, post-decryption
strict validation requirements). All of that was preserved. The defects were accuracy drift
and leftover scaffolding; every itemized edit below was applied in v1.0.0:

1. **`AgileEncryption.cs` summary** — "50000 iterations default" corrected to 100,000;
   "PBKDF2" reworded to "PBKDF2-style iterated SHA-512" (the scheme is MS-OFFCRYPTO's
   counter-prefixed iterated hash, not RFC 2898 PBKDF2). README wording aligned.
2. **`CfbContainer.cs`** — "~55 MB" DIFAT capacity corrected to ~7 MB for 512-byte
   sectors; reader comment updated to reflect the new DIFAT-chain support.
3. **`WorkbookModel.cs`** — stale "step 5 / no Load yet" paragraph replaced; a public
   performance warning added (sparse sheets emit gap-filling rows — use the streaming
   writer for huge sparse data).
4. **`Workbook.Load` docs** — previously overstated preservation (styles, widths, freezes,
   merges, defined names); rewritten to truthfully state value-only round-trips with
   formulas loading as cached values.
5. **`SheetWriter.cs` dxfs comments** — contradictory "wait for step 8" narrative replaced
   with an accurate statement that dxfs are out of scope; the `CellIs` rendering limitation
   is now surfaced in the public docs of `ConditionalFormat.CellIs` and the README.
6. **Codename/AI leaks** — `"Generated by SIGMA"` (doc example + three test references)
   replaced with `"Generated by Chuvadi.Sheets"`; README's "Claude-produced" reworded;
   the encryption-verification note updated to reflect independent third-party validation.
7. **`EncryptionOptions.SpinCount` docs** — now describe a knob that actually works
   (fixed in code, Finding 3) with corrected terminology.
8. **Step-N scaffolding retired** — README rewritten around capabilities; build-sequence
   history moved to `CHANGELOG.md`; user-visible "step N" strings removed from test output
   and headers; leftover planning narration deleted.
9. **Hygiene** — vestigial `[Serializable]` removed from `XlsxPasswordRequiredException`;
   dead `hmacIv` local (and its misleading comment) deleted; missing XML `<param>` tags
   completed so the now-generated IntelliSense doc file builds warning-free.

---

## 4. Verification Evidence (post-remediation)

- Release build: **0 warnings, 0 errors** under `TreatWarningsAsErrors`.
- Full suite: **all test groups pass**, including three new regression tests —
  tampered-ciphertext rejection (HMAC), custom spin-count round-trip (150,000 iterations,
  value asserted inside EncryptionInfo), and `<headerFooter>` emission via both the model
  API and the ergonomic export config.
- Independent cross-validation: `msoffcrypto-tool` decrypts library-encrypted files **with
  its own integrity verification passing** (default and custom spin counts); `openpyxl`
  opens the decrypted packages and correctly parses the emitted page headers/footers.
- Final verification of encrypted files in desktop Microsoft Excel remains recommended
  before production rollout (no real Excel was available in the audit environment).

## 5. Recommendations — implementation status (v1.1.0)

1. **CI + xUnit — IMPLEMENTED.** `.github/workflows/ci.yml` builds (warnings-as-errors),
   runs the xUnit suite and the full end-to-end suite on Linux and Windows, enforces the
   pure-BCL rule for the shipped library, and packs the NuGet artifact on every push and
   pull request. `tests/Chuvadi.Sheets.Tests` drives every verification group as
   individual xUnit facts plus targeted unit tests (tamper rejection, stream/array
   encryption equivalence, header/footer XML, wrong-password handling).
2. **SECURITY.md + signing — IMPLEMENTED.** SECURITY.md documents the private disclosure
   channel, response expectations, and the security model. `.github/workflows/release.yml`
   provides a tag-triggered release pipeline with optional NuGet package signing
   (activates when certificate secrets are configured) and optional NuGet.org publication;
   RELEASING.md documents signed-tag and certificate setup. Actual key/certificate
   provisioning is necessarily the repository owner's step.
3. **Streaming encryption + decompression limits — IMPLEMENTED.**
   `XlsxWriterOptions.Encryption` enables encrypted output from the streaming writer —
   the package is assembled into a temp file and encrypted in 4096-byte segments with an
   incrementally computed HMAC, so the plaintext workbook is never fully resident in
   memory (verified: 10,000-row streamed encrypted export round-trips and passes
   msoffcrypto's independent integrity verification). `Workbook.SaveTo(path, encryption)`
   now spools through a temp file instead of buffering the plaintext in memory.
   `ZipExtractionLimits` (entry count / per-entry / total caps, counted on actual
   decompressed bytes) and `XlsxReaderOptions.MaxPartBytes` close the decompression-bomb
   gap from Finding 5; eight new hardening tests cover bomb rejection, budget exhaustion,
   entry flooding, and normal operation under generous caps.
4. **Trademark clearance & export-control legal review — DEFERRED by owner.** Run formal
   trademark clearance for "Chuvadi" before commercial branding; obtain export-control
   legal review before commercial distribution of the encryption capability.

---

*Prepared by automated source-level audit. Sections 1.4–1.5 are informational analysis,
not legal advice.*
