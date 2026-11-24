using System;
using System.IO;
using System.Linq;
using Chuvadi.Zip.Internal;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    /// <summary>
    /// 1.0.2 (security, Z-01): a reader opened with default options enforces the <see cref="ZipExtractionLimits.Untrusted"/>
    /// values. <see cref="ZipExtractionLimits.None"/> is the explicit opt-out for archives you trust.
    /// </summary>
    public class DefaultLimitsTests
    {
        private static readonly Lazy<byte[]> BigEntry = new(() =>
        {
            // One entry of 513 MiB of zeros (1 MiB over the per-entry cap): the archive is well under 1 MiB.
            var ms = new MemoryStream();
            using (var w = ZipWriter.Create(ms, new ZipWriterOptions { LeaveOpen = true }))
            using (var s = w.CreateEntry("bomb.bin"))
            {
                var chunk = new byte[1 << 20];
                for (int i = 0; i < 513; i++) s.Write(chunk, 0, chunk.Length);
            }
            return ms.ToArray();
        });

        private static byte[] ReadEntry(byte[] data, string name, ZipReaderOptions? options = null)
        {
            using var r = ZipReader.Open(data, options);
            return r.ReadAllBytes(r.FindEntry(name)!);
        }

        private static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "chuvadi-zip-dl-" + Guid.NewGuid().ToString("N"));
            return d;
        }

        [Fact]
        public void The_default_limits_are_the_Untrusted_preset()
        {
            var o = new ZipReaderOptions();
            Assert.Same(ZipExtractionLimits.Untrusted, o.Limits);
            Assert.Equal(10_000, o.Limits.MaxEntries);
            Assert.Equal(512L * 1024 * 1024, o.Limits.MaxEntryBytes);
            Assert.Equal(2L * 1024 * 1024 * 1024, o.Limits.MaxTotalBytes);
            Assert.Equal(200d, o.Limits.MaxCompressionRatio);
        }

        [Fact]
        public void A_null_Limits_value_is_refused_and_None_is_the_opt_out()
        {
            Assert.Throws<ArgumentNullException>(() => new ZipReaderOptions { Limits = null! });
            var o = new ZipReaderOptions { Limits = ZipExtractionLimits.None };
            Assert.Null(o.Limits.MaxEntryBytes);
        }

        [Fact]
        public void Z01_an_oversized_entry_is_refused_on_every_default_read_path()
        {
            var data = BigEntry.Value;
            Assert.True(data.Length < 1_048_576, "the bomb archive must stay under 1 MiB (was " + data.Length + ")");

            // in memory, by name and by reader
            var ex1 = Assert.Throws<ZipLimitExceededException>(() => ReadEntry(data, "bomb.bin"));
            Assert.Equal(ZipLimit.EntryBytes, ex1.Limit);
            using (var r = ZipReader.Open(data))
            {
                Assert.Throws<ZipLimitExceededException>(() => r.ReadAllBytes(r.Entries[0]));
                Assert.Throws<ZipLimitExceededException>(() => r.OpenRead(r.Entries[0]));
                Assert.Throws<ZipLimitExceededException>(() => r.CopyTo(r.Entries[0], Stream.Null));
                Assert.Throws<ZipLimitExceededException>(() => r.Verify());
            }
            // listing still works: reading the directory costs nothing
            Assert.Single(data.ListZipEntries());

            // to disk, from bytes and from a file path: nothing is left behind
            var dir = TempDir();
            try
            {
                Assert.Throws<ZipLimitExceededException>(() => data.ExtractTo(dir));
                Assert.True(!Directory.Exists(dir) || !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any());

                var zipPath = Path.Combine(Path.GetTempPath(), "chuvadi-zip-dl-" + Guid.NewGuid().ToString("N") + ".zip");
                File.WriteAllBytes(zipPath, data);
                try
                {
                    Assert.Throws<ZipLimitExceededException>(() => zipPath.ExtractTo(dir));
                    Assert.True(!Directory.Exists(dir) || !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any());
                }
                finally { File.Delete(zipPath); }
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Fact]
        public void A_high_compression_ratio_is_refused_by_default_and_allowed_with_None()
        {
            var data = ZipWriter.ToBytes(w => w.AddBytes("zeros.bin", new byte[2 * 1024 * 1024]));
            Assert.True(data.Length < 10_000);
            var ex = Assert.Throws<ZipLimitExceededException>(() => ReadEntry(data, "zeros.bin"));
            Assert.Equal(ZipLimit.CompressionRatio, ex.Limit);
            var ok = ReadEntry(data, "zeros.bin", new ZipReaderOptions { Limits = ZipExtractionLimits.None });
            Assert.Equal(2 * 1024 * 1024, ok.Length);
        }

        [Fact]
        public void More_than_10000_entries_are_refused_by_default_and_allowed_with_None()
        {
            var data = ZipWriter.ToBytes(w =>
            {
                for (int i = 0; i < 10_001; i++) w.AddBytes("f" + i, ReadOnlySpan<byte>.Empty, new ZipEntryOptions { Method = ZipCompressionMethod.Stored });
            });
            var ex = Assert.Throws<ZipLimitExceededException>(() => ZipReader.Open(data));
            Assert.Equal(ZipLimit.EntryCount, ex.Limit);
            using var r = ZipReader.Open(data, new ZipReaderOptions { Limits = ZipExtractionLimits.None });
            Assert.Equal(10_001, r.Count);
        }

        [Fact]
        public void Deep_and_long_names_are_refused_by_default()
        {
            var deep = ZipWriter.ToBytes(w => w.AddText(string.Join("/", Enumerable.Repeat("d", 33)) + "/f.txt", "x"));
            Assert.Equal(ZipLimit.PathDepth, Assert.Throws<ZipLimitExceededException>(() => ZipReader.Open(deep)).Limit);
            var longName = ZipWriter.ToBytes(w => w.AddText(new string('n', 1025), "x"));
            Assert.Equal(ZipLimit.NameLength, Assert.Throws<ZipLimitExceededException>(() => ZipReader.Open(longName)).Limit);
        }

        [Fact]
        public void An_ordinary_archive_is_unaffected_by_the_default()
        {
            var data = ZipWriter.ToBytes(w => { w.AddText("a/b.txt", "hello"); w.AddBytes("c.bin", new byte[100_000]); });
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(ReadEntry(data, "a/b.txt")));
            Assert.Equal(100_000, ReadEntry(data, "c.bin").Length);
        }

        // ---- overwrite policy (Z-02) ----

        [Fact]
        public void Extraction_does_not_replace_an_existing_file_by_default()
        {
            Assert.Equal(ZipOverwritePolicy.Fail, new ZipExtractOptions().Overwrite);
            var data = ZipWriter.ToBytes(w => { w.AddText("a.txt", "new"); w.AddText("b.txt", "new-b"); });
            var dir = TempDir();
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.txt"), "old");
                var ex = Assert.Throws<IOException>(() => data.ExtractTo(dir));
                Assert.Contains("already exists", ex.Message);
                Assert.Equal("old", File.ReadAllText(Path.Combine(dir, "a.txt")));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Extraction_into_a_new_folder_works_by_default()
        {
            var data = ZipWriter.ToBytes(w => { w.AddText("a.txt", "x"); w.AddText("d/b.txt", "y"); });
            var dir = TempDir();
            try
            {
                var res = data.ExtractTo(dir);
                Assert.Equal(2, res.FilesExtracted);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Theory]
        [InlineData(ZipOverwritePolicy.Overwrite, "new", 1, 0)]
        [InlineData(ZipOverwritePolicy.Skip, "old", 0, 1)]
        public void The_explicit_overwrite_policies_still_work(ZipOverwritePolicy policy, string expected, int extracted, int skipped)
        {
            var data = ZipWriter.ToBytes(w => w.AddText("a.txt", "new"));
            var dir = TempDir();
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.txt"), "old");
                var res = data.ExtractTo(dir, new ZipExtractOptions { Overwrite = policy });
                Assert.Equal(expected, File.ReadAllText(Path.Combine(dir, "a.txt")));
                Assert.Equal(extracted, res.FilesExtracted);
                Assert.Equal(skipped, res.SkippedExisting.Count);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- Windows file-name rules (the logic runs on every platform; extraction applies it on Windows) ----

        [Theory]
        [InlineData("CON", true)]
        [InlineData("dir/nul.txt", true)]
        [InlineData("a/COM1", true)]
        [InlineData("lpt9.log", true)]
        [InlineData("Aux.tar.gz", true)]
        [InlineData("CONIN$", true)]
        [InlineData("con .txt", true)]
        [InlineData("COM¹", true)]
        [InlineData("a./b", true)]
        [InlineData("dir/name ", true)]
        [InlineData("x/...", true)]
        [InlineData("what?.txt", true)]
        [InlineData("a|b", true)]
        [InlineData("star*.txt", true)]
        [InlineData("tab\there", true)]
        [InlineData("console.txt", false)]
        [InlineData("com10.txt", false)]
        [InlineData("dir/readme.md", false)]
        [InlineData("a b/c d.txt", false)]
        [InlineData(".hidden/file", false)]
        public void Windows_rules_flag_device_names_trailing_dots_and_spaces(string name, bool unsafeOnWindows)
        {
            Assert.Equal(unsafeOnWindows, PathSafety.WindowsReason(name) is not null);
        }

        [Fact]
        public void With_the_Windows_rules_on_extraction_throws_or_skips_such_entries()
        {
            var data = ZipWriter.ToBytes(w => { w.AddText("ok.txt", "1"); w.AddText("dir/NUL.txt", "2"); w.AddText("trail./x.txt", "3"); });
            bool old = PathSafety.ApplyWindowsRules;
            PathSafety.ApplyWindowsRules = true;
            var dir = TempDir();
            try
            {
                Assert.Throws<ZipUnsafePathException>(() => data.ExtractTo(dir));
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                var res = data.ExtractTo(dir, new ZipExtractOptions { UnsafePaths = ZipUnsafePathPolicy.Skip });
                Assert.Equal(1, res.FilesExtracted);
                Assert.Equal(2, res.SkippedUnsafe.Count);
                Assert.True(File.Exists(Path.Combine(dir, "ok.txt")));
            }
            finally
            {
                PathSafety.ApplyWindowsRules = old;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
