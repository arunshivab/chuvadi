using System;
using System.IO;
using System.Text;
using Chuvadi.Zip.Internal;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    public class PathSafetyTests
    {
        [Theory]
        [InlineData("a/b.txt", "a/b.txt", false)]
        [InlineData("a\\b.txt", "a/b.txt", false)]
        [InlineData("./a//b.txt", "a/b.txt", false)]
        [InlineData("dir/", "dir/", true)]
        [InlineData("./", "", true)]
        public void Normalizes(string input, string expected, bool dir)
        {
            Assert.True(PathSafety.TryNormalize(input, out var n, out var isDir, out _));
            Assert.Equal(expected, n);
            Assert.Equal(dir, isDir);
        }

        [Theory]
        [InlineData("../x")]
        [InlineData("a/../../x")]
        [InlineData("/x")]
        [InlineData("\\x")]
        [InlineData("C:/x")]
        [InlineData("a/b:stream")]
        [InlineData("a\0b")]
        [InlineData("")]
        public void Rejects(string input)
        {
            Assert.False(PathSafety.TryNormalize(input, out _, out _, out var err));
            Assert.NotNull(err);
        }

        [Fact]
        public void ResolveUnderRoot_blocks_escape()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rootx"));
            Assert.NotNull(PathSafety.ResolveUnderRoot(root, "a/b.txt"));
            Assert.Null(PathSafety.ResolveUnderRoot(root, "../rootx2/b.txt"));
        }

        [Fact]
        public void Depth_counts_segments()
        {
            Assert.Equal(3, PathSafety.Depth("a/b/c.txt"));
            Assert.Equal(2, PathSafety.Depth("a/b/"));
        }
    }

    public class FormatHelperTests
    {
        [Fact]
        public void Dos_time_round_trips_to_two_seconds()
        {
            var t = new DateTime(2022, 11, 30, 23, 59, 58, DateTimeKind.Local);
            var (d, tm) = ZipFormat.LocalToDos(t);
            Assert.Equal(t, ZipFormat.DosToLocal(d, tm));
        }

        [Fact]
        public void Dos_time_clamps_out_of_range_years()
        {
            var (d, _) = ZipFormat.LocalToDos(new DateTime(1975, 1, 1, 0, 0, 0, DateTimeKind.Local));
            Assert.Equal(1980, ZipFormat.DosToLocal(d, 0).Year);
        }

        [Fact]
        public void Dos_time_decoder_tolerates_garbage_fields()
        {
            var dt = ZipFormat.DosToLocal(0xFFFF, 0xFFFF);   // month 15, day 31, 31:63:62 - must not throw
            Assert.True(dt.Year > 2000);
        }

        [Fact]
        public void TryGetExtra_survives_truncated_data()
        {
            var extra = new byte[] { 0x01, 0x00, 0x10, 0x00, 1, 2, 3 };    // claims 16 bytes, has 3
            Assert.False(ZipFormat.TryGetExtra(extra, 1, out _));
        }
    }

    public class AesCtrTests
    {
        [Fact]
        public void Keystream_is_continuous_across_chunking()
        {
            var key = new byte[32];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)i;
            var data = new byte[10_000];
            new Random(3).NextBytes(data);
            var a = (byte[])data.Clone();
            var b = (byte[])data.Clone();
            using (var c = new AesCtr(key)) c.Transform(a);
            using (var c = new AesCtr(key))
            {
                int pos = 0;
                foreach (int n in new[] { 1, 15, 16, 17, 4095, 4096, 900, 775 })
                {
                    c.Transform(b.AsSpan(pos, n));
                    pos += n;
                }
                c.Transform(b.AsSpan(pos));
            }
            Assert.Equal(a, b);
        }

        [Fact]
        public void Matches_aes_ecb_of_little_endian_counter()
        {
            var key = new byte[16];
            var zeros = new byte[48];
            using (var c = new AesCtr(key)) c.Transform(zeros);
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = key;
            for (ulong blk = 1; blk <= 3; blk++)
            {
                var ctr = new byte[16];
                BitConverter.TryWriteBytes(ctr, blk);
                var expect = aes.EncryptEcb(ctr, System.Security.Cryptography.PaddingMode.None);
                Assert.Equal(expect, zeros.AsSpan((int)(blk - 1) * 16, 16).ToArray());
            }
        }
    }

    public class ApiBehaviourTests
    {
        [Fact]
        public void Zip64_threshold_selects_zip64_for_declared_large_entries()
        {
            var bytes = ZipWriter.ToBytes(w => w.AddBytes("a", new byte[10], new ZipEntryOptions { Zip64 = true }));
            using var r = ZipReader.Open(bytes);
            Assert.True(r.Entries[0].IsZip64);
            var small = ZipWriter.ToBytes(w => w.AddBytes("a", new byte[10]));
            using var r2 = ZipReader.Open(small);
            Assert.False(r2.Entries[0].IsZip64);
            Assert.False(r2.IsZip64);
        }

        [Fact]
        public void Reader_can_leave_stream_open_or_close_it()
        {
            var bytes = ZipWriter.ToBytes(w => w.AddText("a", "a"));
            var ms = new MemoryStream(bytes);
            ZipReader.Open(ms, new ZipReaderOptions { LeaveOpen = true }).Dispose();
            Assert.True(ms.CanRead);
            ZipReader.Open(ms, new ZipReaderOptions { LeaveOpen = false }).Dispose();
            Assert.False(ms.CanRead);
        }

        [Fact]
        public void Writer_can_leave_stream_open_or_close_it()
        {
            var ms = new MemoryStream();
            ZipWriter.Create(ms).Dispose();
            Assert.True(ms.CanWrite);
            var ms2 = new MemoryStream();
            ZipWriter.Create(ms2, new ZipWriterOptions { LeaveOpen = false }).Dispose();
            Assert.False(ms2.CanWrite);
        }

        [Fact]
        public void Multiple_entry_streams_can_be_open_at_once()
        {
            var a = new byte[50_000]; new Random(1).NextBytes(a);
            var b = new byte[60_000]; new Random(2).NextBytes(b);
            var zip = ZipWriter.ToBytes(w => { w.AddBytes("a", a); w.AddBytes("b", b); });
            using var r = ZipReader.Open(zip);
            using var sa = r.Entries[0].OpenRead();
            using var sb = r.Entries[1].OpenRead();
            var ba = new byte[a.Length]; var bb = new byte[b.Length];
            int pa = 0, pb = 0;
            while (pa < ba.Length || pb < bb.Length)
            {
                if (pa < ba.Length) pa += sa.Read(ba, pa, Math.Min(777, ba.Length - pa));
                if (pb < bb.Length) pb += sb.Read(bb, pb, Math.Min(1234, bb.Length - pb));
            }
            Assert.Equal(a, ba);
            Assert.Equal(b, bb);
        }

        [Fact]
        public void Entry_stream_reports_length_and_position()
        {
            var zip = ZipWriter.ToBytes(w => w.AddText("a", "hello"));
            using var r = ZipReader.Open(zip);
            using var s = r.Entries[0].OpenRead();
            Assert.Equal(5, s.Length);
            s.ReadByte();
            Assert.Equal(1, s.Position);
        }

        [Fact]
        public void Exception_hierarchy()
        {
            Assert.True(typeof(ZipLimitExceededException).IsSubclassOf(typeof(ZipFormatException)));
            Assert.True(typeof(ZipUnsafePathException).IsSubclassOf(typeof(ZipFormatException)));
            Assert.True(typeof(ZipIntegrityException).IsSubclassOf(typeof(ZipFormatException)));
            Assert.True(typeof(ZipUnsupportedException).IsSubclassOf(typeof(ZipFormatException)));
            Assert.False(typeof(ZipPasswordRequiredException).IsSubclassOf(typeof(ZipFormatException)));
        }

        [Fact]
        public void Reproducible_output_with_fixed_timestamp()
        {
            var opts = new ZipWriterOptions { DefaultLastModified = new DateTimeOffset(2020, 5, 5, 5, 5, 4, TimeSpan.Zero) };
            byte[] Make() => ZipWriter.ToBytes(w => { w.AddText("a", "alpha"); w.AddText("b/c", "gamma"); }, opts);
            Assert.Equal(Make(), Make());
        }

        [Fact]
        public void Aes_output_is_randomised()
        {
            var opts = new ZipWriterOptions { Password = "pw" };
            Assert.NotEqual(ZipWriter.ToBytes(w => w.AddText("a", "alpha"), opts), ZipWriter.ToBytes(w => w.AddText("a", "alpha"), opts));
        }
    }
}
