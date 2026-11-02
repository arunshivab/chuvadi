using System.Xml.Linq;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Tests;

public class LayoutSvgTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    private static XDocument Parse(string svg) => XDocument.Parse(svg);

    private static string TextLayer(XDocument d)
        => string.Join(" ", d.Descendants(Svg + "text").Select(t => t.Value).Where(v => v.Trim().Length > 0).Select(v => v.Trim()));

    [Fact]
    public void Svg_is_well_formed_and_sized_in_points()
    {
        using LaidOutDocument doc = DocumentLayout.OpenText("Hello", null, Flow.Options());
        LaidOutPage page = doc.GetPage(0);
        XDocument d = Parse(LayoutSvg.ToSvg(page));
        XElement root = d.Root!;
        Assert.Equal(Svg + "svg", root.Name);
        Assert.EndsWith("pt", (string)root.Attribute("width")!);
        Assert.StartsWith("0 0 ", (string)root.Attribute("viewBox")!);

        XDocument r = Parse(LayoutSvg.ToSvg(page, new LayoutSvgOptions { Size = LayoutSvgSize.Responsive, Background = null }));
        Assert.Equal("100%", (string)r.Root!.Attribute("width")!);
        Assert.Null(r.Root!.Attribute("height"));
    }

    [Fact]
    public void Text_layer_carries_the_source_text_and_glyphs_are_outline_paths()
    {
        const string text = "Hello from the viewer";
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P(text)), Flow.Options());
        LaidOutPage page = doc.GetPage(0);
        XDocument d = Parse(LayoutSvg.ToSvg(page));

        Assert.Equal(text, TextLayer(d));
        foreach (XElement t in d.Descendants(Svg + "text"))
        {
            Assert.Equal("0", (string)t.Attribute("fill-opacity")!); // invisible: the outlines are what is seen
        }

        // Every visible glyph is a <use> of a non-empty path definition; nothing is drawn as visible text.
        Dictionary<string, string> defs = d.Descendants(Svg + "defs").Elements(Svg + "path").ToDictionary(p => (string)p.Attribute("id")!, p => (string)p.Attribute("d")!);
        List<XElement> uses = d.Descendants(Svg + "use").ToList();
        int inked = text.Count(c => c != ' ');
        Assert.Equal(inked, uses.Count);
        foreach (XElement u in uses)
        {
            string id = ((string)u.Attribute(XLink + "href")!).TrimStart('#');
            Assert.True(defs.TryGetValue(id, out string? path));
            Assert.StartsWith("M", path);
        }
        // Repeated letters share one definition ("e" occurs four times).
        Assert.True(defs.Count < uses.Count);
    }

    [Fact]
    public void Selectable_text_can_be_turned_off()
    {
        using LaidOutDocument doc = DocumentLayout.OpenText("Hello", null, Flow.Options());
        XDocument d = Parse(LayoutSvg.ToSvg(doc.GetPage(0), new LayoutSvgOptions { SelectableText = false }));
        Assert.Empty(d.Descendants(Svg + "text"));
        Assert.NotEmpty(d.Descendants(Svg + "use"));
    }

    [Fact]
    public void Markup_in_the_text_is_escaped()
    {
        const string text = "a<b & \"c\" </svg><script>x</script>";
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Flow.Options());
        string svg = LayoutSvg.ToSvg(doc.GetPage(0), new LayoutSvgOptions { Title = "<t>&" });
        XDocument d = Parse(svg);
        Assert.Empty(d.Descendants(Svg + "script"));
        Assert.Equal(text.Replace(" ", string.Empty), TextLayer(d).Replace(" ", string.Empty));
        Assert.Equal("<t>&", d.Descendants(Svg + "title").Single().Value);
    }

    [Fact]
    public void Id_prefix_keeps_two_pages_apart_and_is_validated()
    {
        using LaidOutDocument doc = DocumentLayout.OpenText("Hello", null, Flow.Options());
        string a = LayoutSvg.ToSvg(doc.GetPage(0), new LayoutSvgOptions { IdPrefix = "p1-" });
        string b = LayoutSvg.ToSvg(doc.GetPage(0), new LayoutSvgOptions { IdPrefix = "p2-" });
        HashSet<string> ida = Parse(a).Descendants().Select(e => (string?)e.Attribute("id")).OfType<string>().ToHashSet();
        HashSet<string> idb = Parse(b).Descendants().Select(e => (string?)e.Attribute("id")).OfType<string>().ToHashSet();
        Assert.NotEmpty(ida);
        Assert.Empty(ida.Intersect(idb));
        Assert.All(ida, id => Assert.StartsWith("p1-", id));
        Assert.Throws<ArgumentException>(() => new LayoutSvgOptions { IdPrefix = "1x" });
        Assert.Throws<ArgumentException>(() => new LayoutSvgOptions { IdPrefix = "a\"b" });
    }

    private static LayoutDisplayList List(params LayoutOp[] ops) => new LayoutDisplayList(ops, 200, 100);

    [Fact]
    public void Links_allow_only_safe_schemes_and_map_internal_targets()
    {
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        LayoutDisplayList list = List(
            new LinkOp(new LayoutRect(0, 0, 10, 10), "https://example.org/a?b=1&c=2"),
            new LinkOp(new LayoutRect(0, 10, 10, 10), "javascript:alert(1)"),
            new LinkOp(new LayoutRect(0, 20, 10, 10), "mailto:someone@example.org"),
            new LinkOp(new LayoutRect(0, 30, 10, 10), null, "intro", 2),
            new LinkOp(new LayoutRect(0, 40, 10, 10), null, "nowhere"));
        XDocument d = Parse(LayoutSvg.ToSvg(list, new LayoutSvgOptions { Warning = warnings.Add, ExternalLinkTarget = "_blank" }));
        List<XElement> links = d.Descendants(Svg + "a").ToList();
        Assert.Equal(new[] { "https://example.org/a?b=1&c=2", "mailto:someone@example.org", "#page-3" }, links.Select(a => (string)a.Attribute(XLink + "href")!).ToArray());
        Assert.Equal("_blank", (string)links[0].Attribute("target")!);
        Assert.Equal("noopener noreferrer", (string)links[0].Attribute("rel")!);
        Assert.Null(links[2].Attribute("target"));
        Assert.Single(warnings, w => w.Code == LayoutSvg.LinkDropped); // reported once per call

        XDocument anchors = Parse(LayoutSvg.ToSvg(list, new LayoutSvgOptions { AnchorHref = a => "#bm-" + a, IncludeLinks = true }));
        Assert.Contains(anchors.Descendants(Svg + "a"), a => (string)a.Attribute(XLink + "href")! == "#bm-nowhere");
        Assert.Empty(Parse(LayoutSvg.ToSvg(list, new LayoutSvgOptions { IncludeLinks = false })).Descendants(Svg + "a"));
    }

    private static readonly byte[] Png1x1 = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Images_embed_crop_and_fall_back_to_placeholders()
    {
        LayoutImage png = LayoutImage.FromEncoded(Png1x1);
        LayoutImage tiff = new LayoutImage(new byte[] { 0x49, 0x49, 42, 0 }, LayoutImageFormat.Tiff, 10, 10);
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        LayoutDisplayList list = List(
            new ImageOp(png, new LayoutRect(1, 2, 30, 40)),
            new ImageOp(png, new LayoutRect(50, 2, 30, 40), new LayoutRect(0, 0, 1, 0.5)),
            new ImageOp(tiff, new LayoutRect(100, 2, 30, 40)));
        XDocument d = Parse(LayoutSvg.ToSvg(list, new LayoutSvgOptions { Warning = warnings.Add }));
        List<XElement> images = d.Descendants(Svg + "image").ToList();
        Assert.Equal(2, images.Count);
        Assert.StartsWith("data:image/png;base64,", (string)images[0].Attribute(XLink + "href")!);
        XElement crop = images[1].Parent!;
        Assert.Equal(Svg + "svg", crop.Name);
        Assert.Equal("0 0 1 0.5", (string)crop.Attribute("viewBox")!);
        Assert.Single(warnings, w => w.Code == LayoutSvg.ImageFormat);

        XDocument hosted = Parse(LayoutSvg.ToSvg(list, new LayoutSvgOptions { ImageHref = _ => "https://reader.local/img/1" }));
        Assert.All(hosted.Descendants(Svg + "image"), i => Assert.Equal("https://reader.local/img/1", (string)i.Attribute(XLink + "href")!));
    }

    [Fact]
    public void Clips_dashes_and_translucency_are_kept()
    {
        LayoutDisplayList list = List(
            new PushClipOp(new LayoutRect(0, 0, 50, 50)),
            new FillRectOp(new LayoutRect(0, 0, 100, 100), new LayoutColor(255, 0, 0, 128)),
            PopClipOp.Instance,
            new StrokeLineOp(0, 60, 100, 60, 1, LayoutColor.Black, LineDash.Dash));
        XDocument d = Parse(LayoutSvg.ToSvg(list, new LayoutSvgOptions { IdPrefix = "q" }));
        XElement g = d.Root!.Elements(Svg + "g").Single();
        Assert.Equal("url(#qc1)", (string)g.Attribute("clip-path")!);
        Assert.Equal("0.502", (string)g.Element(Svg + "rect")!.Attribute("fill-opacity")!);
        Assert.Equal("4,3", (string)d.Descendants(Svg + "line").Single().Attribute("stroke-dasharray")!);
    }

    [Fact]
    public void Control_characters_and_lone_surrogates_do_not_break_the_xml()
    {
        Assert.Equal("a b\uFFFDc", LayoutSvg.Clean("a\u0001b\uD800c"));
        Assert.Equal("x\U0001F600", LayoutSvg.Clean("x\U0001F600"));
    }

    [Fact]
    public void Writing_is_cancellable()
    {
        string text = string.Join("\n", Enumerable.Range(1, 60).Select(i => "Line " + i));
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Flow.Options());
        LaidOutPage page = doc.GetPage(0);
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => LayoutSvg.ToSvg(page, null, cts.Token));
    }

    [Fact]
    public void Writes_utf8_without_bom_to_a_stream()
    {
        using LaidOutDocument doc = DocumentLayout.OpenText("\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD", null, Flow.Options());
        using MemoryStream ms = new MemoryStream();
        LayoutSvg.Write(doc.GetPage(0), ms);
        byte[] bytes = ms.ToArray();
        Assert.Equal((byte)'<', bytes[0]);
        Assert.Contains("\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD", System.Text.Encoding.UTF8.GetString(bytes));
    }
}
