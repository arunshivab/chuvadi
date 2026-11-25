# Chuvadi.Zip security model

| Threat | Defence |
|---|---|
| Zip-slip (`../`, absolute, drive letters, `:` streams, NUL, `\` separators) | Names normalised and validated when read; resolved path must stay under the root; `ZipUnsafePathException` (or skip) |
| Windows device names, trailing dots and spaces, invalid characters | Refused on Windows as unsafe paths (`ZipUnsafePathException`, or skipped) |
| Silent replacement of existing files | `ZipExtractOptions.Overwrite` defaults to `Fail` (since 1.0.2); `Overwrite` and `Skip` are explicit choices |
| Write through a pre-existing symlink / junction | Parent directories checked for link targets; existing link files are deleted rather than written through |
| Symlink entries | Skipped by default; `Throw` or `ExtractAsFile` selectable; never created as links |
| Decompression bomb | Limits are on by default (the `Untrusted` values, since 1.0.2) and apply to actual decompressed bytes; compression-ratio cap; declared size must match actual output. Opt out with `ZipExtractionLimits.None` for trusted archives. Ceiling: 2 GiB in total; entries under 1 MiB are exempt from the ratio check (cumulative ratio limit planned for 1.1.0) |
| Lying headers | Output stops at declared size + 1 byte and raises `ZipIntegrityException` |
| Overlapping entries (zip quine) | Entries sorted by offset and checked against each other and the central directory |
| Local vs central name mismatch | Compared on every open |
| Huge central directory / entry count | `MaxCentralDirectoryBytes`, `MaxEntries`, count-vs-size sanity check before allocation |
| Tampered AES data | HMAC-SHA1 verified (constant-time) at end of entry; wrong password detected by the 2-byte verifier |
| Memory exhaustion from non-seekable input | `MaxBufferedBytes` |
| Partial files after failure | Deleted (`DeletePartialFiles`) |

Notes: ZipCrypto is broken and read-only. The 2-byte AES password verifier and the ZipCrypto check byte can only reject ~99.99% / ~99.6% of wrong passwords outright; the AES MAC always catches the rest. WinZip AES uses PBKDF2-HMAC-SHA1 with 1000 iterations by specification: use long random passwords. AE-2 (the default) stores CRC 0 so plaintext is not fingerprintable.
