# Chuvadi.Zip

Pure-BCL zip library for .NET 10. No NuGet dependencies, no temporary files, no native code.

- Read and write from **path, stream or byte array**
- **Streaming** entries in both directions with bounded memory
- **Zip64** (large entries, large archives, more than 65,535 entries), automatic or forced
- **WinZip AES** encryption: AES-128/192/256, AE-1 and AE-2, read and write
- Legacy **ZipCrypto** read (writing it is deliberately refused: it is broken)
- **Zip-slip protection**, symlink policy, link-escape checks on extraction
- **Zip-bomb limits** (entry count, entry/total size, compression ratio, depth, name length, central-directory size) enforced on the bytes actually produced, with documented exceptions
- Lying-header, overlapping-entry and local/central name-mismatch detection
- CRC-32 and AES authentication verified on every read
- `CancellationToken` on every long-running path
- Comments, timestamps (DOS and extended), Unix permissions, DOS attributes, UTF-8 and legacy code-page names
- Directory to zip, zip to directory, deterministic output when you want it

```csharp
using Chuvadi.Zip;

// Write
using (var zip = ZipWriter.Create("out.zip", new ZipWriterOptions { Password = "s3cret" }))   // AES-256
{
    zip.AddText("readme.txt", "hello");
    zip.AddFile("data/report.pdf", @"C:\work\report.pdf");
    zip.AddDirectoryTree(@"C:\work\assets", new ZipDirectoryOptions { EntryPrefix = "assets" });
}

// Read
using var reader = ZipReader.Open("out.zip", new ZipReaderOptions
{
    Password = "s3cret",
    Limits = ZipExtractionLimits.Untrusted,
});
foreach (var e in reader.Entries)
    Console.WriteLine($"{e.Name}  {e.Size} bytes  {e.Encryption}");
string text = reader.ReadAllText(reader.FindEntry("readme.txt")!);
reader.ExtractTo(@"C:\out");

// One-liners
@"C:\work".ZipDirectory("work.zip");
"work.zip".ExtractTo(@"C:\restore");
```

See `docs/Chuvadi.Zip` in the repository for the full guide, the security model and interoperability results.
