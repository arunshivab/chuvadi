# Changelog

## 1.1.2 (2026-06-26)

### Performance
- `SharedStringTable.GetOrAdd` uses `CollectionsMarshal.GetValueRefOrAddDefault` for a
  single dictionary probe per call instead of two. Hot path on every string-cell write;
  observable speedup at 100K+ row scale.
- `XlsxWriter` placeholder scanner unified between sync and async paths. The async path
  previously allocated two `byte[]` per buffer-fill via `Encoding.ASCII.GetBytes`; now
  uses static UTF-8 byte arrays, zero allocations per call.
- `XlsxReader` lazy-allocates its inline-string `StringBuilder` only when an `<is>`
  element is actually encountered. Most cells are not inline strings, so this removes one
  per-cell allocation in the common case.

### Engineering
- `SheetWriter`'s async XML methods (`EnsureWorksheetOpenAsync`, `BeginRowAsync`,
  `EndRowAsync`, `AppendCellAsync`) collapsed into thin sync-call wrappers. The previous
  explicit-async XmlWriter calls duplicated ~60 lines of sync logic without adding real
  async value (the underlying FileStream is already async-buffered).
- No public API changes. No output-format changes. No crypto changes. All 60 tests pass on
  both Linux and Windows.

## 1.1.1 (2026-06-11)

- Restructured into the `chuvadi` monorepo. Shared internals (OOXML packaging, MS-OFFCRYPTO
  crypto, hardening utilities) extracted to `shared/Chuvadi.Internal` and source-linked, so the
  shipped DLL remains a single zero-dependency assembly. No functional or public API changes.
- Repository URL now points to https://github.com/arunshivab/chuvadi.

## 1.1.0 — 2026-06-11

Implements the audit's outstanding recommendations.

### Features
- **Streaming encryption.** `XlsxWriterOptions.Encryption` lets the streaming
  `XlsxWriter` produce encrypted output: the package is assembled into a temp file and
  encrypted in 4096-byte segments with an incrementally computed HMAC, so the plaintext
  workbook is never fully resident in memory. `Workbook.SaveTo(path, encryption)` now
  spools through a temp file instead of buffering the plaintext package in memory.
- **Hostile-input caps.** `ZipExtractionLimits` (MaxEntries / MaxEntryBytes /
  MaxTotalBytes, counted on actual decompressed bytes — declared sizes are not trusted)
  for zip extraction, and `XlsxReaderOptions.MaxPartBytes` capping the decompressed size
  of any xlsx package part. Both opt-in; exceeding a cap throws with a clear message.

### Engineering
- **xUnit test project** (`tests/Chuvadi.Sheets.Tests`) drives every verification group
  as individual facts plus targeted unit tests (tamper rejection, stream/array
  encryption equivalence, header/footer XML).
- **CI** (`.github/workflows/ci.yml`): build with warnings-as-errors, xUnit, the full
  end-to-end suite, and packing, on Linux and Windows; enforces the pure-BCL rule for
  the shipped library.
- **Release pipeline** (`.github/workflows/release.yml`): tag-triggered build/test/pack
  with optional NuGet package signing and NuGet.org publication; procedure in
  RELEASING.md (signed tags).
- **SECURITY.md** added: private disclosure channel, response expectations, and the
  documented security model.

## 1.0.0 — 2026-06-11

Audit-driven hardening and feature completion.

### Security / correctness
- **Encrypted-file integrity is now enforced on read.** `AgileEncryption.Decrypt`
  verifies the [MS-OFFCRYPTO] §2.3.4.14 HMAC-SHA512 over the encrypted package
  (constant-time comparison) before returning plaintext. Tampered or corrupted files
  now throw a clear integrity error instead of decrypting to garbage.
- **Spin count honored end-to-end.** `EncryptionOptions.SpinCount` is now actually
  applied when encrypting (it was previously silently ignored), and files written by
  other producers with non-default spin counts now decrypt correctly (content-key
  derivation previously hard-coded 100,000 iterations).
- **CFB reader: DIFAT-extension support.** Large encrypted containers whose FAT
  exceeds the 109 header DIFAT slots are now readable.
- **CFB reader: hostile-input hardening.** Directory-entry name lengths are bounds-
  checked against the 64-byte field.
- Removed dead code (`hmacIv`) and the obsolete `[Serializable]` attribute on
  `XlsxPasswordRequiredException`.

### Features
- **Page headers/footers.** `SheetWriter.SetHeaderFooter`, `Sheet.SetHeaderFooter`
  (model API), and `PageHeaderFooter(...)` on both export configs emit the OOXML
  `<headerFooter>` element (Excel header/footer codes supported: `&L/&C/&R`, `&P`,
  `&N`, `&D`, `&T`, `&F`, `&A`).

### Documentation
- README rewritten around capabilities; build-sequence narrative retired to this file.
- Corrected the encryption summary (100,000-iteration default; "PBKDF2-style iterated
  SHA-512" wording), the CFB DIFAT capacity figure, the stale `Workbook` model
  comments, and the `Workbook.Load` docs (now accurately describe value-only
  round-trips). The `CellIs` conditional-format rendering limitation (no dxf records)
  is documented on the public API.
- Internal codename strings removed from doc examples and tests.

### Pre-1.0 history
The library was built in eight incremental steps: (1) OOXML packaging,
(2) shared strings + styles, (3) streaming writer, (4) tables/hyperlinks/comments/
freeze/merge, (5) ergonomic export + workbook model, (6) reader + round-trips,
(7) zip namespace, (8) protection + agile encryption.
