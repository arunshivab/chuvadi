using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Chuvadi.Fonts.ManualTests
{
    /// <summary>The expected state of the pack (D-113: the files Chuvadi.Layout.Fonts carried, byte for byte).</summary>
    internal static class Expected
    {
        public const int FileCount = 22;

        public const long TotalBytes = 4_083_976;

        /// <summary>SHA-256 over the 22 files concatenated in file-name order.</summary>
        public const string CombinedSha256 = "0872b978d03e9d24948aef22e15b07e57da2dc16dbd89d57f4e04b11ed0b33ba";
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
            yield return ("F3 files unchanged since the move (combined SHA-256)", () =>
            {
                using IncrementalHash all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (FontPackFile f in FontPack.Files)
                {
                    all.AppendData(FontPack.ReadAllBytes(f.FileName));
                }
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
                    Require(ReferenceEquals(pack.GetLicenseText(f.FileName), FontPack.LicenseText), f.FileName + ": licence");
                }
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
