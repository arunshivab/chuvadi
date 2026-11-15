using System;
using System.IO;
using System.Text;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    /// <summary>
    /// The "open one entry of an encrypted archive held in memory" case (a viewer opening a password-protected
    /// attachment): byte[] input, one entry read with a password, and size and ratio limits on the bytes produced.
    /// ZipCrypto checks use the FIXED archive of <see cref="ZipCryptoPasswordTests"/>, so no result depends on chance.
    /// AES wrong-password checks try several candidates: a wrong password passes the 2-byte AES verifier about 1 time
    /// in 65,536 and then fails the authentication code with <see cref="ZipIntegrityException"/> instead.
    /// </summary>
    public class InMemoryPasswordEntryTests
    {
        private const string Good = "pw-correct";
        private static readonly byte[] Small = Encoding.UTF8.GetBytes("patient report");

        private static byte[] MakeAes(ZipEncryption mode, byte[]? big = null)
        {
            var ms = new MemoryStream();
            using (var w = ZipWriter.Create(ms, new ZipWriterOptions { Password = Good, Encryption = mode, LeaveOpen = true }))
            {
                w.AddBytes("dir/report.txt", Small);
                if (big != null) w.AddBytes("zeros.bin", big);
            }
            return ms.ToArray();
        }

        [Theory]
        [InlineData(ZipEncryption.Aes128)]
        [InlineData(ZipEncryption.Aes192)]
        [InlineData(ZipEncryption.Aes256)]
        public void An_AES_entry_reads_from_a_byte_array_with_the_password(ZipEncryption mode)
        {
            var data = MakeAes(mode);
            using var r = ZipReader.Open(data);
            var e = r.FindEntry("dir/report.txt")!;
            Assert.Equal(mode, e.Encryption);
            Assert.Equal(Small, r.ReadAllBytes(e, Good));
            Assert.Equal("patient report", r.ReadAllText(e, null, Good));
        }

        [Fact]
        public void The_same_works_from_ReadOnlyMemory_and_from_a_stream_and_with_the_options_password()
        {
            var data = MakeAes(ZipEncryption.Aes256);
            using (var r = ZipReader.Open(new ReadOnlyMemory<byte>(data)))
                Assert.Equal(Small, r.ReadAllBytes(r.FindEntry("dir/report.txt")!, Good));
            using (var r = ZipReader.Open(new MemoryStream(data)))
                Assert.Equal(Small, r.ReadAllBytes(r.FindEntry("dir/report.txt")!, Good));
            using (var r = ZipReader.Open(data, new ZipReaderOptions { Password = Good }))
                Assert.Equal(Small, r.ReadAllBytes(r.FindEntry("dir/report.txt")!));
        }

        [Fact]
        public void A_missing_AES_password_throws_ZipPasswordRequiredException_and_says_missing()
        {
            using var r = ZipReader.Open(MakeAes(ZipEncryption.Aes256));
            var ex = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.FindEntry("dir/report.txt")!));
            Assert.False(ex.WrongPassword);
            Assert.Equal("dir/report.txt", ex.EntryName);
        }

        [Fact]
        public void A_wrong_AES_password_throws_ZipPasswordRequiredException()
        {
            using var r = ZipReader.Open(MakeAes(ZipEncryption.Aes256));
            var e = r.FindEntry("dir/report.txt")!;
            int rejected = 0;
            for (int i = 0; i < 8; i++)
            {
                try { r.ReadAllBytes(e, "wrong-" + i); Assert.Fail("a wrong password read the entry"); }
                catch (ZipPasswordRequiredException ex) { Assert.True(ex.WrongPassword); rejected++; }
                catch (ZipIntegrityException) { /* passed the 2-byte verifier by chance; the MAC still refused it */ }
            }
            Assert.True(rejected >= 1);
        }

        [Fact]
        public void A_missing_ZipCrypto_password_throws_ZipPasswordRequiredException_and_a_wrong_one_says_wrong()
        {
            using var r = ZipReader.Open(ZipCryptoPasswordTests.Archive);
            var e = r.FindEntry("deflated.txt")!;
            var missing = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(e));
            Assert.False(missing.WrongPassword);
            var wrong = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(e, ZipCryptoPasswordTests.FailsCheckByte));
            Assert.True(wrong.WrongPassword);
            Assert.NotEmpty(r.ReadAllBytes(e, ZipCryptoPasswordTests.GoodPassword));
        }

        [Fact]
        public void The_entry_size_limit_applies_to_an_entry_read_from_memory()
        {
            var data = MakeAes(ZipEncryption.Aes256, new byte[4 * 1024 * 1024]);
            using var r = ZipReader.Open(data, new ZipReaderOptions { Password = Good, Limits = new ZipExtractionLimits { MaxEntryBytes = 1_000_000 } });
            Assert.Throws<ZipLimitExceededException>(() => r.ReadAllBytes(r.FindEntry("zeros.bin")!));
            Assert.Equal(Small, r.ReadAllBytes(r.FindEntry("dir/report.txt")!));
        }

        [Fact]
        public void The_compression_ratio_limit_applies_to_an_entry_read_from_memory()
        {
            // 4 MiB of zeros deflates to a few KiB, far above a ratio of 100; entries under 1 MiB are exempt from the ratio.
            var data = MakeAes(ZipEncryption.Aes256, new byte[4 * 1024 * 1024]);
            using var r = ZipReader.Open(data, new ZipReaderOptions { Password = Good, Limits = new ZipExtractionLimits { MaxCompressionRatio = 100 } });
            Assert.Throws<ZipLimitExceededException>(() => r.ReadAllBytes(r.FindEntry("zeros.bin")!));
            Assert.Equal(Small, r.ReadAllBytes(r.FindEntry("dir/report.txt")!));
        }

        [Fact]
        public void The_Untrusted_preset_stops_the_bomb_entry_and_still_reads_a_small_one()
        {
            var data = MakeAes(ZipEncryption.Aes256, new byte[64 * 1024 * 1024]);
            using var r = ZipReader.Open(data, new ZipReaderOptions { Password = Good, Limits = ZipExtractionLimits.Untrusted });
            Assert.Throws<ZipLimitExceededException>(() => r.ReadAllBytes(r.FindEntry("zeros.bin")!));
            Assert.Equal(Small, r.ReadAllBytes(r.FindEntry("dir/report.txt")!));
        }

        [Fact]
        public void The_entry_size_limit_also_applies_to_a_ZipCrypto_entry()
        {
            // The right password is given, so the entry would otherwise read; only the limit stops it.
            var archive = ZipCryptoPasswordTests.Archive;
            using var r = ZipReader.Open(archive, new ZipReaderOptions { Limits = new ZipExtractionLimits { MaxEntryBytes = 100 } });
            Assert.Throws<ZipLimitExceededException>(() => r.ReadAllBytes(r.FindEntry("deflated.txt")!, ZipCryptoPasswordTests.GoodPassword));
        }

        [Fact]
        public void Bytes_that_are_not_a_zip_throw_ZipFormatException()
        {
            Assert.IsAssignableFrom<ZipFormatException>(Record.Exception(() => ZipReader.Open(new byte[] { 1, 2, 3, 4, 5 })));
        }
    }
}
