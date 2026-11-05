using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    /// <summary>
    /// ZipCrypto password checking, on FIXED archives and FIXED passwords so the result never depends on chance.
    /// ZipCrypto verifies a password with a single byte, so a wrong password passes that check about 1 time in 256
    /// (about 1 in 128 for an entry written with a data descriptor, which the reader checks against two bytes).
    /// A test that opens a freshly made ZipCrypto archive (Info-ZIP's zip chooses random bytes for it) with an
    /// arbitrary wrong password therefore fails now and then. These tests never do that.
    ///
    /// The archive below has two ZipCrypto entries, written once with a fixed 11-byte header and the password
    /// "hunter2": "stored.txt" (method 0) and "deflated.txt" (method 8). Info-ZIP unzip and Python's zipfile both
    /// read it with "hunter2". With the fixed header bytes:
    ///   "wrong-password-0"      FAILS the check byte of both entries (a plain "wrong password").
    ///   "wrong-password-200051" PASSES the check byte of both entries, then yields garbage.
    /// Both passwords were found by searching "wrong-password-N" for N = 0, 1, 2, ... against the archive.
    /// </summary>
    public class ZipCryptoPasswordTests
    {
        private const string GoodPassword = "hunter2";
        private const string FailsCheckByte = "wrong-password-0";
        private const string PassesCheckByte = "wrong-password-200051";

        private static readonly byte[] Archive = Convert.FromBase64String(
            "UEsDBBQAAQAAAAAAIQDRzKTyggAAAHYAAAAKAAAAc3RvcmVkLnR4dPwlIlUplJYdiAfLerXWso659uWYBJLfQsFaC6s9uw/T" +
            "iUNTz5P6y7lgvwwd/RFUT4m8xrK0zgof7P/QgSk8m+pooh+R8tRVkeBtUwFpEH0DNWEaYIQz0k9kjBBge4Gx2Ghz8NN9k0Gp" +
            "WyDUYX35LB7kvkX25rP8W81bf8Zzgxq+/7tQSwMEFAABAAgAAAAhAKK4FAxWAAAA6AgAAAwAAABkZWZsYXRlZC50eHQsjY+O" +
            "N8VTaoaeORoD8IO47Ir0bUKYZOPw2Lew+G/rNogNEKfS0aeRwhzfBRaqZUCPuRisBjao1+ap73KLtITuEHkw7KsmHcMjgmyK" +
            "Kpa0cWXsdo5bzVBLAQIUABQAAQAAAAAAIQDRzKTyggAAAHYAAAAKAAAAAAAAAAAAAAAAAAAAAABzdG9yZWQudHh0UEsBAhQA" +
            "FAABAAgAAAAhAKK4FAxWAAAA6AgAAAwAAAAAAAAAAAAAAAAAqgAAAGRlZmxhdGVkLnR4dFBLBQYAAAAAAgACAHIAAAAqAQAA" +
            "AAA=");

        private static string StoredText => string.Concat(Enumerable.Repeat("Stored entry: the quick brown fox jumps over the lazy dog. ", 2));
        private static string DeflatedText => string.Concat(Enumerable.Repeat("Deflated entry: pack my box with five dozen liquor jugs. ", 40));

        public static TheoryData<string> EntryNames => new TheoryData<string> { "stored.txt", "deflated.txt" };

        [Fact]
        public void The_fixed_archive_reads_with_the_right_password()
        {
            using var r = ZipReader.Open(Archive);
            Assert.All(r.Entries, e => Assert.Equal(ZipEncryption.ZipCrypto, e.Encryption));
            Assert.Equal(StoredText, r.ReadAllText(r.FindEntry("stored.txt")!, null, GoodPassword));
            Assert.Equal(DeflatedText, r.ReadAllText(r.FindEntry("deflated.txt")!, null, GoodPassword));
            r.Verify(GoodPassword);
        }

        [Theory]
        [MemberData(nameof(EntryNames))]
        public void A_password_that_fails_the_check_byte_is_reported_as_wrong(string name)
        {
            using var r = ZipReader.Open(Archive);
            var ex = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.FindEntry(name)!, FailsCheckByte));
            Assert.True(ex.WrongPassword);
            Assert.Equal(name, ex.EntryName);
            Assert.Equal($"Wrong password for entry '{name}'.", ex.Message);
            Assert.Null(ex.InnerException);
        }

        [Fact]
        public void A_stored_entry_whose_wrong_password_passes_the_check_byte_is_reported_as_wrong_or_damaged()
        {
            using var r = ZipReader.Open(Archive);
            var ex = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.FindEntry("stored.txt")!, PassesCheckByte));
            Assert.True(ex.WrongPassword);
            Assert.Equal("stored.txt", ex.EntryName);
            Assert.Equal("Wrong password for entry 'stored.txt', or the entry is damaged: the ZipCrypto password check passed, but the data then failed its integrity check.", ex.Message);
            var inner = Assert.IsType<ZipIntegrityException>(ex.InnerException);
            Assert.StartsWith("CRC-32 mismatch for entry 'stored.txt' (stored 0xF2A4CCD1, computed 0x", inner.Message);
        }

        [Fact]
        public void A_deflated_entry_whose_wrong_password_passes_the_check_byte_is_reported_as_wrong_or_damaged()
        {
            using var r = ZipReader.Open(Archive);
            var ex = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.FindEntry("deflated.txt")!, PassesCheckByte));
            Assert.True(ex.WrongPassword);
            Assert.Equal("deflated.txt", ex.EntryName);
            Assert.Equal("Wrong password for entry 'deflated.txt', or the entry is damaged: the ZipCrypto password check passed, but the data then failed its integrity check.", ex.Message);
            // The wording of the decompressor's own message depends on the .NET runtime, so only its type is pinned.
            Assert.IsType<ZipIntegrityException>(ex.InnerException);
        }

        [Fact]
        public void The_same_report_comes_from_the_stream_verify_and_extract()
        {
            using var r = ZipReader.Open(Archive);
            var e = r.FindEntry("stored.txt")!;
            using (var s = e.OpenRead(PassesCheckByte))
            {
                var buf = new byte[4096];
                int n;
                var ex = Assert.Throws<ZipPasswordRequiredException>(() => { while ((n = s.Read(buf, 0, buf.Length)) > 0) { } });
                Assert.IsType<ZipIntegrityException>(ex.InnerException);
            }
            Assert.Throws<ZipPasswordRequiredException>(() => r.Verify(PassesCheckByte));
            Assert.Throws<ZipPasswordRequiredException>(() => r.CopyTo(e, Stream.Null, PassesCheckByte));
            var asyncEx = Record.Exception(() => r.CopyToAsync(e, Stream.Null, PassesCheckByte).GetAwaiter().GetResult());
            Assert.IsType<ZipPasswordRequiredException>(asyncEx);
        }

        [Fact]
        public void A_damaged_ZipCrypto_entry_read_with_the_right_password_gets_the_same_report()
        {
            // ZipCrypto cannot tell a wrong password that slipped through the check byte from damage: both are a failed CRC-32.
            var damaged = (byte[])Archive.Clone();
            damaged[60] ^= 0xFF;                       // inside the encrypted data of "stored.txt" (local header 40 bytes + 12-byte encryption header)
            using var r = ZipReader.Open(damaged);
            var ex = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.FindEntry("stored.txt")!, GoodPassword));
            Assert.True(ex.WrongPassword);
            Assert.IsType<ZipIntegrityException>(ex.InnerException);
        }

        // ---- Scope guard: only ZipCrypto entries that passed the check byte change. -----------------------------------

        [Fact]
        public void A_damaged_unencrypted_entry_still_throws_ZipIntegrityException()
        {
            var bytes = ZipWriter.ToBytes(w => w.AddBytes("a.bin", Enumerable.Range(0, 400).Select(i => (byte)i).ToArray(),
                new ZipEntryOptions { Method = ZipCompressionMethod.Stored }));
            int start = bytes.AsSpan().IndexOf(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });   // the stored data, wherever the header's extra fields end
            Assert.True(start > 0);
            bytes[start + 10] ^= 0xFF;
            using var r = ZipReader.Open(bytes);
            var ex = Assert.Throws<ZipIntegrityException>(() => r.ReadAllBytes(r.FindEntry("a.bin")!));
            Assert.StartsWith("CRC-32 mismatch", ex.Message);
        }

        [Fact]
        public void A_wrong_AES_password_is_unchanged_and_a_damaged_AES_entry_still_throws_ZipIntegrityException()
        {
            var opts = new ZipWriterOptions { Password = "pw-aes" };
            var content = new string('x', 600);
            var bytes = ZipWriter.ToBytes(w => w.AddText("a", content, null, new ZipEntryOptions { Method = ZipCompressionMethod.Stored }), opts);

            using (var r = ZipReader.Open(bytes))
            {
                var wrong = Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.FindEntry("a")!, "not-pw-aes"));
                Assert.True(wrong.WrongPassword);
                Assert.Equal("Wrong password for entry 'a'.", wrong.Message);
                Assert.Null(wrong.InnerException);
            }

            // Ciphertext starts after: local header 30 + name 1 + AES extra 11 + salt (16 for AES-256: 32/2) + verifier 2.
            var damaged = (byte[])bytes.Clone();
            damaged[30 + 1 + 11 + 16 + 2 + 10] ^= 0xFF;
            using (var r = ZipReader.Open(damaged))
                Assert.Throws<ZipIntegrityException>(() => r.ReadAllBytes(r.FindEntry("a")!, "pw-aes"));
        }
    }
}
