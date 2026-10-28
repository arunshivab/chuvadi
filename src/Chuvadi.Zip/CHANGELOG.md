# Chuvadi.Zip - Changelog

## 1.0.0

First release. A complete zip engine written for this library (it does not wrap `ZipArchive`), built only from the BCL (`DeflateStream`, `Aes`, `Rfc2898DeriveBytes`, `HMAC`).

- **Reading**: `ZipReader.Open(path | byte[] | Stream)`, `OpenAsync`; non-seekable input is buffered in memory only (cap `MaxBufferedBytes`), never on disk. Entry list, `FindEntry`, `OpenRead`, `ReadAllBytes`, `ReadAllText`, `CopyTo(Async)`, `Verify`, `ExtractTo(Async)`, `ExtractEntries(Async)`. Archive prefixes (self-extractor stubs) handled.
- **Writing**: `ZipWriter.Create(path | Stream)`, `ToBytes`; `CreateEntry` (stream), `AddBytes`, `AddText`, `AddStream(Async)`, `AddFile(Async)`, `AddDirectory`, `AddDirectoryTree(Async)`. Seekable outputs patch headers; non-seekable outputs use data descriptors.
- **Zip64**: read everywhere; write in `Auto`, `Always` or `Never` mode. A streamed entry that may exceed 4 GiB must declare `ExpectedSize` or `Zip64`, otherwise the writer throws `ZipLimitExceededException` (it never silently writes a corrupt archive).
- **Encryption**: WinZip AES-128/192/256, AE-1 and AE-2, read and write, HMAC verified (read side drains to the authentication code). ZipCrypto read only; writing it throws `ZipUnsupportedException`.
- **Safety**: `ZipExtractionLimits` (+ `Untrusted` preset), zip-slip rejection (`..`, rooted, drive letters, `:`, NUL, backslashes), `ZipUnsafePathPolicy`, `ZipSymlinkPolicy`, `ZipOverwritePolicy`, refusal to write through links, overlapping-entry detection, declared-size enforcement against actual output, partial-file cleanup on failure.
- **Metadata**: comments, DOS time + `0x5455` extended timestamp, Unix mode, DOS attributes, raw external attributes, host system; UTF-8 flag, CP437 fallback (configurable), Info-ZIP Unicode path (`0x7075`).
- **Exceptions**: `ZipFormatException` base with `ZipLimitExceededException`, `ZipUnsafePathException`, `ZipIntegrityException`, `ZipUnsupportedException`; `ZipPasswordRequiredException` (missing or wrong password).
- **Not in 1.0**: BZip2/LZMA/Deflate64/PPMd/strong-encryption entries (reported as unsupported, listable), split archives, forward-only (no-seek) reading, RAR and 7z.
- `Chuvadi.Sheets.Zip` is unchanged; it will be marked obsolete in favour of this package in a later Sheets release.
