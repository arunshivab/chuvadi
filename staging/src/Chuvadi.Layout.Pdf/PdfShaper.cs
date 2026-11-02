using System;
using System.Collections.Generic;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Text;
using PdfFontFace = Chuvadi.Pdf.Fonts.Rendering.FontFace;
using PdfShapedGlyph = Chuvadi.Pdf.Text.Shaping.ShapedGlyph;
using PdfShaping = Chuvadi.Pdf.Text.Shaping;

namespace Chuvadi.Layout.Pdf
{
    /// <summary>
    /// Complex-script shaping for Chuvadi.Layout through Chuvadi.Pdf.Text.Shaping: Indic reordering, GSUB and GPOS
    /// for Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada and Malayalam with faces that carry
    /// GSUB (TrueType or CFF outlines, single fonts or collection faces). Set <c>LayoutOptions.Shaper = new PdfShaper()</c>.
    /// Each face is loaded once by Chuvadi.Pdf (<c>TextShaper.Shape(FontFace, ...)</c>, since 3.25), which caches its tables
    /// and feature lookups, so the line-fitting loop does no font parsing.
    /// </summary>
    /// <remarks>
    /// The whole run is shaped at once, so contextual forms across syllables apply as in HarfBuzz (for example the Bengali
    /// e-sign form in \u09AA\u09CD\u09B0\u09A4\u09BF\u09AC\u09C7\u09A6\u09A8, which syllable-by-syllable shaping missed). Since Chuvadi.Pdf 3.26 each glyph's cluster is
    /// the UTF-16 index of its cluster's first character, with HarfBuzz's cluster merging (a split vowel, a reordered
    /// matra and a ligature share their cluster); the adapter reports it as an index into Layout's text, and Layout
    /// measures each cluster by the glyphs that report it.
    /// <para>
    /// Chuvadi.Pdf 3.26's shaper is a port of HarfBuzz's Indic shaper and matches HarfBuzz 15.0.0 glyph for glyph on its
    /// conformance corpus (all nine LiPi Indic scripts).
    /// </para>
    /// </remarks>
    public sealed class PdfShaper : IShaper
    {
        public string Name => "Chuvadi.Pdf.Text.Shaping";

        public bool Supports(FontFace face, UnicodeScript script)
        {
            ArgumentNullException.ThrowIfNull(face);
            return ToLipi(script) is not null && face.HasGsub;
        }

        public IReadOnlyList<ShapedGlyph> Shape(FontFace face, string text, int start, int length, ShapingOptions options)
        {
            ArgumentNullException.ThrowIfNull(face);
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(options);
            if (start < 0 || length < 0 || start + length > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            PdfShaping.LipiScript script = ToLipi(options.Script) ?? throw new ArgumentException($"Script {options.Script} is not supported by {Name}.", nameof(options));
            PdfFontFace pdfFace;
            try
            {
                pdfFace = PdfFaces.Get(face);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A face Chuvadi.Pdf cannot load (for example CFF2): nominal glyphs.
                return SimpleShaper.Instance.Shape(face, text, start, length, options);
            }
            double toUnits = pdfFace.UnitsPerEm / 1000.0;
            PdfShaping.ShapingFeatures features = new PdfShaping.ShapingFeatures { Kern = options.Kerning };
            string run = text.Substring(start, length);
            IReadOnlyList<PdfShapedGlyph> shaped;
            try
            {
                shaped = PdfShaping.TextShaper.Shape(pdfFace, run, script, features);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Malformed font data or an unsupported table: nominal glyphs.
                return SimpleShaper.Instance.Shape(face, text, start, length, options);
            }
            List<ShapedGlyph> result = new List<ShapedGlyph>(shaped.Count);
            foreach (PdfShapedGlyph g in shaped)
            {
                result.Add(new ShapedGlyph(
                    g.GlyphId,
                    start + Math.Clamp(g.Cluster, 0, Math.Max(0, length - 1)),
                    (int)Math.Round(g.XAdvance * toUnits),
                    (int)Math.Round(g.XOffset * toUnits),
                    (int)Math.Round(g.YOffset * toUnits)));
            }
            return result;
        }

        private static PdfShaping.LipiScript? ToLipi(UnicodeScript script) => script switch
        {
            UnicodeScript.Devanagari => PdfShaping.LipiScript.Devanagari,
            UnicodeScript.Bengali => PdfShaping.LipiScript.Bengali,
            UnicodeScript.Gurmukhi => PdfShaping.LipiScript.Gurmukhi,
            UnicodeScript.Gujarati => PdfShaping.LipiScript.Gujarati,
            UnicodeScript.Oriya => PdfShaping.LipiScript.Odia,
            UnicodeScript.Tamil => PdfShaping.LipiScript.Tamil,
            UnicodeScript.Telugu => PdfShaping.LipiScript.Telugu,
            UnicodeScript.Kannada => PdfShaping.LipiScript.Kannada,
            UnicodeScript.Malayalam => PdfShaping.LipiScript.Malayalam,
            _ => null,
        };
    }
}
