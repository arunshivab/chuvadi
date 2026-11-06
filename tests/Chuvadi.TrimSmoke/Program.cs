using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Chuvadi.Fonts;
using Chuvadi.Zip;

// Trim and NativeAOT smoke app (D-217 item 2). Exercises Chuvadi.Zip and Chuvadi.Fonts the way a consumer does, and
// exits non-zero on the first thing that is wrong. CI publishes it trimmed (TrimMode=full) and as NativeAOT, with
// warnings as errors, then runs the published executable.
internal static class Program
{
    private static int Main()
    {
        try
        {
            FontsCheck();
            var archive = ZipRoundTrip();
            Console.WriteLine("OK: Fonts " + FontPack.Files.Count + " files, zip " + archive + " bytes");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED: " + ex);
            return 1;
        }
    }

    private static void Require(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
    }

    private static void FontsCheck()
    {
        Require(FontPack.Files.Count == 22, "expected 22 font files, got " + FontPack.Files.Count);
        // Every embedded font is found by name, has the length and SHA-256 its catalog states, and opens as a stream.
        foreach (var f in FontPack.Files)
        {
            var bytes = FontPack.ReadAllBytes(f.FileName);
            Require(bytes.LongLength == f.Length, f.FileName + ": length");
            Require(Convert.ToHexString(SHA256.HashData(bytes)).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase), f.FileName + ": SHA-256");
            using var s = FontPack.Open(f.FileName);
            Require(s.Length == f.Length, f.FileName + ": stream length");
            Require(ReferenceEquals(FontPack.Find(f.FileName), f) || FontPack.Find(f.FileName) == f, f.FileName + ": Find");
        }
        Require(FontPack.LicenseText.Contains("Open Font License", StringComparison.OrdinalIgnoreCase), "license text");
        Require(!FontPack.TryOpen("no-such-font.ttf", out _), "TryOpen of a missing file");
        // The interface a layout or PDF library reads the pack through.
        var pack = FontPack.Instance;
        Require(pack.Name == "Chuvadi.Fonts", "pack name");
        Require(pack.Files.Count == FontPack.Files.Count, "pack file count");
        var first = pack.Files[0];
        using (var s = pack.Open(first.FileName)) Require(s.Length > 0, "pack Open");
        Require(pack.GetLicenseText(first.FileName).Length > 0, "pack licence text");
    }

    private static int ZipRoundTrip()
    {
        var tamil = FontPack.Files.First(f => f.FileName.Contains("Tamil", StringComparison.OrdinalIgnoreCase));
        var fontBytes = FontPack.ReadAllBytes(tamil.FileName);
        var text = string.Concat(Enumerable.Repeat("trim and AOT smoke: ", 500));
        const string password = "sécret";

        var data = ZipWriter.ToBytes(w =>
        {
            using (var e = w.CreateEntry("plain.txt")) { var b = Encoding.UTF8.GetBytes(text); e.Write(b, 0, b.Length); }
            using (var e = w.CreateEntry("stored.bin", new ZipEntryOptions { Method = ZipCompressionMethod.Stored })) e.Write(fontBytes, 0, fontBytes.Length);
            using (var e = w.CreateEntry("secret/aes.txt", new ZipEntryOptions { Encryption = ZipEncryption.Aes256, Password = password }))
            { var b = Encoding.UTF8.GetBytes(text); e.Write(b, 0, b.Length); }
        });

        using var r = ZipReader.Open(data, new ZipReaderOptions { Password = password });
        Require(r.Entries.Count == 3, "entry count");
        Require(r.ReadAllText(r.FindEntry("plain.txt")!) == text, "plain entry");
        Require(r.ReadAllBytes(r.FindEntry("stored.bin")!).AsSpan().SequenceEqual(fontBytes), "font bytes through the zip");
        Require(r.ReadAllText(r.FindEntry("secret/aes.txt")!) == text, "AES entry");
        r.Verify();

        var rejected = false;
        try { r.ReadAllBytes(r.FindEntry("secret/aes.txt")!, "wrong-password"); }
        catch (ZipPasswordRequiredException) { rejected = true; }
        catch (ZipIntegrityException) { rejected = true; } // 1 in 65,536: passes the 2-byte verifier, fails the MAC
        Require(rejected, "a wrong password must be rejected");
        return data.Length;
    }
}
