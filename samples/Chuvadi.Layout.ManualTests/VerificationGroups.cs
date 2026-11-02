using System;
using System.Collections.Generic;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.ManualTests
{
    /// <summary>End-to-end verification groups. Each throws on failure.</summary>
    internal static partial class VerificationGroups
    {
        public static IEnumerable<Action> All()
        {
            yield return L1_BundledPackLoadsEveryFace;
            yield return L1_OfficeFontsResolveToMetricCompatibleSubstitutes;
            yield return L1_ExcelMaxDigitWidthReferencePoints;
            yield return L1_IndicScriptFallback;
            yield return L1_EmbeddedFontDeobfuscationRoundTrip;
            foreach (Action a in Later())
            {
                yield return a;
            }
        }

        private static partial IEnumerable<Action> Later();

        internal static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static FontResolver Bundled() => new FontResolver(new IFontSource[] { BundledFontSource.Instance, BuiltInFontSource.Instance });

        private static void L1_BundledPackLoadsEveryFace()
        {
            int n = 0;
            foreach (FontFaceDescriptor d in BundledFontSource.Instance.GetFaces())
            {
                FontFace f = BundledFontSource.Instance.LoadFace(d);
                Check(f.FamilyName == d.FamilyName && f.Weight == d.Weight && f.IsItalic == d.IsItalic, "catalog mismatch for " + d.Key);
                n++;
            }
            Check(n == 29, "expected 29 bundled faces, got " + n);
            Check(BuiltInFontSource.Instance.GetFaces().Count == 22, "expected 22 built-in LiPi faces");
        }

        private static void L1_OfficeFontsResolveToMetricCompatibleSubstitutes()
        {
            FontResolver r = Bundled();
            (string, string)[] pairs =
            {
                ("Calibri", "Carlito"), ("Cambria", "Caladea"), ("Arial", "Liberation Sans"),
                ("Times New Roman", "Liberation Serif"), ("Courier New", "Liberation Mono"),
            };
            foreach ((string requested, string expected) in pairs)
            {
                FontResolution res = r.Resolve(new FontRequest(requested));
                Check(res.Face.FamilyName == expected && res.Kind == FontMatchKind.MetricCompatibleSubstitute,
                    requested + " resolved to " + res.Face.FamilyName + " (" + res.Kind + ")");
            }
            Check(r.ReportedSubstitutions.Count == pairs.Length, "every substitution must be reported once");
        }

        private static void L1_ExcelMaxDigitWidthReferencePoints()
        {
            FontResolver r = Bundled();
            int calibri = r.Resolve(new FontRequest("Calibri")).Face.MaxDigitWidthPixels(11);
            int arial = r.Resolve(new FontRequest("Arial")).Face.MaxDigitWidthPixels(10);
            Check(calibri == 7, "Calibri 11 MDW should be 7 px, got " + calibri);
            Check(arial == 7, "Arial 10 MDW should be 7 px, got " + arial);
        }

        private static void L1_IndicScriptFallback()
        {
            FontResolver r = Bundled();
            FontResolution primary = r.Resolve(new FontRequest("Calibri"));
            int[] samples = { 0x0915, 0x0995, 0x0A15, 0x0A95, 0x0B15, 0x0B95, 0x0C15, 0x0C95, 0x0D15 };
            foreach (int cp in samples)
            {
                FontResolution f = r.ResolveForCodepoint(primary, cp);
                Check(f.Face.HasGlyph(cp) && f.Kind == FontMatchKind.ScriptFallback,
                    $"U+{cp:X4} ({UnicodeScripts.GetScript(cp)}) not covered by fallback");
            }
        }

        private static void L1_EmbeddedFontDeobfuscationRoundTrip()
        {
            byte[] font = BundledFontSource.ReadResource("Caladea-Regular.ttf");
            const string Key = "{1A2B3C4D-5E6F-7081-92A3-B4C5D6E7F809}";
            byte[] back = EmbeddedFontObfuscation.Deobfuscate(EmbeddedFontObfuscation.Obfuscate(font, Key), Key);
            Check(FontFace.Load(back).FamilyName == "Caladea", "round trip failed");
        }
    }
}
