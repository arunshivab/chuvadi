using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Chuvadi.Fonts.ManualTests
{
    /// <summary>
    /// The expected state of the pack: the 22 LiPi Sans files Chuvadi.Layout.Fonts carried, byte for byte (D-113), and
    /// since 1.2.0 the Noto Emoji face as the Pdf chat delivered it (D-418, D-420).
    /// </summary>
    internal static class Expected
    {
        public const int FileCount = 23;

        public const long TotalBytes = 4_970_604;

        public const int LiPiFileCount = 22;

        /// <summary>SHA-256 over the 22 LiPi Sans files concatenated in file-name order (unchanged since 1.0.0).</summary>
        public const string CombinedSha256 = "0872b978d03e9d24948aef22e15b07e57da2dc16dbd89d57f4e04b11ed0b33ba";

        public const string EmojiFile = "NotoEmoji-Regular.ttf";

        /// <summary>SHA-256 of NotoEmoji-Regular.ttf as delivered (PROVENANCE.md v2).</summary>
        public const string EmojiSha256 = "1ada997ccb67bf2404828e25528e5257d6db10c80287e77684abf6737a5d198f";

        /// <summary>SHA-256 of the Noto Emoji OFL.txt as delivered, before any line-ending conversion.</summary>
        public const string EmojiLicenseSha256 = "500bb1ccf43df7bbb522112f9133a52b16e1c35e809632f5d8609b179152de5b";
    }

    /// <summary>Verification suite: checks every file of the pack against its catalog row; exit code 1 if any check fails.</summary>
    public static class Program
    {
        public static int Main(string[] args) => Run(Console.Out);

        internal static int Run(TextWriter log)
        {
            int passed = 0, failed = 0;
            foreach ((string name, Action check) in Checks())
            {
                try
                {
                    check();
                    log.WriteLine("PASS  " + name);
                    passed++;
                }
                catch (Exception ex)
                {
                    log.WriteLine("FAIL  " + name + " -> " + ex.Message);
                    failed++;
                }
            }
            log.WriteLine();
            log.WriteLine("Total: " + (passed + failed) + "  Passed: " + passed + "  Failed: " + failed);
            return failed == 0 ? 0 : 1;
        }

        private static IEnumerable<(string, Action)> Checks()
        {
            yield return ("F1 pack size", () =>
            {
                Require(FontPack.Files.Count == Expected.FileCount, $"{FontPack.Files.Count} files, expected {Expected.FileCount}");
                long total = 0;
                foreach (FontPackFile f in FontPack.Files)
                {
                    total += f.Length;
                }
                Require(total == Expected.TotalBytes, $"{total} bytes, expected {Expected.TotalBytes}");
            });
            yield return ("F2 every file matches its catalog row", () =>
            {
                foreach (FontPackFile f in FontPack.Files)
                {
                    byte[] data = FontPack.ReadAllBytes(f.FileName);
                    Require(data.LongLength == f.Length, f.FileName + ": length");
                    Require(Convert.ToHexStringLower(SHA256.HashData(data)) == f.Sha256, f.FileName + ": SHA-256");
                    SfntInfo info = SfntInfo.Read(data);
                    Require(info.Name(1) == f.FamilyName && info.Name(2) == f.StyleName && info.Name(4) == f.FullName && info.Name(6) == f.PostScriptName, f.FileName + ": names");
                    Require(info.Weight == f.Weight && info.Italic == f.Italic, f.FileName + ": weight or italic");
                    Require(info.FsType == 0, f.FileName + ": embedding must be installable");
                }
            });
            yield return ("F3 the LiPi Sans files unchanged since the move (combined SHA-256)", () =>
            {
                using IncrementalHash all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int lipi = 0;
                foreach (FontPackFile f in FontPack.Files)
                {
                    if (f.Script == FontScript.Emoji)
                    {
                        continue;
                    }
                    all.AppendData(FontPack.ReadAllBytes(f.FileName));
                    lipi++;
                }
                Require(lipi == Expected.LiPiFileCount, lipi + " LiPi Sans files");
                string h = Convert.ToHexStringLower(all.GetHashAndReset());
                Require(h == Expected.CombinedSha256, h);
            });
            yield return ("F4 licence text", () => Require(FontPack.LicenseText.Contains("OPEN FONT LICENSE", StringComparison.OrdinalIgnoreCase), "OFL text missing"));
            yield return ("F5 the pack as an IFontFilePack (1.1.0)", () =>
            {
                IFontFilePack pack = FontPack.Instance;
                Require(pack.Name == "Chuvadi.Fonts", pack.Name);
                Require(pack.Files.Count == FontPack.Files.Count, "file count");
                for (int i = 0; i < pack.Files.Count; i++)
                {
                    FontFileInfo f = pack.Files[i];
                    FontPackFile p = FontPack.Files[i];
                    Require(f.FileName == p.FileName && f.FamilyName == p.FamilyName && f.StyleName == p.StyleName && f.FullName == p.FullName
                        && f.PostScriptName == p.PostScriptName && f.Weight == p.Weight && f.Italic == p.Italic && f.Length == p.Length && f.Sha256 == p.Sha256, f.FileName + ": differs from FontPack.Files");
                    SfntInfo info = SfntInfo.Read(FontPack.ReadAllBytes(f.FileName));
                    Require(f.TypographicFamilyName == (info.Name(16) ?? info.Name(1)), f.FileName + ": typographic family");
                    Require(f.WidthClass == info.WidthClass, f.FileName + ": width class");
                    Require(ReferenceEquals(pack.GetLicenseText(f.FileName), FontPack.GetLicenseText(f.FileName)), f.FileName + ": licence");
                }
            });
            yield return ("F6 Noto Emoji (1.2.0): the delivered file, its names, and its own licence", () =>
            {
                FontPackFile? e = FontPack.Find(Expected.EmojiFile);
                Require(e is not null, "missing");
                byte[] data = FontPack.ReadAllBytes(e!.FileName);
                Require(Convert.ToHexStringLower(SHA256.HashData(data)) == Expected.EmojiSha256, "SHA-256 differs from PROVENANCE.md");
                Require(e.Script == FontScript.Emoji && e.FamilyName == "Noto Emoji" && e.Weight == 400 && !e.Italic, "catalog row");
                Require(FontPack.Files[FontPack.Files.Count - 1] == e, "not last in file-name order");
                SfntInfo info = SfntInfo.Read(data);
                Require(info.Name(0) == "Copyright 2013 Google LLC" && info.Name(5) == "Version 3.002", "copyright or version");
                string licence = FontPack.GetLicenseText(e.FileName);
                Require(licence.StartsWith("Copyright 2013 Google LLC", StringComparison.Ordinal), "licence copyright line");
                Require(licence.Contains("SIL OPEN FONT LICENSE Version 1.1", StringComparison.Ordinal), "licence text");
                Require(!licence.Contains("Reserved Font Name \"", StringComparison.Ordinal) && !licence.Contains("with Reserved Font Name", StringComparison.Ordinal), "a Reserved Font Name is declared");
                string lf = licence.Replace("\r\n", "\n", StringComparison.Ordinal);
                Require(Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(lf))) == Expected.EmojiLicenseSha256, "licence text differs from the delivered OFL.txt");
                Require(!ReferenceEquals(licence, FontPack.LicenseText) && FontPack.GetLicenseText("LiPi-Sans-Tamil-Regular.ttf") == FontPack.LicenseText, "LiPi files keep LicenseText");
            });
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
