using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Import;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Tests;

/// <summary>Shared helpers for the flow tests (also linked into the Docs, Sheets and Documents test projects).</summary>
internal static class Flow
{
    public static LayoutOptions Options()
    {
        LayoutOptions o = new LayoutOptions { Now = new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.FromHours(5.5)) };
        o.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));
        return o;
    }

    public static CharacterFormat Calibri11 { get; } = new CharacterFormat { FontFamily = "Calibri", FontSize = 11 };

    public static FlowDocument Doc(params FlowBlock[] blocks)
    {
        FlowDocument d = new FlowDocument();
        FlowSection s = new FlowSection();
        foreach (FlowBlock b in blocks)
        {
            s.Blocks.Add(b);
        }
        d.Sections.Add(s);
        return d;
    }

    public static FlowParagraph P(string text, ParagraphFormat? pf = null, CharacterFormat? cf = null)
        => FlowParagraph.Of(text, cf ?? Calibri11, pf ?? new ParagraphFormat());

    public static double LineHeight(string file, double size)
    {
        FontFace f = TestFonts.Face(file);
        return f.ToPoints(f.WindowsMetrics.LineHeight, size);
    }

    public static List<LayoutBox> Lines(LaidOutPage p) => p.Boxes.Where(b => b.Kind == LayoutBoxKind.Line).ToList();

    public static IEnumerable<GlyphRunOp> Runs(LaidOutPage p) => p.DisplayList.OfType<GlyphRunOp>();
}
