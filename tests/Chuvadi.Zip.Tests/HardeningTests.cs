using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chuvadi.Zip.ManualTests;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    /// <summary>Hostile / degenerate input: each case must end in a documented exception (or defined behaviour) and never hang.</summary>
    public class HardeningTests
    {
        private static T Bounded<T>(Func<T> f)
        {
            var t = Task.Run(f);
            bool done;
            try { done = t.Wait(TimeSpan.FromSeconds(20)); }
            catch (AggregateException ae) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ae.InnerException!).Throw(); throw; }
            if (!done) throw new Exception("operation did not finish within 20 s (hang)");
            return t.Result;
        }

        private static byte[] Good() => ZipWriter.ToBytes(w => { w.AddBytes("a.bin", new byte[3000]); w.AddText("b.txt", "bee"); });

        private static void MustFailAsZipFormat(byte[] data)
        {
            var ex = Record.Exception(() => Bounded(() => { using var r = ZipReader.Open(data); r.Verify(); return 0; }));
            Assert.NotNull(ex);
            var inner = ex is AggregateException ag ? ag.InnerException! : ex;
            Assert.IsAssignableFrom<ZipFormatException>(inner);
        }

        [Fact]
        public void Zero_byte_archive_is_rejected_with_ZipFormatException()
        {
            MustFailAsZipFormat(Array.Empty<byte>());
            var ms = new MemoryStream();
            var ex = Record.Exception(() => ZipReader.Open(ms));
            Assert.IsType<ZipFormatException>(ex);
            var tmp = Path.GetTempFileName();
            try { Assert.IsType<ZipFormatException>(Record.Exception(() => ZipReader.Open(tmp))); }
            finally { File.Delete(tmp); }
            // non-seekable empty input
            Assert.IsType<ZipFormatException>(Record.Exception(() => ZipReader.Open(new ForwardOnlyStream(new MemoryStream()))));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(21)]
        [InlineData(22)]
        public void Tiny_inputs_are_rejected(int size) => MustFailAsZipFormat(new byte[size]);

        [Fact]
        public void Corrupted_central_directory_variants_fail_cleanly()
        {
            var good = Good();
            int eocd = good.Length - 22;
            int cdOffset = BitConverter.ToInt32(good, eocd + 16);

            byte[] With(Action<byte[]> mutate) { var c = (byte[])good.Clone(); mutate(c); return c; }

            MustFailAsZipFormat(With(c => c[cdOffset] ^= 0xFF));                                         // bad central signature
            MustFailAsZipFormat(With(c => BitConverter.GetBytes((ushort)500).CopyTo(c, eocd + 10)));       // entry count too large
            MustFailAsZipFormat(With(c => BitConverter.GetBytes(uint.MaxValue - 5).CopyTo(c, eocd + 12))); // cd size huge
            MustFailAsZipFormat(With(c => BitConverter.GetBytes(uint.MaxValue - 5).CopyTo(c, eocd + 16))); // cd offset huge
            MustFailAsZipFormat(With(c => BitConverter.GetBytes((ushort)0xFFFF).CopyTo(c, cdOffset + 28))); // name length overruns
            MustFailAsZipFormat(With(c => BitConverter.GetBytes((ushort)0xFFFF).CopyTo(c, cdOffset + 30))); // extra length overruns
            MustFailAsZipFormat(With(c => BitConverter.GetBytes(uint.MaxValue).CopyTo(c, cdOffset + 42)));  // zip64 sentinel offset without extra
            MustFailAsZipFormat(With(c => BitConverter.GetBytes(uint.MaxValue).CopyTo(c, cdOffset + 20)));  // zip64 sentinel size without extra
            MustFailAsZipFormat(With(c => Array.Clear(c, cdOffset, good.Length - 22 - cdOffset)));           // central directory zeroed
            MustFailAsZipFormat(With(c => BitConverter.GetBytes((ushort)1).CopyTo(c, eocd + 4)));           // split archive (disk 1)
        }

        [Fact]
        public void Random_central_directory_damage_never_hangs_or_crashes()
        {
            var good = Good();
            int cdStart = BitConverter.ToInt32(good, good.Length - 22 + 16);
            var rnd = new Random(1234);
            for (int i = 0; i < 2000; i++)
            {
                var c = (byte[])good.Clone();
                for (int k = 0; k < 3; k++) c[rnd.Next(cdStart, c.Length)] = (byte)rnd.Next(256);
                var ex = Record.Exception(() => Bounded(() =>
                {
                    using var r = ZipReader.Open(c);
                    foreach (var e in r.Entries) { try { r.ReadAllBytes(e); } catch (ZipFormatException) { } }
                    return 0;
                }));
                if (ex is not null)
                {
                    var inner = ex is AggregateException ag ? ag.InnerException! : ex;
                    Assert.True(inner is ZipFormatException or ZipPasswordRequiredException, "unexpected " + inner.GetType().Name + ": " + inner.Message);
                }
            }
        }

        [Fact]
        public void Truncated_local_header_fails_with_integrity_exception()
        {
            // Local header offset points at the last 10 bytes of the file: header cannot be read
            var e = RawZip.Stored("a.txt", new byte[] { 1, 2, 3 });
            e.OffsetOverride = 0;
            var zip = RawZip.Build(new[] { e });
            var bad = (byte[])zip.Clone();
            int eocd = bad.Length - 22;
            int cd = BitConverter.ToInt32(bad, eocd + 16);
            BitConverter.GetBytes((uint)(bad.Length - 10)).CopyTo(bad, cd + 42);
            using (var r = ZipReader.Open(bad, new ZipReaderOptions { DetectOverlappingEntries = false }))
                Assert.IsType<ZipIntegrityException>(Record.Exception(() => Bounded(() => r.ReadAllBytes(r.Entries[0]))));

            // Local signature destroyed
            using (var r = ZipReader.Open(RawZip.Build(new[] { RawZip.Stored("a.txt", new byte[] { 1 }) }, omitLocalSig: true)))
                Assert.IsType<ZipIntegrityException>(Record.Exception(() => r.ReadAllBytes(r.Entries[0])));

            // Archive cut inside the first local header while keeping a valid-looking tail
            var good = Good();
            var cut = good.AsSpan(0, 20).ToArray();
            MustFailAsZipFormat(cut);

            // Compressed size claims more data than the file holds
            var big = RawZip.Stored("big.bin", new byte[100]);
            big.DeclaredCSize = 1_000_000;
            using (var r = ZipReader.Open(RawZip.Build(new[] { big }), new ZipReaderOptions { DetectOverlappingEntries = false }))
                Assert.IsType<ZipIntegrityException>(Record.Exception(() => r.OpenRead(r.Entries[0])));
        }

        [Fact]
        public void Hostile_declared_size_does_not_cause_huge_allocation()
        {
            var e = RawZip.Deflated("liar.bin", new byte[100], declaredSize: 4_000_000_000L);
            using var r = ZipReader.Open(RawZip.Build(new[] { e }), new ZipReaderOptions { VerifyCrc = false });
            Assert.IsAssignableFrom<ZipFormatException>(Record.Exception(() => Bounded(() => r.ReadAllBytes(r.Entries[0]))));
        }

        [Fact]
        public void Implausible_but_array_sized_declaration_fails_as_integrity_error()
        {
            var e = RawZip.Deflated("liar.bin", new byte[100], declaredSize: 1_500_000_000L);
            using var r = ZipReader.Open(RawZip.Build(new[] { e }), new ZipReaderOptions { VerifyCrc = false });
            Assert.IsType<ZipIntegrityException>(Record.Exception(() => Bounded(() => r.ReadAllBytes(r.Entries[0]))));
        }

        [Fact]
        public void Duplicate_entry_names_are_defined_behaviour()
        {
            // writer refuses by default
            Assert.Throws<ArgumentException>(() => ZipWriter.ToBytes(w => { w.AddText("x", "1"); w.AddText("x", "2"); }));
            Assert.Throws<ArgumentException>(() => ZipWriter.ToBytes(w => { w.AddText("a/x", "1"); w.AddText("a\\x", "2"); }));   // same after normalisation

            // reader lists both, FindEntry returns the first, extraction: last one wins (Overwrite) or the second fails (Fail)
            var zip = RawZip.Build(new[] { RawZip.Stored("dup.txt", Encoding.ASCII.GetBytes("first")), RawZip.Stored("dup.txt", Encoding.ASCII.GetBytes("second")) });
            using var r = ZipReader.Open(zip);
            Assert.Equal(2, r.Count);
            Assert.Equal("first", r.ReadAllText(r.FindEntry("dup.txt")!));
            var dir = Path.Combine(Path.GetTempPath(), "czip-dup-" + Guid.NewGuid().ToString("N"));
            try
            {
                r.ExtractTo(dir);
                Assert.Equal("second", File.ReadAllText(Path.Combine(dir, "dup.txt")));
                Assert.Throws<IOException>(() => r.ExtractTo(Path.Combine(dir, "again"), new ZipExtractOptions { Overwrite = ZipOverwritePolicy.Fail }));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Theory]
        [InlineData("../evil.txt")]
        [InlineData("..\\evil.txt")]
        [InlineData("a/../../evil.txt")]
        [InlineData("a\\..\\..\\evil.txt")]
        [InlineData("/etc/evil.txt")]
        [InlineData("\\evil.txt")]
        [InlineData("C:\\Windows\\evil.txt")]
        [InlineData("C:evil.txt")]
        [InlineData("c:/evil.txt")]
        [InlineData("\\\\server\\share\\evil.txt")]
        [InlineData("//server/share/evil.txt")]
        [InlineData("..")]
        [InlineData("a/b/../../../evil.txt")]
        [InlineData("file.txt:stream")]
        public void Unsafe_names_are_refused_on_every_platform(string name)
        {
            var dir = Path.Combine(Path.GetTempPath(), "czip-slip-" + Guid.NewGuid().ToString("N"));
            var outDir = Path.Combine(dir, "out");
            try
            {
                var zip = RawZip.Build(new[] { RawZip.Stored(name, new byte[] { 1 }) });
                using var r = ZipReader.Open(zip);
                Assert.False(r.Entries[0].IsSafePath);
                var ex = Record.Exception(() => Bounded(() => r.ExtractTo(outDir)));
                var inner = ex is AggregateException ag ? ag.InnerException! : ex;
                Assert.IsType<ZipUnsafePathException>(inner);
                Assert.Equal(name, ((ZipUnsafePathException)inner).EntryName);

                var skipped = r.ExtractTo(outDir, new ZipExtractOptions { UnsafePaths = ZipUnsafePathPolicy.Skip });
                Assert.Equal(0, skipped.FilesExtracted);
                Assert.Single(skipped.SkippedUnsafe);

                // nothing was created outside (or inside) the target
                Assert.False(File.Exists(Path.Combine(dir, "evil.txt")));
                Assert.Empty(Directory.EnumerateFileSystemEntries(outDir, "*", SearchOption.AllDirectories));

                // the writer refuses to create such names
                Assert.Throws<ArgumentException>(() => ZipWriter.ToBytes(w => w.AddText(name, "x")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Safe_dot_segments_and_mixed_separators_extract_inside_the_root()
        {
            var dir = Path.Combine(Path.GetTempPath(), "czip-safe-" + Guid.NewGuid().ToString("N"));
            try
            {
                var zip = RawZip.Build(new[] { RawZip.Stored("./a\\b//c.txt", Encoding.ASCII.GetBytes("ok")) });
                using var r = ZipReader.Open(zip);
                r.ExtractTo(dir);
                Assert.Equal("ok", File.ReadAllText(Path.Combine(dir, "a", "b", "c.txt")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
