using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Phase 6b — exact image anchoring: pictures load as image runs in place (inline, or floating
/// on their anchor paragraph) with offsets, alignments, wrap type/side/polygon, distances,
/// behind/in front, z-order, crop, rotation and flips; GetImagePlacements for layout engines.
/// </summary>
public static class ImagesModelTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace WPn = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace An = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    private static readonly byte[] Png2 = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEklEQVR42mP8z8DwnwEJMBJQAD8YBfvqL4hZAAAAAElFTkSuQmCC");

    public static void Run(string outDir)
    {
        RoundTrip(outDir);
        Placements(outDir);
        WordStyle(outDir);
        ReviewRegressions(outDir);
    }

    private static Document Rich()
    {
        var doc = new Document();
        var inline = ImageSpec.Inline(Png, "image/png", 120, 80);
        inline.AltText = "Lab logo";
        inline.Title = "Logo";
        inline.Crop = new ImageCrop(0.1, 0.05, 0.2, 0);
        inline.RotationDegrees = 90;
        inline.FlipHorizontal = true;
        inline.EffectExtent = new ImageEffectExtent(1, 2, 3, 4);
        doc.AddParagraph().Text("Inline: ").Image(inline).Text(" after");

        var poly = new WrapPolygon { Edited = true };
        poly.Points.AddRange(new (long, long)[] { (0, 0), (10800, 21600), (21600, 0), (0, 0) });
        var floating = ImageSpec.Float(Png2, "image/png", 100, 100, new FloatingPosition
        {
            HorizontalAnchor = HorizontalAnchor.Page, HorizontalOffsetPt = 72.5,
            VerticalAnchor = VerticalAnchor.Paragraph, VAlign = VerticalAlignment.Center,
            Wrap = TextWrap.Tight, WrapSide = WrapSide.Left, WrapPolygon = poly,
            DistanceTopPt = 2, DistanceBottomPt = 4, DistanceLeftPt = 6, DistanceRightPt = 8,
            RelativeHeight = 251660288, LockAnchor = true, AllowOverlap = false, LayoutInCell = false,
        });
        floating.Name = "Stamp";
        floating.FlipVertical = true;
        doc.AddParagraph("Anchor paragraph").Image(floating);

        var behind = ImageSpec.Float(Png, "image/png", 300, 200, new FloatingPosition
        {
            UseSimplePosition = true, SimplePositionXPt = 36, SimplePositionYPt = 48,
            Wrap = TextWrap.None, BehindText = true,
        });
        doc.AddParagraph("Behind").Image(behind);

        var t = doc.AddTable(2);
        t.AddRow("cell", "");
        t.Rows[0].Cells[1].Paragraphs[0].Image(ImageSpec.Inline(Png, "image/png", 20, 20));
        doc.Header.Add(new Paragraph().Image(ImageSpec.Inline(Png, "image/png", 30, 15)));
        return doc;
    }

    private static void RoundTrip(string outDir)
    {
        var a = Path.Combine(outDir, "img-rich-a.docx");
        Rich().SaveTo(a);

        Check("pictures load as image runs in place with every property (crop, rotation, flips, wrap side/polygon, distances, z-order, simple position)", () =>
        {
            var doc = Document.Load(a);
            var p0 = (Paragraph)doc.Blocks[0];
            AssertTrue(p0.Runs.Count == 3 && p0.Runs[1].Image is not null && p0.GetText() == "Inline:  after", "inline image between the text runs");
            var i = p0.Runs[1].Image!;
            AssertTrue(i.WidthPt == 120 && i.HeightPt == 80 && i.AltText == "Lab logo" && i.Title == "Logo" && i.Placement == ImagePlacement.Inline, "inline basics");
            AssertTrue(i.Crop == new ImageCrop(0.1, 0.05, 0.2, 0) && i.RotationDegrees == 90 && i.FlipHorizontal && i.EffectExtent == new ImageEffectExtent(1, 2, 3, 4), "crop/rotation/effects");
            AssertTrue(i.Bytes.SequenceEqual(Png) && i.ContentType == "image/png", "bytes");

            var f = ((Paragraph)doc.Blocks[1]).Runs.Single(r => r.Image is not null).Image!;
            var pos = f.Position!;
            AssertTrue(f.Placement == ImagePlacement.Floating && f.Name == "Stamp" && f.FlipVertical, "floating basics");
            AssertTrue(pos.HorizontalAnchor == HorizontalAnchor.Page && pos.HorizontalOffsetPt == 72.5 && pos.VerticalAnchor == VerticalAnchor.Paragraph && pos.VAlign == VerticalAlignment.Center, "position");
            AssertTrue(pos.Wrap == TextWrap.Tight && pos.WrapSide == WrapSide.Left && pos.WrapPolygon!.Edited && pos.WrapPolygon.Points.Count == 4 && pos.WrapPolygon.Points[1] == (10800, 21600), "wrap");
            AssertTrue(pos.EffectiveDistances == (2, 4, 6, 8) && pos.RelativeHeight == 251660288 && pos.LockAnchor && !pos.AllowOverlap && !pos.LayoutInCell, "distances/z/lock");

            var b = ((Paragraph)doc.Blocks[2]).Runs.Single(r => r.Image is not null).Image!.Position!;
            AssertTrue(b.UseSimplePosition && b.SimplePositionXPt == 36 && b.SimplePositionYPt == 48 && b.Wrap == TextWrap.None && b.BehindText, "behind text, simple position");
        });
        Check("load → save reproduces document.xml and the header exactly; the same picture bytes are stored once", () =>
        {
            var b = Path.Combine(outDir, "img-rich-b.docx");
            Document.Load(a).SaveTo(b);
            AssertTrue(ReadPartXml(a, "word/document.xml") == ReadPartXml(b, "word/document.xml"), "document.xml differs");
            AssertTrue(ReadPartXml(a, "word/header1.xml") == ReadPartXml(b, "word/header1.xml"), "header differs");
            using var z = ZipFile.OpenRead(b);
            var media = z.Entries.Where(e => e.FullName.StartsWith("word/media/")).ToList();
            AssertTrue(media.Count == 2, $"media parts {media.Count} (one per distinct picture)");
        });
    }

    private static void Placements(string outDir)
    {
        Check("GetImagePlacements: paragraph, run index, host, section and table cell for every picture", () =>
        {
            var doc = Document.Load(Path.Combine(outDir, "img-rich-a.docx"));
            var all = doc.GetImagePlacements();
            AssertTrue(all.Count == 5, $"count {all.Count}");
            AssertTrue(all[0].RunIndex == 1 && all[0].Host == ImageHost.Body && all[0].Cell is null && ReferenceEquals(all[0].Section, doc.Section), "first");
            AssertTrue(all[1].Paragraph.GetText() == "Anchor paragraph" && all[1].Image.Placement == ImagePlacement.Floating, "anchor paragraph");
            AssertTrue(all[3].Cell is { Row: 0, Column: 1 } && all[3].Host == ImageHost.Body, "table cell");
            AssertTrue(all[4].Host == ImageHost.Header && ReferenceEquals(all[4].HeaderFooter, doc.Section.Header), "header");
            var fmt = doc.GetEffectiveParagraphFormat(all[1].Paragraph); // anchor paragraph formatting for layout
            AssertTrue(fmt is not null, "effective format of the anchor");
        });
    }

    private static void WordStyle(string outDir)
    {
        Check("Word-style drawings: picture in mc:AlternateContent, chart (not a picture) keeps no image, missing media skipped, hostile values ignored", () =>
        {
            const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
            string Anchor(string graphic, string extra = "") =>
                "<wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\" relativeHeight=\"251659264\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">" +
                "<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"column\"><wp:posOffset>-123825</wp:posOffset></wp:positionH>" +
                "<wp:positionV relativeFrom=\"paragraph\"><wp:align>top</wp:align></wp:positionV><wp:extent cx=\"1270000\" cy=\"635000\"/>" +
                "<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:wrapSquare wrapText=\"largest\"/><wp:docPr id=\"5\" name=\"Picture 5\" descr=\"seal\"/>" +
                "<wp:cNvGraphicFramePr/><a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" + graphic + "</a:graphic>" + extra + "</wp:anchor>";
            string Pic(string relId) => "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
                "<pic:nvPicPr><pic:cNvPr id=\"0\" name=\"x\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"" + relId + "\"/><a:srcRect l=\"5000\" b=\"99999999\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
                "<pic:spPr><a:xfrm rot=\"5400000\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"635000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData>";
            const string Chart = "<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rIdChart\"/></a:graphicData>";
            var body =
                "<w:p><w:r><w:t>A</w:t></w:r><w:r><mc:AlternateContent xmlns:mc=\"" + Mc + "\"><mc:Choice Requires=\"wp14\"><w:drawing>" + Anchor(Pic("rIdPic")) + "</w:drawing></mc:Choice>" +
                "<mc:Fallback><w:drawing>" + Anchor(Pic("rIdPic")) + "</w:drawing></mc:Fallback></mc:AlternateContent></w:r></w:p>" +
                "<w:p><w:r><w:drawing>" + Anchor(Chart) + "</w:drawing></w:r><w:r><w:t>chart</w:t></w:r></w:p>" +
                "<w:p><w:r><w:drawing>" + Anchor(Pic("rIdMissing")) + "</w:drawing></w:r><w:r><w:t>gone</w:t></w:r></w:p>";
            var path = Path.Combine(outDir, "img-word.docx");
            var d = new Document();
            d.AddParagraph("base");
            d.SaveTo(path);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                void Edit(string entry, Func<string, string> change)
                {
                    var e = zip.GetEntry(entry)!;
                    string xml;
                    using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
                    e.Delete();
                    using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
                    w.Write(change(xml));
                }
                using (var m = zip.CreateEntry("word/media/seal.png").Open()) m.Write(Png2);
                Edit("word/_rels/document.xml.rels", x => x.Replace("</Relationships>",
                    "<Relationship Id=\"rIdPic\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/seal.png\"/></Relationships>"));
                Edit("[Content_Types].xml", x => x.Contains("Extension=\"png\"") ? x : x.Replace("</Types>", "<Default Extension=\"png\" ContentType=\"image/png\"/></Types>"));
                Edit("word/document.xml", x =>
                {
                    int start = x.IndexOf("<w:body>", StringComparison.Ordinal) + "<w:body>".Length;
                    int sect = x.IndexOf("<w:sectPr", StringComparison.Ordinal);
                    return x[..start].Replace("<w:document ", "<w:document xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" ") + body + x[sect..];
                });
            }
            var doc = Document.Load(path);
            var ps = doc.Blocks.OfType<Paragraph>().ToList();
            var img = ps[0].Runs.Where(r => r.Image is not null).ToList();
            AssertTrue(img.Count == 1, $"picture read once (Choice), got {img.Count}");
            var spec = img[0].Image!;
            AssertTrue(spec.Position!.HorizontalOffsetPt == -9.75 && spec.Position.VAlign == VerticalAlignment.Top && spec.Position.WrapSide == WrapSide.Largest
                && spec.Position.DistanceLeftPt == 9 && spec.RotationDegrees == 90 && spec.AltText == "seal" && spec.Name is null, "Word anchor read");
            AssertTrue(spec.Crop!.Left == 0.05 && Math.Abs(spec.Crop.Bottom - 0.99) < 1e-9, "crop clamped (never nothing visible)");
            AssertTrue(!ps[1].Runs.Any(r => r.Image is not null) && ps[1].GetText() == "chart", "chart is not an image");
            AssertTrue(!ps[2].Runs.Any(r => r.Image is not null) && ps[2].GetText() == "gone", "missing media skipped");
            var outPath = Path.Combine(outDir, "img-word-out.docx");
            doc.SaveTo(outPath);
            var x2 = XElement.Parse(ReadPartXml(outPath, "word/document.xml"));
            AssertTrue(x2.Descendants(WPn + "anchor").Count() == 1 && x2.Descendants(An + "srcRect").Count() == 1, "written back");
        });
    }

    private const string McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>A docx whose body is <paramref name="bodyInner"/>, with media/seal.png (rIdPic) and optional extra relationships.</summary>
    private static string Inject(string outDir, string name, string bodyInner, string extraRels = "", byte[]? media = null, string mediaName = "seal.png")
    {
        var path = Path.Combine(outDir, name);
        var d = new Document();
        d.AddParagraph("base");
        d.SaveTo(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        void Edit(string entry, Func<string, string> change)
        {
            var e = zip.GetEntry(entry)!;
            string xml;
            using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
            e.Delete();
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(change(xml));
        }
        using (var m = zip.CreateEntry("word/media/" + mediaName).Open()) m.Write(media ?? Png2);
        Edit("word/_rels/document.xml.rels", x => x.Replace("</Relationships>",
            "<Relationship Id=\"rIdPic\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/" + mediaName + "\"/>" + extraRels + "</Relationships>"));
        var ext = Path.GetExtension(mediaName).TrimStart('.');
        Edit("[Content_Types].xml", x => x.Contains($"Extension=\"{ext}\"") ? x : x.Replace("</Types>", $"<Default Extension=\"{ext}\" ContentType=\"{(ext == "png" ? "image/png" : "application/octet-stream")}\"/></Types>"));
        Edit("word/document.xml", x =>
        {
            int start = x.IndexOf("<w:body>", StringComparison.Ordinal) + "<w:body>".Length;
            int sect = x.IndexOf("<w:sectPr", StringComparison.Ordinal);
            return x[..start].Replace("<w:document ", "<w:document xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\" xmlns:mc=\"" + McNs + "\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" ") + bodyInner + x[sect..];
        });
        return path;
    }

    private static string PicGraphic(string blipAttrs) =>
        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"p\"/><pic:cNvPicPr/></pic:nvPicPr>" +
        "<pic:blipFill><a:blip " + blipAttrs + "/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"635000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic>";

    private static string AnchorXml(string positionH, string attrs = "behindDoc=\"0\"", string wrap = "<wp:wrapSquare wrapText=\"bothSides\"/>", string graphic = "", string tail = "", string extent = "cx=\"1270000\" cy=\"635000\"") =>
        "<wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"1\" " + attrs + " locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">" +
        "<wp:simplePos x=\"0\" y=\"0\"/>" + positionH + "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
        "<wp:extent " + extent + "/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>" + wrap + "<wp:docPr id=\"7\" name=\"x\"/><wp:cNvGraphicFramePr/>" +
        (graphic.Length > 0 ? graphic : PicGraphic("r:embed=\"rIdPic\"")) + tail + "</wp:anchor>";

    private static void ReviewRegressions(string outDir)
    {
        Check("percentage positions and sizes (wp14, in mc:AlternateContent) load, and are written back in Word's form with the absolute fallback", () =>
        {
            var choice = AnchorXml("<wp:positionH relativeFrom=\"page\"><wp14:pctPosHOffset>50000</wp14:pctPosHOffset></wp:positionH>",
                tail: "<wp14:sizeRelH relativeFrom=\"margin\"><wp14:pctWidth>40000</wp14:pctWidth></wp14:sizeRelH>");
            var fallback = AnchorXml("<wp:positionH relativeFrom=\"page\"><wp:posOffset>3886200</wp:posOffset></wp:positionH>");
            var path = Inject(outDir, "img-pct.docx", "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wp14\"><w:drawing>" + choice + "</w:drawing></mc:Choice><mc:Fallback><w:drawing>" + fallback + "</w:drawing></mc:Fallback></mc:AlternateContent></w:r></w:p>");
            var img = Document.Load(path).GetImagePlacements().Single().Image;
            AssertTrue(img.Position!.HorizontalOffsetPercent == 50 && img.RelativeWidth == new RelativeSize(40, RelativeSizeFrame.Margin), "percentages read");
            var outPath = Path.Combine(outDir, "img-pct-out.docx");
            Document.Load(path).SaveTo(outPath);
            var xml = ReadPartXml(outPath, "word/document.xml");
            AssertContains(xml, "<mc:Choice xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\" Requires=\"wp14\">", "AlternateContent written");
            AssertContains(xml, "<wp14:pctPosHOffset>50000</wp14:pctPosHOffset>", "percentage position");
            AssertContains(xml, "<wp14:pctWidth>40000</wp14:pctWidth>", "relative size");
            AssertTrue(Document.Load(outPath).GetImagePlacements().Count == 1, "one picture after round-trip");
        });
        Check("a text box holding a picture keeps its text (in the text box) and is not mistaken for a picture", () =>
        {
            const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
            var inner = "<w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"381000\" cy=\"285750\"/><wp:docPr id=\"9\" name=\"in\"/>" + PicGraphic("r:embed=\"rIdPic\"") + "</wp:inline></w:drawing>";
            var shape = "<a:graphic><a:graphicData uri=\"" + Wps + "\"><wps:wsp xmlns:wps=\"" + Wps + "\"><wps:txbx><w:txbxContent><w:p><w:r><w:t>Box text</w:t></w:r><w:r>" + inner + "</w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic>";
            var path = Inject(outDir, "img-textbox.docx", "<w:p><w:r><w:drawing>" + AnchorXml("<wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH>", graphic: shape, extent: "cx=\"2743200\" cy=\"914400\"") + "</w:drawing></w:r></w:p>");
            var p = (Paragraph)Document.Load(path).Blocks[0];
            // Since Phase 8 the text box is modelled: its paragraphs (with the picture inside) live in Run.TextBox.
            var box = p.Runs.Single(r => r.TextBox is not null).TextBox!;
            AssertTrue(box.GetText() == "Box text" && !p.Runs.Any(r => r.Image is not null), $"text '{box.GetText()}'");
            AssertTrue(box.Paragraphs[0].Runs.Any(r => r.Image is not null), "the picture stays inside the box");
        });
        Check("linked pictures, opaque media formats, behindDoc with square wrap, negative sizes and stray elements in posOffset", () =>
        {
            var body =
                "<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"1905000\" cy=\"1422400\"/><wp:docPr id=\"1\" name=\"l\"/>" + PicGraphic("r:link=\"rIdExt\"") + "</wp:inline></w:drawing></w:r></w:p>" +
                "<w:p><w:r><w:drawing>" + AnchorXml("<wp:positionH relativeFrom=\"column\"><wp:posOffset>12<w:x/></wp:posOffset></wp:positionH>", attrs: "behindDoc=\"1\"", wrap: "<wp:wrapSquare wrapText=\"largest\"/>", extent: "cx=\"-1905000\" cy=\"635000\"") + "</w:drawing></w:r></w:p>";
            var path = Inject(outDir, "img-misc.docx", body,
                "<Relationship Id=\"rIdExt\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"file:///C:/pics/photo.jpg\" TargetMode=\"External\"/>",
                media: Encoding.ASCII.GetBytes("not-an-image-format"), mediaName: "seal.xyz");
            var doc = Document.Load(path);
            var all = doc.GetImagePlacements();
            AssertTrue(all.Count == 2, $"count {all.Count}");
            AssertTrue(all[0].Image.LinkUrl == "file:///C:/pics/photo.jpg" && all[0].Image.Bytes.Length == 0 && all[0].Image.WidthPt == 150, "linked picture kept with its size");
            var b = all[1].Image;
            AssertTrue(b.ContentType == "application/octet-stream" && b.Bytes.Length > 0, "opaque media kept");
            AssertTrue(b.Position!.BehindText && b.Position.Wrap == TextWrap.Square && b.WidthPt == 0, "behind + square; negative width clamped");
            var outPath = Path.Combine(outDir, "img-misc-out.docx");
            doc.SaveTo(outPath);
            var xml = ReadPartXml(outPath, "word/document.xml");
            AssertContains(xml, "r:link=\"rIdImgLink1\"", "link written");
            AssertContains(ReadPartXml(outPath, "word/_rels/document.xml.rels"), "TargetMode=\"External\"", "external relationship");
            AssertContains(xml, "behindDoc=\"1\"", "behindDoc kept for square wrap");
            AssertTrue(Document.Load(outPath).GetImagePlacements().Count == 2, "both survive");
        });
        Check("a failing save leaves the existing file untouched; DocxReader.Images shares one array per media part and lists group pictures", () =>
        {
            var target = Path.Combine(outDir, "img-atomic.docx");
            var ok = new Document();
            ok.AddParagraph("original");
            ok.SaveTo(target);
            var before = File.ReadAllBytes(target);
            var bad = new Document();
            bad.AddParagraph().Image(ImageSpec.Inline(Png, "image/png", double.NaN, 10));
            AssertThrows<ArgumentOutOfRangeException>(() => bad.SaveTo(target), "NaN size");
            AssertTrue(File.ReadAllBytes(target).SequenceEqual(before), "original intact");
            AssertTrue(!Directory.GetFiles(outDir, ".img-atomic.docx.*").Any(), "no temp file left");

            var many = new StringBuilder();
            for (int i = 0; i < 50; i++) many.Append("<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"12700\" cy=\"12700\"/><wp:docPr id=\"" + (i + 1) + "\" name=\"m\"/>" + PicGraphic("r:embed=\"rIdPic\"") + "</wp:inline></w:drawing></w:r></w:p>");
            const string Wpg = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
            many.Append("<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"12700\" cy=\"12700\"/><wp:docPr id=\"99\" name=\"g\"/><a:graphic><a:graphicData uri=\"" + Wpg + "\"><wpg:wgp xmlns:wpg=\"" + Wpg + "\"><pic:pic><pic:blipFill><a:blip r:embed=\"rIdPic\"/></pic:blipFill></pic:pic></wpg:wgp></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
            var path = Inject(outDir, "img-many.docx", many.ToString());
            var doc = Document.Load(path);
            AssertTrue(doc.Images.Count == 51 && doc.Images.All(i => ReferenceEquals(i.Bytes, doc.Images[0].Bytes)), $"images {doc.Images.Count}, shared bytes");
            AssertTrue(doc.GetImagePlacements().Count == 50, "group is not an image run");
        });
    }
}
