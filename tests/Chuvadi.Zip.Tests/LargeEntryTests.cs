using System;
using System.IO;
using System.IO.Compression;
using Chuvadi.Zip.ManualTests;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    /// <summary>Writer behaviour around the 4 GiB boundary, streamed through a discarding sink (no disk, bounded memory).</summary>
    public class LargeEntryTests
    {
        private static void Pump(Stream s, long bytes)
        {
            var chunk = new byte[1 << 20];
            for (long done = 0; done < bytes; done += chunk.Length) s.Write(chunk, 0, (int)Math.Min(chunk.Length, bytes - done));
        }

        [Fact]
        public void Declared_zip64_entry_beyond_4GiB_is_written()
        {
            var sink = new DiscardStream();
            const long size = (4L << 30) + (64L << 20);
            using (var w = ZipWriter.Create(sink, new ZipWriterOptions { DefaultMethod = ZipCompressionMethod.Stored }))
            {
                using var s = w.CreateEntry("huge.bin", new ZipEntryOptions { ExpectedSize = size });
                Pump(s, size);
            }
            Assert.True(sink.Total > size && sink.Total < size + 1024);
        }

        [Fact]
        public void Undeclared_entry_beyond_4GiB_fails_with_clear_error()
        {
            var sink = new DiscardStream();
            var ex = Assert.Throws<ZipLimitExceededException>(() =>
            {
                using var w = ZipWriter.Create(sink, new ZipWriterOptions { DefaultMethod = ZipCompressionMethod.Stored });
                using var s = w.CreateEntry("huge.bin");
                Pump(s, 4L << 30);
            });
            Assert.Equal(ZipLimit.Zip64Required, ex.Limit);
        }

        [Fact]
        public void Entry_flagged_zip64_without_size_may_exceed_4GiB()
        {
            var sink = new DiscardStream();
            const long size = (4L << 30) + (1L << 20);
            using (var w = ZipWriter.Create(sink, new ZipWriterOptions { DefaultMethod = ZipCompressionMethod.Stored }))
            {
                using var s = w.CreateEntry("huge.bin", new ZipEntryOptions { Zip64 = true });
                Pump(s, size);
            }
            Assert.True(sink.Total > size);
        }
    }
}
