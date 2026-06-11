# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| 1.x (latest release) | ✅ |
| pre-1.0 | ❌ — upgrade to the latest 1.x |

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Preferred channel: **GitHub private vulnerability reporting** — on the repository page, go
to *Security → Report a vulnerability*. This creates a private advisory visible only to
the maintainer.

If that is unavailable, contact the maintainer through the profile listed on the
repository owner's GitHub account and mention "the Chuvadi libraries (Chuvadi.Sheets, Chuvadi.Docs) security" in the subject.

What to include: an affected version, a minimal reproduction (a crafted file is ideal),
and the impact you believe it has. You can expect an acknowledgment within 7 days. Please
allow up to 90 days for a coordinated fix before public disclosure.

## Security model — what this library does and does not promise

**Workbook encryption (`EncryptionOptions`)** is real cryptography: OOXML Agile
Encryption (AES-256-CBC, iterated-SHA-512 key derivation with a 100,000-iteration
default, HMAC-SHA512 integrity verified on both write and read). The strength of the
protection is bounded by the strength of the password.

**Sheet/workbook protection (`Protect(...)`)** is *not* encryption. It is Excel's
cooperative edit-locking; the file content remains readable and the protection is
removable by a determined user. Never rely on it for confidentiality.

**Zip password protection is intentionally absent.** Classic ZipCrypto is broken and
AES-zip is not in the .NET BCL. Use workbook encryption, or encrypt archives with a
dedicated tool.

**Untrusted input:** zip extraction enforces zip-slip protection always; decompression
caps are opt-in via `ZipExtractionLimits` (extraction) and
`XlsxReaderOptions.MaxPartBytes` (xlsx reading). If you process files from untrusted
sources, set these limits.

**Data at rest during writes:** the streaming writer spools sheet XML to temp files
(`chuvadi_sheets_*.tmp`, deletable directory configurable via
`XlsxWriterOptions.TempDirectory`); encrypted saves stage the plaintext package in a temp
file that is deleted after encryption. Plan for this if your threat model includes local
disk access.

## Scope

In scope: memory-safety issues, parser crashes on crafted input that bypass the
documented limits, cryptographic flaws (key derivation, IV reuse, integrity bypass),
path-traversal bypasses, and anything enabling code execution.

Out of scope: brute-forcing weak passwords, the documented non-confidentiality of
sheet protection, resource exhaustion when the caller chose not to configure limits,
and issues only reproducible with a modified copy of the library.
