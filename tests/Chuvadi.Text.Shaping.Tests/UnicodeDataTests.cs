// Checks the generated tables against the pinned UCD files themselves, read
// here by a separate, simpler parser, so a fault in the generator's packing
// cannot hide.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.TestSupport;
using Chuvadi.Text.Shaping.Unicode;
using Xunit;

namespace Chuvadi.Text.Shaping.Tests;

public class UnicodeDataTests
{
    private static readonly string Ucd = Path.Combine(TraceLogCheck.FindRepositoryRoot(), "tools", "ucd", UnicodeData.Version);

    private static IEnumerable<(int From, int To, string Value)> Ranges(string file)
    {
        foreach (string raw in File.ReadLines(Path.Combine(Ucd, file)))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] f = line.Split(';');
            string[] r = f[0].Trim().Split("..");
            int a = int.Parse(r[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int b = r.Length > 1 ? int.Parse(r[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : a;
            yield return (a, b, f.Length > 1 ? f[1].Trim() : string.Empty);
        }
    }

    [Fact]
    public void VersionIsPinned() => Assert.Equal("17.0.0", UnicodeData.Version);

    [Fact]
    public void GeneralCategoryAndCombiningClassMatchUnicodeDataTxt()
    {
        string[] gcNames = { "Cn", "Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No", "Pc", "Pd", "Ps", "Pe", "Pi", "Pf", "Po", "Sm", "Sc", "Sk", "So", "Zs", "Zl", "Zp", "Cc", "Cf", "Cs", "Co" };
        string[] expectedGc = Enumerable.Repeat("Cn", 0x110000).ToArray();
        int[] expectedCcc = new int[0x110000];
        int first = -1;
        foreach (string line in File.ReadLines(Path.Combine(Ucd, "UnicodeData.txt")))
        {
            string[] f = line.Split(';');
            int cp = int.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (f[1].EndsWith("First>", StringComparison.Ordinal))
            {
                first = cp;
                continue;
            }

            int from = f[1].EndsWith("Last>", StringComparison.Ordinal) ? first : cp;
            for (int c = from; c <= cp; c++)
            {
                expectedGc[c] = f[2];
                expectedCcc[c] = int.Parse(f[3], CultureInfo.InvariantCulture);
            }
        }

        for (int cp = 0; cp < 0x110000; cp++)
        {
            Assert.True(gcNames[(int)UnicodeData.GetGeneralCategory(cp)] == expectedGc[cp], $"gc U+{cp:X4}");
            Assert.True(UnicodeData.GetCombiningClass(cp) == expectedCcc[cp], $"ccc U+{cp:X4}");
        }
    }

    [Fact]
    public void IndicCategoriesMatchTheirFiles()
    {
        string[] isc = Enumerable.Repeat("Other", 0x110000).ToArray();
        foreach ((int a, int b, string v) in Ranges("IndicSyllabicCategory.txt"))
        {
            for (int c = a; c <= b; c++)
            {
                isc[c] = v;
            }
        }

        string[] ipc = Enumerable.Repeat("NA", 0x110000).ToArray();
        foreach ((int a, int b, string v) in Ranges("IndicPositionalCategory.txt"))
        {
            for (int c = a; c <= b; c++)
            {
                ipc[c] = v;
            }
        }

        for (int cp = 0; cp < 0x110000; cp++)
        {
            Assert.True(UnicodeData.GetIndicSyllabicCategory(cp).ToString() == isc[cp].Replace("_", string.Empty, StringComparison.Ordinal), $"isc U+{cp:X4}");
            Assert.True(UnicodeData.GetIndicPositionalCategory(cp).ToString() == ipc[cp].Replace("_", string.Empty, StringComparison.Ordinal), $"ipc U+{cp:X4}");
        }
    }

    [Fact]
    public void DefaultIgnorableAndScriptMatchTheirFiles()
    {
        bool[] di = new bool[0x110000];
        foreach ((int a, int b, string v) in Ranges("DerivedCoreProperties.txt"))
        {
            if (v == "Default_Ignorable_Code_Point")
            {
                for (int c = a; c <= b; c++)
                {
                    di[c] = true;
                }
            }
        }

        Dictionary<string, string> shortName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in File.ReadLines(Path.Combine(Ucd, "PropertyValueAliases.txt")))
        {
            string[] f = raw.Split('#')[0].Split(';').Select(x => x.Trim()).ToArray();
            if (f.Length >= 3 && f[0] == "sc")
            {
                shortName[f[2]] = f[1];
            }
        }

        string[] sc = Enumerable.Repeat("Zzzz", 0x110000).ToArray();
        foreach ((int a, int b, string v) in Ranges("Scripts.txt"))
        {
            for (int c = a; c <= b; c++)
            {
                sc[c] = shortName[v];
            }
        }

        for (int cp = 0; cp < 0x110000; cp++)
        {
            Assert.True(UnicodeData.IsDefaultIgnorable(cp) == di[cp], $"DI U+{cp:X4}");
            Assert.True(UnicodeData.GetScriptCode(cp) == sc[cp], $"sc U+{cp:X4}");
        }

        Assert.Equal(Script.Tamil, Script.FromCodePoint(0x0BA4));
        Assert.Equal(Script.Inherited, Script.FromCodePoint(0x0301));
        Assert.Equal(Script.Unknown, Script.FromCodePoint(0x110000));
    }

    [Fact]
    public void NormalizationConformance()
    {
        // UAX #15 conformance: NormalizationTest.txt, all parts, plus every code point not in part 1 is unchanged.
        HashSet<int> part1 = new HashSet<int>();
        int part = -1;
        int checkedLines = 0;
        foreach (string raw in File.ReadLines(Path.Combine(Ucd, "NormalizationTest.txt")))
        {
            if (raw.StartsWith("@Part", StringComparison.Ordinal))
            {
                part = raw[5] - '0';
                continue;
            }

            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] c = line.Split(';').Take(5).Select(Decode).ToArray();
            string ctx = line;
            Assert.True(Normalization.ToNfc(c[0]) == c[1], "NFC(c1) " + ctx);
            Assert.True(Normalization.ToNfc(c[1]) == c[1], "NFC(c2) " + ctx);
            Assert.True(Normalization.ToNfc(c[2]) == c[1], "NFC(c3) " + ctx);
            Assert.True(Normalization.ToNfc(c[3]) == c[3], "NFC(c4) " + ctx);
            Assert.True(Normalization.ToNfc(c[4]) == c[3], "NFC(c5) " + ctx);
            Assert.True(Normalization.ToNfd(c[0]) == c[2], "NFD(c1) " + ctx);
            Assert.True(Normalization.ToNfd(c[1]) == c[2], "NFD(c2) " + ctx);
            Assert.True(Normalization.ToNfd(c[2]) == c[2], "NFD(c3) " + ctx);
            Assert.True(Normalization.ToNfd(c[3]) == c[4], "NFD(c4) " + ctx);
            Assert.True(Normalization.ToNfd(c[4]) == c[4], "NFD(c5) " + ctx);
            if (part == 1)
            {
                part1.Add(char.ConvertToUtf32(c[0], 0));
            }

            checkedLines++;
        }

        Assert.True(checkedLines > 19000);
        for (int cp = 0; cp < 0x110000; cp++)
        {
            if ((cp >= 0xD800 && cp <= 0xDFFF) || part1.Contains(cp))
            {
                continue;
            }

            string s = char.ConvertFromUtf32(cp);
            Assert.True(Normalization.ToNfc(s) == s && Normalization.ToNfd(s) == s, $"U+{cp:X4} must be unchanged");
        }
    }

    private static string Decode(string field)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string h in field.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ConvertFromUtf32(int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }
}
