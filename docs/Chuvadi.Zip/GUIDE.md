# Chuvadi.Zip guide

Namespace `Chuvadi.Zip`. Everything below is public API.

## Reading

| Task | API |
|---|---|
| Open | `ZipReader.Open(string path / byte[] / ReadOnlyMemory<byte> / Stream, ZipReaderOptions?)`, `OpenAsync(Stream, ...)` |
| List | `reader.Entries`, `Count`, `Comment`, `IsZip64`, `PrefixLength`, `FindEntry(name)` |
| Entry metadata | `ZipEntryInfo`: `Name`, `FileName`, `NormalizedName`, `IsSafePath`, `UnsafeReason`, `Size`, `CompressedSize`, `Crc32`, `CompressionMethod`/`RawMethod`, `Encryption`, `AesVersion`, `IsZip64`, `IsSupported`, `IsDirectory`, `LastModified`, `Comment`, `HostSystem`, `ExternalAttributes`, `UnixMode`, `DosAttributes`, `IsSymlink`, `IsUtf8Name`, `GeneralPurposeFlags` |
| Read content | `OpenRead(entry, password?)` (verifying stream), `ReadAllBytes`, `ReadAllText`, `CopyTo`, `CopyToAsync` |
| Verify everything | `Verify(password?, ct)` - reads every entry, checks CRC-32 / AES MAC / limits |
| Extract | `ExtractTo(dir, ZipExtractOptions?)`, `ExtractToAsync`, `ExtractEntries(...)` |

`OpenRead` returns a stream that decompresses, decrypts, counts and checks as you read. CRC-32 and the AES authentication code are checked when the stream reaches its end: **read to the end** (`ReadAllBytes`, `CopyTo` and `Verify` do) before trusting an entry as intact. Several entry streams may be open on one reader at once.

Passwords: `ZipReaderOptions.Password` is the default; every `OpenRead`/`ReadAll*`/`ExtractOptions` call can override it. A missing or wrong password throws `ZipPasswordRequiredException` (`WrongPassword` tells which). AES uses UTF-8; legacy ZipCrypto uses `LegacyPasswordEncoding` (default UTF-8).

Non-seekable input streams are copied into memory (never to disk) up to `MaxBufferedBytes` (default 256 MiB), then `ZipLimitExceededException(BufferedBytes)`.

## Writing

`ZipWriter.Create(path | Stream, ZipWriterOptions?)`, `ZipWriter.ToBytes(w => ...)`.

| Task | API |
|---|---|
| Bytes / text | `AddBytes`, `AddText` |
| Stream / file | `AddStream(Async)`, `AddFile(Async)` (timestamp and, on Unix, permissions recorded) |
| Streaming entry | `using var s = writer.CreateEntry(name, options);` write, dispose (one open entry at a time) |
| Directory entry | `AddDirectory("name")` |
| Whole tree | `AddDirectoryTree(src, ZipDirectoryOptions?)` or `src.ZipDirectory(zip)` |
| Finish | `Finish()` or `Dispose()` writes the central directory; `Comment` is written then |

`ZipWriterOptions`: `DefaultLevel`, `DefaultMethod`, `Zip64` (Auto/Always/Never), `Password`, `Encryption` (a password alone selects AES-256), `AesVersion` (AE-2 default), `Comment`, `HostSystem`, `LeaveOpen`, `DefaultLastModified` (set it for reproducible archives), `WriteExtendedTimestamps`, `AllowDuplicateNames`, `MaxNameBytes`.
`ZipEntryOptions` overrides per entry: `Method`, `Level`, `Comment`, `LastModified`, `UnixMode`, `DosAttributes`, `ExternalAttributes`, `Encryption`, `Password`, `ExpectedSize`, `Zip64`.

Seekable output: sizes and CRC are patched into the local header. Non-seekable output (pipes, network streams): data descriptors are used. Memory is bounded either way; no temporary files exist.

**Large streamed entries.** The writer must know before the data whether an entry needs Zip64. `AddBytes`, `AddFile` and `AddStream` on a seekable source supply the size automatically. For `CreateEntry` on an unknown-length source that might pass 4 GiB, set `ExpectedSize` or `Zip64 = true`; otherwise the writer throws `ZipLimitExceededException(Zip64Required)` at the 4 GiB mark instead of producing a broken archive.

Writing ZipCrypto is refused (`ZipUnsupportedException`).

## One entry from memory, with a password and limits

The case of a viewer that gets an encrypted archive as bytes (an attachment) and shows one entry:

1. Open from memory: `ZipReader.Open(byte[])`, `Open(ReadOnlyMemory<byte>)` or `Open(Stream)`. Nothing is written to disk.
2. Set the limits on the options: `new ZipReaderOptions { Limits = ZipExtractionLimits.Untrusted }`, or your own `ZipExtractionLimits { MaxEntryBytes = ..., MaxCompressionRatio = ... }`. Limits count the bytes actually produced, not the sizes the header claims; the ratio limit does not apply to entries under 1 MiB.
3. Find the entry (`FindEntry(name)` or `Entries`), then `ReadAllBytes(entry, password)`, or `OpenRead(entry, password)` for a stream. Read to the end before trusting the result.

What to catch:

| Exception | Meaning |
|---|---|
| `ZipPasswordRequiredException` | No password (`WrongPassword` false) or a wrong password (`WrongPassword` true). For a ZipCrypto entry it is also thrown, with `ZipIntegrityException` as `InnerException`, when a wrong password passes the format's one-byte check (about 1 time in 256) and the data then fails its CRC-32 or decompression. AES wrong passwords are detected reliably. |
| `ZipLimitExceededException` | A limit was reached while reading. |
| `ZipUnsupportedException` | The entry uses BZip2, LZMA, Deflate64, PPMd or strong encryption: it lists fine but cannot be read. |
| `ZipIntegrityException` | The data is damaged (CRC-32 or AES authentication failed). |
| `ZipFormatException` | The bytes are not a zip. All of the above derive from it. |

Supported encryption: WinZip AES-128/192/256 (AE-1 and AE-2) and ZipCrypto, both for reading; the writer produces AES only.

## Limits (`ZipExtractionLimits`)

`MaxEntries`, `MaxEntryBytes`, `MaxTotalBytes` (summed over everything one reader produces), `MaxCompressionRatio` (entries under 1 MiB exempt), `MaxPathDepth`, `MaxNameLength`, `MaxCentralDirectoryBytes`. `ZipExtractionLimits.None` (default) and `ZipExtractionLimits.Untrusted` presets. A violation throws `ZipLimitExceededException` with `Limit`, `Maximum` and `EntryName`.

## Exceptions

```
Exception
 |- ZipFormatException            malformed archive
 |   |- ZipLimitExceededException a configured limit tripped (Limit, Maximum, EntryName)
 |   |- ZipUnsafePathException    zip-slip / symlink / link escape (EntryName)
 |   |- ZipIntegrityException     bad CRC, bad AES MAC, truncated, lying header, overlap, name mismatch
 |   `- ZipUnsupportedException   BZip2/LZMA/..., split archives, strong encryption, ZipCrypto write
 `- InvalidOperationException
     `- ZipPasswordRequiredException  missing or wrong password (WrongPassword)
```
Cancellation raises `OperationCanceledException`.

## Cancellation

All async methods and `Verify`, `AddDirectoryTree`, `ZipDirectory*`, `ToZip*`, `ExtractTo*` take a `CancellationToken`, checked between entries and between buffers. Disk and in-memory stream I/O inside a buffer is synchronous, so cancellation latency is one 80 KiB buffer.

## Known limits of 1.0

Deflate and Stored only; no BZip2/LZMA/Deflate64/PPMd; no split archives; strong (PKWARE) encryption unsupported; no forward-only reader; no in-place update of an existing archive; RAR and 7z are out of scope.
