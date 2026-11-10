using System;
using System.Globalization;
using System.IO;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.ManualTests
{
    /// <summary>Prints resolution, max digit width and line height for the Office core fonts.</summary>
    internal static class FontReport
    {
        private static readonly string[] Families =
        {
            "Calibri", "Cambria", "Arial", "Times New Roman", "Courier New", "Aptos", "Segoe UI", "Nirmala UI",
        };

        public static void Write(TextWriter w)
        {
            w.WriteLine();
            w.WriteLine("Font report (size 11 pt; MDW = Excel max digit width at 96 dpi; LH = Word single line height)");
            Section(w, "Bundled only (deterministic)", allowSystem: false);
            Section(w, "Bundled + system fonts", allowSystem: true);
        }

        private static void Section(TextWriter w, string title, bool allowSystem)
        {
            w.WriteLine();
            w.WriteLine("== " + title);
            LayoutOptions options = new LayoutOptions { AllowSystemFonts = allowSystem };
            options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));
            FontResolver resolver = options.CreateFontResolver();
            foreach (string family in Families)
            {
                FontResolution r = resolver.Resolve(new FontRequest(family));
                FontFace f = r.Face;
                double lh = f.ToPoints(f.WindowsMetrics.LineHeight, 11);
                w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-16} -> {1,-28} {2,-26} {3,-8} MDW {4} px  LH {5:0.000} pt  hdmx {6}",
                    family, f.FullName, r.Kind, f.Origin, f.MaxDigitWidthPixels(11), lh,
                    f.GetDeviceAdvance(f.GetGlyphId('0'), 15).HasValue ? "yes" : "no"));
            }
        }
    }
}
