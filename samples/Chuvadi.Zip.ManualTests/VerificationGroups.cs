using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Zip.Internal;

namespace Chuvadi.Zip.ManualTests
{
    /// <summary>End-to-end verification groups. Each throws on failure; shared by the console runner and the xUnit suite.</summary>
    public static class VerificationGroups
    {
        public static IReadOnlyList<Action> All() => new Action[]
        {
            Crc32_KnownVectors,
            RoundTrip_Bytes_Text_Stream_File,
            RoundTrip_Metadata,
            Empty_Archive_And_Empty_Entry,
            Directory_Entries,
            NonSeekable_Output_Uses_Descriptors,
            NonSeekable_Input_Is_Buffered_And_Capped,
            Zip64_Always_RoundTrip,
            Zip64_Never_Throws,
            Many_Entries_Use_Zip64_Eocd,
            Aes_All_Strengths_And_Versions,
            Aes_WrongPassword_Missing_Tamper,
            Aes_NonSeekable_Output,
            ZipCrypto_Read,
            ZipCrypto_Write_Refused,
            ZipSlip_Rejected,
            ZipSlip_Skip_Policy,
            Limits_Enforced,
            Lying_Header_Bomb_Detected,
            Overlap_Detected,
            Corruption_Detected,
            Garbage_And_Truncated_Input,
            Prefix_Offset_Handled,
            Legacy_Cp437_And_Unicode_Names,
            Extract_Policies,
            Directory_Zip_And_Extract,
            Cancellation_Honoured,
            Streaming_Memory_Is_Bounded,
            Writer_Argument_Validation,
            Unsupported_Method_Reported,
        };

        // ---- helpers ----------------------------------------------------------------

        internal static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static T Throws<T>(Action a) where T : Exception
        {
            try { a(); }
            catch (T ex) { return ex; }
            catch (Exception ex) { throw new Exception($"Expected {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}"); }
            throw new Exception($"Expected {typeof(T).Name} but nothing was thrown.");
        }

        internal static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "chuvadi-zip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        internal static void Cleanup(string dir) { try { Directory.Delete(dir, true); } catch { } }

        private static byte[] Pattern(int n, int seed = 1)
        {
            var b = new byte[n];
            new Random(seed).NextBytes(b);
            return b;
        }

        private static int DataOffset(byte[] zip, ZipEntryInfo e)
        {
            int p = (int)e.HeaderOffset;
            return p + 30 + BitConverter.ToUInt16(zip, p + 26) + BitConverter.ToUInt16(zip, p + 28);
        }

        private static byte[] Compressible(int n) => Enumerable.Range(0, n).Select(i => (byte)("lorem ipsum "[i % 12])).ToArray();

        // ---- groups -----------------------------------------------------------------

        public static void Crc32_KnownVectors()
        {
            Check(Crc32.Update(0, Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926, "CRC32 of '123456789'");
            Check(Crc32.Update(0, Array.Empty<byte>()) == 0, "CRC32 of empty");
            var data = Pattern(100_003);
            uint whole = Crc32.Update(0, data);
            uint split = Crc32.Update(Crc32.Update(0, data.AsSpan(0, 33333)), data.AsSpan(33333));
            Check(whole == split, "CRC32 incremental equals one-shot");
            using var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, CompressionLevel.Fastest, true)) d.Write(data);
            // slicing-by-8 vs bytewise reference
            uint c = 0xFFFFFFFF;
            foreach (var b in data)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            }
            Check(~c == whole, "CRC32 slicing-by-8 matches bitwise reference");
        }

        public static void RoundTrip_Bytes_Text_Stream_File()
        {
            var dir = TempDir();
            try
            {
                var big = Compressible(1_000_000);
                var rnd = Pattern(300_000);
                var filePath = Path.Combine(dir, "src.bin");
                File.WriteAllBytes(filePath, rnd);
                var zipPath = Path.Combine(dir, "a.zip");
                using (var w = ZipWriter.Create(zipPath))
                {
                    w.AddBytes("big.bin", big);
                    w.AddText("hello.txt", "héllo wörld ✓");
                    w.AddStream("stream.bin", new MemoryStream(rnd));
                    w.AddFile("file.bin", filePath);
                    w.AddBytes("stored.bin", rnd, new ZipEntryOptions { Method = ZipCompressionMethod.Stored });
                }
                using var r = ZipReader.Open(zipPath);
                Check(r.Count == 5, "entry count");
                Check(r.ReadAllBytes(r.FindEntry("big.bin")!).AsSpan().SequenceEqual(big), "big.bin content");
                Check(r.ReadAllText(r.FindEntry("hello.txt")!) == "héllo wörld ✓", "text content");
                Check(r.ReadAllBytes(r.FindEntry("stream.bin")!).AsSpan().SequenceEqual(rnd), "stream content");
                Check(r.ReadAllBytes(r.FindEntry("file.bin")!).AsSpan().SequenceEqual(rnd), "file content");
                var st = r.FindEntry("stored.bin")!;
                Check(st.CompressionMethod == ZipCompressionMethod.Stored && st.CompressedSize == rnd.Length, "stored method");
                Check(r.FindEntry("big.bin")!.CompressedSize < big.Length / 10, "deflate actually compresses");
                Check(r.FindEntry("nope") is null, "FindEntry miss");
                r.Verify();
            }
            finally { Cleanup(dir); }

            // bytes API
            var bytes = ZipWriter.ToBytes(w => w.AddText("x.txt", "x"));
            using var r2 = ZipReader.Open(bytes);
            Check(r2.ReadAllText(r2.Entries[0]) == "x", "in-memory round trip");
        }

        public static void RoundTrip_Metadata()
        {
            var when = new DateTimeOffset(2021, 7, 14, 9, 30, 44, TimeSpan.Zero);
            var bytes = ZipWriter.ToBytes(w =>
            {
                w.Comment = "archive comment ünï";
                w.AddText("a.txt", "a", options: new ZipEntryOptions { Comment = "entry comment", LastModified = when, UnixMode = 0x1ED });
                w.AddText("b.txt", "b", options: new ZipEntryOptions { DosAttributes = 0x01 });
            });
            using var r = ZipReader.Open(bytes);
            Check(r.Comment == "archive comment ünï", "archive comment");
            var a = r.FindEntry("a.txt")!;
            Check(a.Comment == "entry comment", "entry comment");
            Check(a.LastModified.ToUnixTimeSeconds() == when.ToUnixTimeSeconds(), "extended timestamp is exact");
            Check(a.HostSystem == ZipHostSystem.Unix, "host system");
            Check(a.UnixMode == (0x1ED | 0x8000), "unix mode with file type: " + a.UnixMode);
            Check(!a.IsSymlink && !a.IsDirectory, "flags");
            Check(r.FindEntry("b.txt")!.DosAttributes == 0x01, "dos attributes");

            var dosBytes = ZipWriter.ToBytes(w => w.AddText("d.txt", "d"), new ZipWriterOptions { HostSystem = ZipHostSystem.Dos, WriteExtendedTimestamps = false, DefaultLastModified = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2001, 2, 3))) });
            using var r2 = ZipReader.Open(dosBytes);
            var d = r2.Entries[0];
            Check(d.HostSystem == ZipHostSystem.Dos && d.UnixMode is null, "dos host has no unix mode");
            Check(d.LastModified.Year == 2001 && d.LastModified.Month == 2 && d.LastModified.Day == 3 && d.LastModified.Second == 6, "dos timestamp to 2s: " + d.LastModified);

            var raw = ZipWriter.ToBytes(w => w.AddText("e.txt", "e", options: new ZipEntryOptions { ExternalAttributes = 0xA1FF0000 }));
            using var r3 = ZipReader.Open(raw);
            Check(r3.Entries[0].IsSymlink, "raw external attributes honoured (symlink)");
        }

        public static void Empty_Archive_And_Empty_Entry()
        {
            var empty = ZipWriter.ToBytes(_ => { });
            Check(empty.Length == 22, "empty archive is just an EOCD (" + empty.Length + ")");
            using (var r = ZipReader.Open(empty)) Check(r.Count == 0, "empty archive has no entries");

            var one = ZipWriter.ToBytes(w => w.AddBytes("zero.bin", ReadOnlySpan<byte>.Empty));
            using var r2 = ZipReader.Open(one);
            Check(r2.ReadAllBytes(r2.Entries[0]).Length == 0, "empty entry");
            r2.Verify();
        }

        public static void Directory_Entries()
        {
            var bytes = ZipWriter.ToBytes(w =>
            {
                w.AddDirectory("docs");
                w.AddText("docs/readme.txt", "r");
            });
            using var r = ZipReader.Open(bytes);
            var d = r.FindEntry("docs/")!;
            Check(d.IsDirectory && d.Size == 0 && d.FileName == "", "directory entry");
            Check(d.UnixMode is int m && (m & 0xF000) == 0x4000, "directory mode");
            Check(r.FindEntry("docs")!.Name == "docs/", "FindEntry normalises directory names");
        }

        public static void NonSeekable_Output_Uses_Descriptors()
        {
            var sink = new ForwardOnlyWriteStream();
            using (var w = ZipWriter.Create(sink))
            {
                w.AddBytes("a.bin", Compressible(100_000));
                w.AddText("b.txt", "bee");
                using var s = w.CreateEntry("c.bin");
                s.Write(Pattern(5000));
            }
            var bytes = sink.Inner.ToArray();
            using var r = ZipReader.Open(bytes);
            Check(r.Count == 3, "count");
            Check((r.Entries[0].Flags & 0x08) != 0, "data descriptor flag set");
            Check(r.ReadAllBytes(r.Entries[0]).AsSpan().SequenceEqual(Compressible(100_000)), "content a");
            Check(r.ReadAllText(r.Entries[1]) == "bee", "content b");
            Check(r.ReadAllBytes(r.Entries[2]).Length == 5000, "content c");
            r.Verify();
        }

        public static void NonSeekable_Input_Is_Buffered_And_Capped()
        {
            var bytes = ZipWriter.ToBytes(w => w.AddBytes("x", Pattern(50_000)));
            using (var r = ZipReader.Open(new ForwardOnlyStream(new MemoryStream(bytes))))
                Check(r.ReadAllBytes(r.Entries[0]).Length == 50_000, "non-seekable input read");
            var ex = Throws<ZipLimitExceededException>(() =>
                ZipReader.Open(new ForwardOnlyStream(new MemoryStream(bytes)), new ZipReaderOptions { MaxBufferedBytes = 1000 }));
            Check(ex.Limit == ZipLimit.BufferedBytes, "buffer cap reported");
            var r2 = ZipReader.OpenAsync(new ForwardOnlyStream(new MemoryStream(bytes))).GetAwaiter().GetResult();
            r2.Dispose();
        }

        public static void Zip64_Always_RoundTrip()
        {
            var data = Compressible(70_000);
            var bytes = ZipWriter.ToBytes(w =>
            {
                w.AddBytes("z.bin", data);
                w.AddText("t.txt", "tt", options: new ZipEntryOptions { Method = ZipCompressionMethod.Stored });
            }, new ZipWriterOptions { Zip64 = Zip64Mode.Always });
            using var r = ZipReader.Open(bytes);
            Check(r.IsZip64, "archive reports Zip64");
            Check(r.Entries.All(e => e.IsZip64), "entries report Zip64");
            Check(r.ReadAllBytes(r.Entries[0]).AsSpan().SequenceEqual(data), "zip64 deflate content");
            Check(r.ReadAllText(r.Entries[1]) == "tt", "zip64 stored content");
            r.Verify();

            // non-seekable + zip64 (descriptors with 8-byte sizes)
            var sink = new ForwardOnlyWriteStream();
            using (var w = ZipWriter.Create(sink, new ZipWriterOptions { Zip64 = Zip64Mode.Always }))
                w.AddBytes("n.bin", data);
            using var r2 = ZipReader.Open(sink.Inner.ToArray());
            Check(r2.ReadAllBytes(r2.Entries[0]).AsSpan().SequenceEqual(data), "zip64 + descriptor content");
        }

        public static void Zip64_Never_Throws()
        {
            Throws<ZipLimitExceededException>(() =>
                ZipWriter.ToBytes(w => w.AddBytes("a", new byte[1], new ZipEntryOptions { Zip64 = true }), new ZipWriterOptions { Zip64 = Zip64Mode.Never }));
            Throws<ZipLimitExceededException>(() =>
                ZipWriter.ToBytes(w => w.AddBytes("a", new byte[1], new ZipEntryOptions { ExpectedSize = 5L * 1024 * 1024 * 1024 }), new ZipWriterOptions { Zip64 = Zip64Mode.Never }));
        }

        public static void Many_Entries_Use_Zip64_Eocd()
        {
            const int n = 70_000;
            var bytes = ZipWriter.ToBytes(w =>
            {
                for (int i = 0; i < n; i++) w.AddBytes("f" + i, ReadOnlySpan<byte>.Empty, new ZipEntryOptions { Method = ZipCompressionMethod.Stored });
            });
            using var r = ZipReader.Open(bytes);
            Check(r.Count == n && r.IsZip64, "70000 entries via Zip64 EOCD (count=" + r.Count + ")");
            Check(r.FindEntry("f69999") is not null, "last entry present");
            Throws<ZipLimitExceededException>(() => ZipWriter.ToBytes(w =>
            {
                for (int i = 0; i < n; i++) w.AddBytes("f" + i, ReadOnlySpan<byte>.Empty, new ZipEntryOptions { Method = ZipCompressionMethod.Stored });
            }, new ZipWriterOptions { Zip64 = Zip64Mode.Never }));
        }

        public static void Aes_All_Strengths_And_Versions()
        {
            var data = Compressible(123_456);
            foreach (var enc in new[] { ZipEncryption.Aes128, ZipEncryption.Aes192, ZipEncryption.Aes256 })
            foreach (var ver in new[] { ZipAesVersion.Ae1, ZipAesVersion.Ae2 })
            foreach (var method in new[] { ZipCompressionMethod.Deflate, ZipCompressionMethod.Stored })
            {
                string label = $"{enc}/{ver}/{method}";
                var bytes = ZipWriter.ToBytes(w =>
                {
                    w.AddBytes("s.bin", data, new ZipEntryOptions { Method = method });
                    w.AddBytes("empty.bin", ReadOnlySpan<byte>.Empty);
                }, new ZipWriterOptions { Password = "pässwörd ✓", Encryption = enc, AesVersion = ver });
                using var r = ZipReader.Open(bytes);
                var e = r.Entries[0];
                Check(e.Encryption == enc && e.AesVersion == ver && e.IsEncrypted, label + " metadata");
                Check(e.RawMethod == (ushort)method, label + " actual method");
                if (ver == ZipAesVersion.Ae2) Check(e.Crc32 == 0, label + " AE-2 stores CRC 0"); else Check(e.Crc32 == Crc32.Update(0, data), label + " AE-1 keeps CRC");
                Check(r.ReadAllBytes(e, "pässwörd ✓").AsSpan().SequenceEqual(data), label + " content");
                Check(r.ReadAllBytes(r.Entries[1], "pässwörd ✓").Length == 0, label + " empty entry");
                r.Verify("pässwörd ✓");
            }

            // password via reader options, per-entry override, mixed archive
            var mixed = ZipWriter.ToBytes(w =>
            {
                w.AddText("plain.txt", "plain");
                w.AddText("secret.txt", "secret", options: new ZipEntryOptions { Password = "pw" });
                w.AddText("other.txt", "other", options: new ZipEntryOptions { Password = "pw2", Encryption = ZipEncryption.Aes128 });
            });
            using var r2 = ZipReader.Open(mixed, new ZipReaderOptions { Password = "pw" });
            Check(!r2.FindEntry("plain.txt")!.IsEncrypted, "plain entry stays unencrypted");
            Check(r2.ReadAllText(r2.FindEntry("plain.txt")!) == "plain", "plain readable without password");
            Check(r2.ReadAllText(r2.FindEntry("secret.txt")!) == "secret", "reader default password");
            Check(r2.ReadAllText(r2.FindEntry("other.txt")!, password: "pw2") == "other", "per-call password");
        }

        public static void Aes_WrongPassword_Missing_Tamper()
        {
            var data = Compressible(10_000);
            var bytes = ZipWriter.ToBytes(w => w.AddBytes("s.bin", data), new ZipWriterOptions { Password = "right" });
            using (var r = ZipReader.Open(bytes))
            {
                var e = r.Entries[0];
                var missing = Throws<ZipPasswordRequiredException>(() => r.OpenRead(e));
                Check(!missing.WrongPassword, "missing password flagged");
                var wrong = Throws<ZipPasswordRequiredException>(() => r.OpenRead(e, "wrong"));
                Check(wrong.WrongPassword, "wrong password flagged");
            }

            // flip one ciphertext byte: local header is 30 + name + extras; flip well inside the data
            for (int variant = 0; variant < 2; variant++)
            {
                var tampered = (byte[])bytes.Clone();
                int pos = variant == 0 ? 120 : tampered.Length / 2;
                using (var probe = ZipReader.Open(bytes))
                {
                    var e = probe.Entries[0];
                    int cipherStart = DataOffset(bytes, e) + 16 + 2;
                    pos = cipherStart + (variant == 0 ? 5 : (int)e.CompressedSize / 2 - 30);
                }
                tampered[pos] ^= 0x40;
                using var r = ZipReader.Open(tampered);
                Throws<ZipFormatException>(() => r.ReadAllBytes(r.Entries[0], "right"));
            }

            // flip a MAC byte: data decrypts fine but authentication fails at the end
            var macBad = (byte[])bytes.Clone();
            using (var probe = ZipReader.Open(bytes))
            {
                var e = probe.Entries[0];
                macBad[DataOffset(bytes, e) + (int)e.CompressedSize - 1] ^= 0x01;
            }
            using var rm = ZipReader.Open(macBad);
            var ex = Throws<ZipIntegrityException>(() => rm.ReadAllBytes(rm.Entries[0], "right"));
            Check(ex.Message.Contains("AES authentication"), "MAC failure message: " + ex.Message);
        }

        public static void Aes_NonSeekable_Output()
        {
            var sink = new ForwardOnlyWriteStream();
            var data = Pattern(40_000, 5);
            using (var w = ZipWriter.Create(sink, new ZipWriterOptions { Password = "pw" }))
                w.AddBytes("a.bin", data);
            using var r = ZipReader.Open(sink.Inner.ToArray());
            Check(r.ReadAllBytes(r.Entries[0], "pw").AsSpan().SequenceEqual(data), "AES over data descriptor");
        }

        public static void ZipCrypto_Read()
        {
            var data = Compressible(5000);
            foreach (bool descriptorStyle in new[] { false, true })
            {
                var e = RawZip.ZipCryptoStored("legacy.txt", data, "secret");
                if (descriptorStyle) { e.Flags |= 0x08; }
                var zip = RawZip.Build(new[] { e });
                using var r = ZipReader.Open(zip);
                var info = r.Entries[0];
                Check(info.Encryption == ZipEncryption.ZipCrypto, "detected ZipCrypto");
                if (!descriptorStyle)
                {
                    Throws<ZipPasswordRequiredException>(() => r.OpenRead(info));
                    Check(Throws<ZipPasswordRequiredException>(() => r.OpenRead(info, "nope")).WrongPassword, "wrong ZipCrypto password");
                }
                Check(r.ReadAllBytes(info, "secret").AsSpan().SequenceEqual(data), "ZipCrypto content (descriptor flag=" + descriptorStyle + ")");
                r.Verify("secret");
            }
        }

        public static void ZipCrypto_Write_Refused()
        {
            Throws<ZipUnsupportedException>(() => ZipWriter.ToBytes(w => w.AddText("a", "a"),
                new ZipWriterOptions { Password = "x", Encryption = ZipEncryption.ZipCrypto }));
        }

        public static void ZipSlip_Rejected()
        {
            var dir = TempDir();
            try
            {
                foreach (var evil in new[] { "../evil.txt", "a/../../evil.txt", "/abs/evil.txt", "C:/win/evil.txt", "..\\evil.txt", "a/b:ads.txt" })
                {
                    var zip = RawZip.Build(new[] { RawZip.Stored(evil, new byte[] { 1, 2, 3 }) });
                    using var r = ZipReader.Open(zip);
                    Check(!r.Entries[0].IsSafePath && r.Entries[0].NormalizedName is null, "flagged unsafe: " + evil);
                    var ex = Throws<ZipUnsafePathException>(() => r.ExtractTo(Path.Combine(dir, "out")));
                    Check(ex.EntryName == evil, "exception names the entry");
                }
                Check(!File.Exists(Path.Combine(dir, "evil.txt")) && !File.Exists("/abs/evil.txt"), "nothing escaped");
            }
            finally { Cleanup(dir); }
        }

        public static void ZipSlip_Skip_Policy()
        {
            var dir = TempDir();
            try
            {
                var zip = RawZip.Build(new[]
                {
                    RawZip.Stored("../evil.txt", new byte[] { 1 }),
                    RawZip.Stored("ok/fine.txt", Encoding.ASCII.GetBytes("fine")),
                });
                using var r = ZipReader.Open(zip);
                var res = r.ExtractTo(Path.Combine(dir, "out"), new ZipExtractOptions { UnsafePaths = ZipUnsafePathPolicy.Skip });
                Check(res.FilesExtracted == 1 && res.SkippedUnsafe.Count == 1, "one extracted, one skipped");
                Check(File.ReadAllText(Path.Combine(dir, "out", "ok", "fine.txt")) == "fine", "safe entry extracted");
                Check(!File.Exists(Path.Combine(dir, "evil.txt")), "unsafe not written");

                // writer refuses to create unsafe names
                Throws<ArgumentException>(() => ZipWriter.ToBytes(w => w.AddText("../x", "x")));
                Throws<ArgumentException>(() => ZipWriter.ToBytes(w => w.AddText("/x", "x")));
                Throws<ArgumentException>(() => ZipWriter.ToBytes(w => w.AddText("C:\\x", "x")));
            }
            finally { Cleanup(dir); }
        }

        public static void Limits_Enforced()
        {
            var many = ZipWriter.ToBytes(w => { for (int i = 0; i < 20; i++) w.AddText("f" + i, "x"); });
            Check(Throws<ZipLimitExceededException>(() => ZipReader.Open(many, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxEntries = 10 } })).Limit == ZipLimit.EntryCount, "entry count");

            var big = ZipWriter.ToBytes(w => w.AddBytes("big", Compressible(3_000_000)));
            using (var r = ZipReader.Open(big, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxEntryBytes = 1000 } }))
                Check(Throws<ZipLimitExceededException>(() => r.OpenRead(r.Entries[0])).Limit == ZipLimit.EntryBytes, "entry bytes");
            using (var r = ZipReader.Open(big, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxCompressionRatio = 10 } }))
                Check(Throws<ZipLimitExceededException>(() => r.OpenRead(r.Entries[0])).Limit == ZipLimit.CompressionRatio, "ratio");

            var two = ZipWriter.ToBytes(w => { w.AddBytes("a", Pattern(600)); w.AddBytes("b", Pattern(600, 2)); });
            using (var r = ZipReader.Open(two, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxTotalBytes = 1000 } }))
            {
                r.ReadAllBytes(r.Entries[0]);
                Check(Throws<ZipLimitExceededException>(() => r.ReadAllBytes(r.Entries[1])).Limit == ZipLimit.TotalBytes, "total bytes");
            }

            var deep = ZipWriter.ToBytes(w => w.AddText("a/b/c/d/e/f.txt", "x"));
            Check(Throws<ZipLimitExceededException>(() => ZipReader.Open(deep, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxPathDepth = 3 } })).Limit == ZipLimit.PathDepth, "depth");
            var longName = ZipWriter.ToBytes(w => w.AddText(new string('n', 300), "x"));
            Check(Throws<ZipLimitExceededException>(() => ZipReader.Open(longName, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxNameLength = 100 } })).Limit == ZipLimit.NameLength, "name length");
            Check(Throws<ZipLimitExceededException>(() => ZipReader.Open(many, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxCentralDirectoryBytes = 100 } })).Limit == ZipLimit.CentralDirectoryBytes, "cd bytes");

            // Untrusted preset opens normal archives fine
            using var ok = ZipReader.Open(two, new ZipReaderOptions { Limits = ZipExtractionLimits.Untrusted });
            ok.Verify();
        }

        public static void Lying_Header_Bomb_Detected()
        {
            // 8 MiB of zeros deflated, but the header claims 100 bytes: actual output must be cut off and rejected
            var zip = RawZip.Build(new[] { RawZip.Deflated("liar.bin", new byte[8 * 1024 * 1024], declaredSize: 100) });
            using var r = ZipReader.Open(zip, new ZipReaderOptions { VerifyCrc = false });
            var ex = Throws<ZipIntegrityException>(() => r.ReadAllBytes(r.Entries[0]));
            Check(ex.Message.Contains("more data than"), "overlong stream message: " + ex.Message);
            Check(r.TotalBytesRead <= 4 * 1024 * 1024, "decompression stopped early (" + r.TotalBytesRead + ")");

            // header claims MORE than actual
            var zip2 = RawZip.Build(new[] { RawZip.Deflated("short.bin", new byte[100], declaredSize: 5000) });
            using var r2 = ZipReader.Open(zip2, new ZipReaderOptions { VerifyCrc = false });
            Throws<ZipIntegrityException>(() => r2.ReadAllBytes(r2.Entries[0]));
        }

        public static void Overlap_Detected()
        {
            var a = RawZip.Stored("a.bin", Pattern(500));
            var b = RawZip.Stored("b.bin", Pattern(500, 2));
            b.OffsetOverride = 0;                          // b's header "starts" inside a's data region
            var zip = RawZip.Build(new[] { a, b });
            Throws<ZipIntegrityException>(() => ZipReader.Open(zip));
            using var allowed = ZipReader.Open(zip, new ZipReaderOptions { DetectOverlappingEntries = false });
            Check(allowed.Count == 2, "overlap check can be disabled");
        }

        public static void Corruption_Detected()
        {
            var data = Pattern(5000);
            var good = ZipWriter.ToBytes(w => w.AddBytes("a.bin", data, new ZipEntryOptions { Method = ZipCompressionMethod.Stored }));
            var bad = (byte[])good.Clone();
            bad[30 + 5 + 100] ^= 0xFF;                      // flip a data byte
            using (var r = ZipReader.Open(bad))
            {
                var ex = Throws<ZipIntegrityException>(() => r.ReadAllBytes(r.Entries[0]));
                Check(ex.Message.Contains("CRC-32"), "CRC mismatch: " + ex.Message);
            }
            using (var r = ZipReader.Open(bad, new ZipReaderOptions { VerifyCrc = false }))
                Check(r.ReadAllBytes(r.Entries[0]).Length == 5000, "CRC verification can be disabled");

            // local/central name mismatch
            var e = RawZip.Stored("good.txt", new byte[] { 1 });
            e.LocalName = Encoding.ASCII.GetBytes("evil.txt");
            using (var r = ZipReader.Open(RawZip.Build(new[] { e })))
                Throws<ZipIntegrityException>(() => r.OpenRead(r.Entries[0]));

            // corrupt deflate stream
            var d = RawZip.Deflated("d.bin", Compressible(10_000));
            for (int i = 0; i < d.Data.Length; i += 3) d.Data[i] ^= 0xA5;
            using (var r = ZipReader.Open(RawZip.Build(new[] { d })))
                Throws<ZipFormatException>(() => r.ReadAllBytes(r.Entries[0]));
        }

        public static void Garbage_And_Truncated_Input()
        {
            Throws<ZipFormatException>(() => ZipReader.Open(new byte[0]));
            Throws<ZipFormatException>(() => ZipReader.Open(Encoding.ASCII.GetBytes("this is not a zip archive at all, really")));
            Throws<ZipFormatException>(() => ZipReader.Open(new byte[100_000]));
            var good = ZipWriter.ToBytes(w => w.AddBytes("a", Pattern(10_000)));
            for (int cut = 1; cut < good.Length; cut += 97)
            {
                try
                {
                    using var r = ZipReader.Open(good.AsSpan(0, cut).ToArray());
                    r.Verify();
                    throw new Exception("truncated archive accepted at " + cut);
                }
                catch (ZipFormatException) { }
            }
            // random corruption never crashes with anything but ZipFormatException / password errors
            var rnd = new Random(42);
            for (int i = 0; i < 300; i++)
            {
                var mutated = (byte[])good.Clone();
                for (int k = 0; k < 4; k++) mutated[rnd.Next(mutated.Length)] = (byte)rnd.Next(256);
                try { using var r = ZipReader.Open(mutated); r.Verify(); }
                catch (ZipFormatException) { }
            }
        }

        public static void Prefix_Offset_Handled()
        {
            var prefix = Enumerable.Repeat((byte)'M', 777).ToArray();
            var zip = RawZip.Build(new[] { RawZip.Stored("a.txt", Encoding.ASCII.GetBytes("prefixed")) }, prefix);
            using var r = ZipReader.Open(zip);
            Check(r.PrefixLength == 777, "prefix length " + r.PrefixLength);
            Check(r.ReadAllText(r.Entries[0]) == "prefixed", "content after prefix");
        }

        public static void Legacy_Cp437_And_Unicode_Names()
        {
            // flag 0, CP437 byte 0x82 = 'é'
            var cp = RawZip.Stored("caf?.txt", new byte[] { 1 });
            cp.Name = new byte[] { (byte)'c', (byte)'a', (byte)'f', 0x82, (byte)'.', (byte)'t', (byte)'x', (byte)'t' };
            using (var r = ZipReader.Open(RawZip.Build(new[] { cp })))
                Check(r.Entries[0].Name == "café.txt", "CP437 decoded: " + r.Entries[0].Name);

            // Info-ZIP Unicode Path extra
            var up = RawZip.Stored("x", new byte[] { 1 });
            up.Name = new byte[] { (byte)'c', (byte)'a', (byte)'f', 0x82 };
            var utf = Encoding.UTF8.GetBytes("café");
            var extra = new List<byte> { 0x75, 0x70 };
            extra.AddRange(BitConverter.GetBytes((ushort)(5 + utf.Length)));
            extra.Add(1);
            extra.AddRange(BitConverter.GetBytes(Crc32.Update(0, up.Name)));
            extra.AddRange(utf);
            up.CdExtra = extra.ToArray();
            using (var r = ZipReader.Open(RawZip.Build(new[] { up })))
                Check(r.Entries[0].Name == "café" && r.Entries[0].IsUtf8Name, "Unicode path extra: " + r.Entries[0].Name);

            // our writer: UTF-8 flag round trip with several scripts
            var names = new[] { "日本語/ファイル.txt", "ñandú/ü.txt", "emoji-😀.txt", "plain.txt" };
            var zip = ZipWriter.ToBytes(w => { foreach (var n in names) w.AddText(n, n); });
            using var r2 = ZipReader.Open(zip);
            for (int i = 0; i < names.Length; i++)
            {
                Check(r2.Entries[i].Name == names[i], "round trip name " + names[i]);
                Check(r2.ReadAllText(r2.Entries[i]) == names[i], "round trip content " + names[i]);
            }
            Check((r2.Entries[3].GeneralPurposeFlags & 0x800) == 0, "ASCII names do not set the UTF-8 flag");
            Check((r2.Entries[0].GeneralPurposeFlags & 0x800) != 0, "non-ASCII names set the UTF-8 flag");
        }

        public static void Extract_Policies()
        {
            var dir = TempDir();
            try
            {
                var zip = ZipWriter.ToBytes(w => { w.AddText("a.txt", "new"); w.AddText("sub/b.txt", "b"); });
                var target = Path.Combine(dir, "t");
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "a.txt"), "old");

                using var r = ZipReader.Open(zip);
                var skip = r.ExtractTo(target, new ZipExtractOptions { Overwrite = ZipOverwritePolicy.Skip });
                Check(File.ReadAllText(Path.Combine(target, "a.txt")) == "old" && skip.SkippedExisting.Count == 1, "Skip keeps existing");
                Throws<IOException>(() => r.ExtractTo(target, new ZipExtractOptions { Overwrite = ZipOverwritePolicy.Fail }));
                var ow = r.ExtractTo(target);
                Check(File.ReadAllText(Path.Combine(target, "a.txt")) == "new" && ow.FilesExtracted == 2, "Overwrite replaces");

                var filtered = r.ExtractTo(Path.Combine(dir, "f"), new ZipExtractOptions { Filter = e => e.Name.StartsWith("sub/") });
                Check(filtered.FilesExtracted == 1 && filtered.SkippedFiltered.Count == 1, "filter");

                // symlink entries are skipped by default, never created
                var link = ZipWriter.ToBytes(w => w.AddText("lnk", "/etc/passwd", options: new ZipEntryOptions { ExternalAttributes = 0xA1FF0000 }));
                using var rl = ZipReader.Open(link);
                var lr = rl.ExtractTo(Path.Combine(dir, "l"));
                Check(lr.SkippedSymlinks.Count == 1 && !File.Exists(Path.Combine(dir, "l", "lnk")), "symlink skipped");
                Throws<ZipUnsafePathException>(() => rl.ExtractTo(Path.Combine(dir, "l2"), new ZipExtractOptions { Symlinks = ZipSymlinkPolicy.Throw }));
                rl.ExtractTo(Path.Combine(dir, "l3"), new ZipExtractOptions { Symlinks = ZipSymlinkPolicy.ExtractAsFile });
                Check(File.ReadAllText(Path.Combine(dir, "l3", "lnk")) == "/etc/passwd", "symlink as file");

                // failed entry leaves no partial file
                var corrupt = ZipWriter.ToBytes(w => w.AddBytes("c.bin", Pattern(5000), new ZipEntryOptions { Method = ZipCompressionMethod.Stored }));
                corrupt[30 + 5 + 10] ^= 0xFF;
                using var rc = ZipReader.Open(corrupt);
                Throws<ZipIntegrityException>(() => rc.ExtractTo(Path.Combine(dir, "c")));
                Check(!File.Exists(Path.Combine(dir, "c", "c.bin")), "partial file removed");

                // writing through a pre-existing link in the target is refused (POSIX only)
                if (!OperatingSystem.IsWindows())
                {
                    var outside = Path.Combine(dir, "outside");
                    Directory.CreateDirectory(outside);
                    var tgt = Path.Combine(dir, "lt");
                    Directory.CreateDirectory(tgt);
                    Directory.CreateSymbolicLink(Path.Combine(tgt, "sub"), outside);
                    using var rr = ZipReader.Open(zip);
                    Throws<ZipUnsafePathException>(() => rr.ExtractTo(tgt));
                    Check(!File.Exists(Path.Combine(outside, "b.txt")), "nothing written through symlinked directory");
                }
            }
            finally { Cleanup(dir); }
        }

        public static void Directory_Zip_And_Extract()
        {
            var dir = TempDir();
            try
            {
                var src = Path.Combine(dir, "src");
                Directory.CreateDirectory(Path.Combine(src, "a", "b"));
                Directory.CreateDirectory(Path.Combine(src, "empty"));
                File.WriteAllText(Path.Combine(src, "root.txt"), "root");
                File.WriteAllText(Path.Combine(src, "a", "mid.txt"), "mid");
                File.WriteAllBytes(Path.Combine(src, "a", "b", "deep.bin"), Pattern(70_000));
                Directory.CreateDirectory(Path.Combine(src, "ünïcode"));
                File.WriteAllText(Path.Combine(src, "ünïcode", "ß.txt"), "unicode");
                var zipPath = Path.Combine(dir, "out.zip");
                var reports = new List<ZipProgress>();
                src.ZipDirectory(zipPath, new ZipDirectoryOptions { Progress = new SyncProgress<ZipProgress>(reports.Add) });
                Check(reports.Count == 4, "progress reports per file (" + reports.Count + ")");

                var entries = zipPath.ListZipEntries().Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                Check(entries.SequenceEqual(new[] { "a/b/deep.bin", "a/mid.txt", "empty/", "root.txt", "ünïcode/ß.txt" }.OrderBy(n => n, StringComparer.Ordinal)),
                    "entry names: " + string.Join(",", entries));

                var outDir = Path.Combine(dir, "restored");
                var res = zipPath.ExtractTo(outDir);
                Check(res.FilesExtracted == 4 && Directory.Exists(Path.Combine(outDir, "empty")), "extracted");
                Check(File.ReadAllText(Path.Combine(outDir, "ünïcode", "ß.txt")) == "unicode", "unicode path restored");
                Check(File.ReadAllBytes(Path.Combine(outDir, "a", "b", "deep.bin")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(src, "a", "b", "deep.bin"))), "binary restored");

                // prefix + filter + deterministic output
                var bytes1 = src.ZipDirectoryToBytes(new ZipDirectoryOptions { EntryPrefix = "pk", Filter = p => !p.EndsWith(".bin"), Writer = new ZipWriterOptions { DefaultLastModified = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) }, PreserveTimestamps = false, PreserveUnixPermissions = false });
                var bytes2 = src.ZipDirectoryToBytes(new ZipDirectoryOptions { EntryPrefix = "pk", Filter = p => !p.EndsWith(".bin"), Writer = new ZipWriterOptions { DefaultLastModified = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) }, PreserveTimestamps = false, PreserveUnixPermissions = false });
                Check(bytes1.AsSpan().SequenceEqual(bytes2), "unencrypted output is deterministic");
                using var rp = ZipReader.Open(bytes1);
                Check(rp.Entries.All(e => e.Name.StartsWith("pk/")) && rp.Entries.All(e => !e.Name.EndsWith(".bin")), "prefix and filter");

                // the output zip inside the source tree must not include itself
                var inside = Path.Combine(src, "self.zip");
                src.ZipDirectory(inside);
                Check(!inside.ListZipEntries().Any(e => e.Name == "self.zip"), "archive does not contain itself");

                // async equivalents
                var asyncZip = Path.Combine(dir, "async.zip");
                src.ZipDirectoryAsync(asyncZip).GetAwaiter().GetResult();
                zipPath.ExtractToAsync(Path.Combine(dir, "restored2")).GetAwaiter().GetResult();
                Check(File.Exists(Path.Combine(dir, "restored2", "root.txt")), "async extract");
            }
            finally { Cleanup(dir); }
        }

        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _a;
            public SyncProgress(Action<T> a) { _a = a; }
            public void Report(T value) => _a(value);
        }

        public static void Cancellation_Honoured()
        {
            var dir = TempDir();
            try
            {
                var zip = ZipWriter.ToBytes(w => { for (int i = 0; i < 10; i++) w.AddBytes("f" + i, Pattern(10_000, i)); });
                using var r = ZipReader.Open(zip);
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                Throws<OperationCanceledException>(() => r.ExtractToAsync(Path.Combine(dir, "x"), null, cts.Token).GetAwaiter().GetResult());
                Throws<OperationCanceledException>(() => r.Verify(null, cts.Token));
                Throws<OperationCanceledException>(() => r.CopyToAsync(r.Entries[0], new MemoryStream(), null, cts.Token).GetAwaiter().GetResult());

                var src = Path.Combine(dir, "s");
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "a.txt"), "a");
                Throws<OperationCanceledException>(() => src.ZipDirectoryAsync(Path.Combine(dir, "o.zip"), null, cts.Token).GetAwaiter().GetResult());
                Throws<OperationCanceledException>(() => ZipWriter.ToBytes(w => w.AddDirectoryTree(src, null, cts.Token)));
            }
            finally { Cleanup(dir); }
        }

        public static void Streaming_Memory_Is_Bounded()
        {
            // 384 MiB through a non-seekable discarding sink, deflate level Fastest and Stored: heap must stay small
            GC.Collect();
            long before = GC.GetTotalMemory(true);
            var sink = new DiscardStream();
            var chunk = Compressible(1 << 20);
            long peak = 0;
            using (var w = ZipWriter.Create(sink, new ZipWriterOptions { DefaultLevel = CompressionLevel.Fastest }))
            {
                using var s = w.CreateEntry("huge.bin", new ZipEntryOptions { ExpectedSize = 384L << 20 });
                for (int i = 0; i < 384; i++)
                {
                    s.Write(chunk);
                    if ((i & 31) == 0) peak = Math.Max(peak, GC.GetTotalMemory(false));
                }
            }
            Check(sink.Total > 0, "bytes were produced");
            Check(peak - before < 96L * 1024 * 1024, "writer memory is bounded (growth " + (peak - before) / 1024 / 1024 + " MiB)");

            // reading back a large stored entry from a seekable file stream in bounded memory
            var dir = TempDir();
            try
            {
                var path = Path.Combine(dir, "big.zip");
                using (var w = ZipWriter.Create(path, new ZipWriterOptions { DefaultMethod = ZipCompressionMethod.Stored }))
                {
                    using var s = w.CreateEntry("big.bin");
                    for (int i = 0; i < 200; i++) s.Write(chunk);
                }
                GC.Collect();
                long b2 = GC.GetTotalMemory(true);
                long p2 = 0, total = 0;
                using (var r = ZipReader.Open(path))
                using (var s = r.OpenRead(r.Entries[0]))
                {
                    var buf = new byte[81920];
                    int n, k = 0;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        if ((++k & 255) == 0) p2 = Math.Max(p2, GC.GetTotalMemory(false));
                    }
                }
                Check(total == 200L << 20, "read all bytes");
                Check(p2 - b2 < 64L * 1024 * 1024, "reader memory is bounded (growth " + (p2 - b2) / 1024 / 1024 + " MiB)");
            }
            finally { Cleanup(dir); }
        }

        public static void Writer_Argument_Validation()
        {
            Throws<ArgumentException>(() => ZipWriter.ToBytes(w => { w.AddText("dup", "1"); w.AddText("dup", "2"); }));
            ZipWriter.ToBytes(w => { w.AddText("dup", "1"); w.AddText("dup", "2"); }, new ZipWriterOptions { AllowDuplicateNames = true });
            Throws<ArgumentException>(() => ZipWriter.ToBytes(w => w.AddText("dir/", "x")));
            Throws<ArgumentException>(() => ZipWriter.ToBytes(w => w.AddText("a", "x", options: new ZipEntryOptions { Encryption = ZipEncryption.Aes256 })));   // no password
            Throws<InvalidOperationException>(() => ZipWriter.ToBytes(w => { w.CreateEntry("a"); w.CreateEntry("b"); }));
            Throws<ArgumentNullException>(() => ZipWriter.Create((Stream)null!));
            Throws<ArgumentException>(() => ZipWriter.Create(new MemoryStream(new byte[0], false)));
            Throws<FileNotFoundException>(() => ZipWriter.ToBytes(w => w.AddFile("a", "/definitely/not/here.bin")));

            // entry streams can't be used after the archive is finished
            var ms = new MemoryStream();
            var writer = ZipWriter.Create(ms);
            writer.AddText("a", "a");
            writer.Finish();
            Throws<InvalidOperationException>(() => writer.AddText("b", "b"));
            writer.Dispose();

            // reading API misuse
            var bytes = ZipWriter.ToBytes(w => w.AddText("a", "a"));
            var r1 = ZipReader.Open(bytes);
            var r2 = ZipReader.Open(bytes);
            Throws<ArgumentException>(() => r1.OpenRead(r2.Entries[0]));
            r1.Dispose();
            Throws<ObjectDisposedException>(() => r1.OpenRead(r1.Entries[0]));
            r2.Dispose();
        }

        public static void Unsupported_Method_Reported()
        {
            var e = RawZip.Stored("bz.bin", new byte[] { 1, 2, 3 });
            e.Method = 12;                                   // BZip2
            using var r = ZipReader.Open(RawZip.Build(new[] { e }));
            Check(!r.Entries[0].IsSupported, "reported as unsupported");
            Throws<ZipUnsupportedException>(() => r.OpenRead(r.Entries[0]));
            var e2 = RawZip.Stored("strong.bin", new byte[] { 1 });
            e2.Flags = 0x41;                                 // encrypted + strong encryption
            using var r2 = ZipReader.Open(RawZip.Build(new[] { e2 }));
            Throws<ZipUnsupportedException>(() => r2.OpenRead(r2.Entries[0], "x"));
        }
    }
}
