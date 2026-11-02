using System;
using System.Collections.Generic;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L2Groups()
        {
            yield return L2_LineBreakingLatinAndNumbers;
            yield return L2_LineBreakingIndicSyllables;
            yield return L2_GraphemeClustersForConjunctsAndEmoji;
            yield return L2_SimpleShaperWithKerning;
            yield return L2_ScriptItemization;
        }

        private static void L2_LineBreakingLatinAndNumbers()
        {
            string s = "Total: $1,234.50 (incl. tax) - see https://example.org/a/b";
            LineBreakOpportunity[] o = LineBreaker.GetOpportunities(s);
            int dollar = s.IndexOf('$');
            for (int i = dollar + 1; i < dollar + 9; i++)
            {
                Check(o[i] == LineBreakOpportunity.NoBreak, "number must not break at " + i);
            }
            Check(o[s.IndexOf("(incl", StringComparison.Ordinal)] == LineBreakOpportunity.Allowed, "break before '(' after a space");
            Check(o[s.Length] == LineBreakOpportunity.Mandatory, "end of text is mandatory");
        }

        private static void L2_LineBreakingIndicSyllables()
        {
            // Malayalam "\u0D2E\u0D32\u0D2F\u0D3E\u0D33\u0D02 \u0D2D\u0D3E\u0D37": breaks only at the space.
            string s = "\u0D2E\u0D32\u0D2F\u0D3E\u0D33\u0D02 \u0D2D\u0D3E\u0D37";
            LineBreakOpportunity[] o = LineBreaker.GetOpportunities(s);
            for (int i = 1; i < s.Length; i++)
            {
                bool allowed = o[i] == LineBreakOpportunity.Allowed;
                Check(allowed == (i == 7), "unexpected opportunity at " + i);
            }
        }

        private static void L2_GraphemeClustersForConjunctsAndEmoji()
        {
            Check(GraphemeClusters.NextBoundary("\u0915\u094D\u0937x", 0) == 3, "Devanagari KSSA must be one cluster");
            Check(GraphemeClusters.NextBoundary("\u0B95\u0BCD\u0BB7x", 0) == 2, "Tamil KA+VIRAMA is not linked to SSA (InCB=None for Tamil virama)");
            string thumbs = "\U0001F44D\U0001F3FDx";
            Check(GraphemeClusters.NextBoundary(thumbs, 0) == 4, "emoji modifier sequence must be one cluster");
        }

        private static void L2_SimpleShaperWithKerning()
        {
            FontFace f = BundledFontSource.Instance.LoadFace(Find("LiberationSans-Regular.ttf"));
            IReadOnlyList<ShapedGlyph> glyphs = SimpleShaper.Instance.Shape(f, "To", 0, 2, new ShapingOptions { Kerning = true });
            Check(glyphs.Count == 2 && glyphs[0].XAdvance < f.GetAdvance(glyphs[0].GlyphId), "'To' must kern in " + f.FullName);
        }

        private static FontFaceDescriptor Find(string key)
        {
            foreach (FontFaceDescriptor d in BundledFontSource.Instance.GetFaces())
            {
                if (d.Key == key)
                {
                    return d;
                }
            }
            throw new InvalidOperationException(key + " not bundled");
        }

        private static void L2_ScriptItemization()
        {
            string s = "Hindi: \u0939\u093F\u0928\u094D\u0926\u0940, Tamil: \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD";
            List<ScriptRun> runs = ScriptItemizer.Itemize(s, 0, s.Length);
            Check(runs.Count == 4, "expected Latin/Devanagari/Latin/Tamil runs, got " + runs.Count);
            Check(runs[1].Script == UnicodeScript.Devanagari && runs[3].Script == UnicodeScript.Tamil, "wrong scripts");
        }
    }
}
