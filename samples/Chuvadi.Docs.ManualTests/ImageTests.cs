using System;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Image read/write/template coverage: inline + floating insertion, auto-sizing from PNG/JPEG,
/// reading bytes/size/placement/table-location back, SaveImages, Document.Load Images, and the
/// two template image mechanisms (text-to-image and replace-by-alt-text).
/// </summary>
public static class ImageTests
{
    // 80x40 PNG @96dpi -> auto 60x30pt; 120x60 PNG @120dpi -> auto 72x36pt; 100x50 JPEG @72dpi -> 100x50pt.
    private const string Png80x40_B64 = "iVBORw0KGgoAAAANSUhEUgAAAFAAAAAoCAIAAADmAupWAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAAQ0lEQVR42u3PMQ0AAAgDsMmZfz2IwQUPTWqgmfaVCAsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsLCwsL31qSIqbTW6qd7wAAAABJRU5ErkJggg==";
    private const string Png120x60_B64 = "iVBORw0KGgoAAAANSUhEUgAAAHgAAAA8CAIAAAAiz+n/AAAACXBIWXMAABJ0AAASdAHeZh94AAAAaklEQVR42u3QQQ0AAAgEoItjHGMbyxbOBxsJSPVwIApEi0a0aNEWRItGtGjRFkSLRrRo0YgWjWjRohEtGtGiRSNaNKJFi0a0aESLFo1o0YgWLRrRohEtWjSiRSNatGhEi0a0aNGIFo3oXxYZ0CoOCza1ZgAAAABJRU5ErkJggg==";
    private const string Jpeg100x50_B64 = "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAUDBAQEAwUEBAQFBQUGBwwIBwcHBw8LCwkMEQ8SEhEPERETFhwXExQaFRERGCEYGh0dHx8fExciJCIeJBweHx7/2wBDAQUFBQcGBw4ICA4eFBEUHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh4eHh7/wAARCAAyAGQDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDHoooryj8hCiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKAP//Z";

    private static byte[] Png80x40 => Convert.FromBase64String(Png80x40_B64);
    private static byte[] Png120x60 => Convert.FromBase64String(Png120x60_B64);
    private static byte[] Jpeg100x50 => Convert.FromBase64String(Jpeg100x50_B64);

    /// <summary>80x40 PNG @96dpi (auto-sizes to 60x30pt). Shared with the xUnit suite.</summary>
    public static byte[] SamplePng80x40() => Convert.FromBase64String(Png80x40_B64);
    /// <summary>120x60 PNG @120dpi (auto-sizes to 72x36pt).</summary>
    public static byte[] SamplePng120x60() => Convert.FromBase64String(Png120x60_B64);
    /// <summary>100x50 JPEG @72dpi (auto-sizes to 100x50pt).</summary>
    public static byte[] SampleJpeg100x50() => Convert.FromBase64String(Jpeg100x50_B64);

    public static void Run(string outDir)
    {
        InlineAndCellImages(outDir);
        FloatingImage(outDir);
        AutoSizeFromBytes(outDir);
        ReadBackAndSave(outDir);
        DocumentLoadImages(outDir);
        TemplateTextToImage(outDir);
        TemplateReplaceByAltText(outDir);
        EncryptedRoundTripWithImages(outDir);
    }

    private static void InlineAndCellImages(string outDir)
    {
        var path = Path.Combine(outDir, "img-inline.docx");
        var doc = new Document();
        doc.AddHeading("Inline images", 1);
        doc.AddImage(ImageSpec.Inline(Png80x40, "image/png", 60, 30));

        var p = new Paragraph();
        p.Text("Before ").Image(ImageSpec.Inline(Jpeg100x50, "image/jpeg", 50, 25)).Text(" after.");
        doc.AddBlock(p);

        var t = doc.AddTable(2);
        var row = t.AddRow();
        row.Cell(0).SetText("Logo:");
        row.Cell(1).SetImage(ImageSpec.Inline(Png80x40, "image/png", 40, 20));
        doc.SaveTo(path);

        Check("inline: document.xml has drawing", () =>
        {
            var xml = ReadPartXml(path, "word/document.xml");
            AssertContains(xml, "<wp:inline", "inline drawing present");
            AssertContains(xml, "a:blip", "blip present");
        });
        Check("inline: media parts written", () =>
        {
            var parts = ListParts(path);
            AssertTrue(parts.Any(p => p.StartsWith("word/media/image") && p.EndsWith(".png")), "png media part");
            AssertTrue(parts.Any(p => p.StartsWith("word/media/image") && p.EndsWith(".jpeg")), "jpeg media part");
        });
        Check("inline: image relationships present", () =>
        {
            var rels = ReadPartXml(path, "word/_rels/document.xml.rels");
            AssertContains(rels, "/image", "image relationship type");
        });
    }

    private static void FloatingImage(string outDir)
    {
        var path = Path.Combine(outDir, "img-floating.docx");
        var doc = new Document();
        doc.AddParagraph("A floating watermark sits behind this text.");
        var pos = new FloatingPosition
        {
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HAlign = HorizontalAlignment.Center,
            VAlign = VerticalAlignment.Center,
            Wrap = TextWrap.None,
            BehindText = true,
        };
        doc.AddImage(ImageSpec.Float(Png120x60, "image/png", 200, 100, pos));

        var p2 = new Paragraph().Text("Offset-anchored logo here.");
        var pos2 = new FloatingPosition
        {
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            HorizontalOffsetPt = 36,
            VerticalOffsetPt = 12,
            Wrap = TextWrap.Square,
        };
        p2.Image(ImageSpec.Float(Png80x40, "image/png", 60, 30, pos2));
        doc.AddBlock(p2);
        doc.SaveTo(path);

        Check("floating: anchor + behindDoc emitted", () =>
        {
            var xml = ReadPartXml(path, "word/document.xml");
            AssertContains(xml, "<wp:anchor", "anchor present");
            AssertContains(xml, "behindDoc=\"1\"", "behindDoc for watermark");
            AssertContains(xml, "<wp:align>center</wp:align>", "center align");
            AssertContains(xml, "relativeFrom=\"page\"", "page anchor");
        });
        Check("floating: offset + wrapSquare emitted", () =>
        {
            var xml = ReadPartXml(path, "word/document.xml");
            AssertContains(xml, "<wp:posOffset>", "posOffset present");
            AssertContains(xml, "<wp:wrapSquare", "wrapSquare present");
        });
    }

    private static void AutoSizeFromBytes(string outDir)
    {
        Check("autosize: PNG 80x40@96 -> 60x30pt", () =>
        {
            var spec = ImageSpec.FromBytes(Png80x40);
            AssertTrue(Math.Abs(spec.WidthPt - 60) < 0.5, $"width {spec.WidthPt}");
            AssertTrue(Math.Abs(spec.HeightPt - 30) < 0.5, $"height {spec.HeightPt}");
            AssertTrue(spec.ContentType == "image/png", "content type png");
        });
        Check("autosize: JPEG 100x50@72 -> 100x50pt", () =>
        {
            var spec = ImageSpec.FromBytes(Jpeg100x50);
            AssertTrue(Math.Abs(spec.WidthPt - 100) < 0.5, $"width {spec.WidthPt}");
            AssertTrue(Math.Abs(spec.HeightPt - 50) < 0.5, $"height {spec.HeightPt}");
        });
        Check("autosize: ScaleToWidth keeps aspect", () =>
        {
            var spec = ImageSpec.FromBytes(Png120x60).ScaleToWidth(36);
            AssertTrue(Math.Abs(spec.WidthPt - 36) < 0.01, "scaled width");
            AssertTrue(Math.Abs(spec.HeightPt - 18) < 0.01, $"scaled height {spec.HeightPt}");
        });
    }

    private static void ReadBackAndSave(string outDir)
    {
        var path = Path.Combine(outDir, "img-readback.docx");
        var doc = new Document();
        doc.AddImage(ImageSpec.Inline(Png80x40, "image/png", 60, 30));
        var t = doc.AddTable(2);
        t.AddRow("A", "B");                 // row 0
        var r1 = t.AddRow();                // row 1
        r1.Cell(1).SetImage(ImageSpec.Inline(Jpeg100x50, "image/jpeg", 50, 25));
        doc.SaveTo(path);

        Check("read: Images() returns both with bytes + size", () =>
        {
            using var reader = DocxReader.Open(path);
            var imgs = reader.Images();
            AssertTrue(imgs.Count == 2, $"expected 2 images, got {imgs.Count}");
            var png = imgs.First(i => i.ContentType == "image/png");
            AssertTrue(png.Bytes.Length == Png80x40.Length, "png bytes round-trip");
            AssertTrue(Math.Abs(png.WidthPt - 60) < 0.5, $"png width {png.WidthPt}");
        });
        Check("read: table image carries row/column", () =>
        {
            using var reader = DocxReader.Open(path);
            var inCell = reader.Images().First(i => i.ContentType == "image/jpeg");
            AssertTrue(inCell.TableIndex == 0, $"table index {inCell.TableIndex}");
            AssertTrue(inCell.TableRow == 1, $"row {inCell.TableRow}");
            AssertTrue(inCell.TableColumn == 1, $"col {inCell.TableColumn}");
        });
        Check("read: SaveImages writes files", () =>
        {
            var folder = Path.Combine(outDir, "extracted");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            using var reader = DocxReader.Open(path);
            var written = reader.SaveImages(folder);
            AssertTrue(written.Count == 2, "two files written");
            foreach (var f in written) AssertTrue(new FileInfo(f).Length > 0, "non-empty file");
        });
    }

    private static void DocumentLoadImages(string outDir)
    {
        var path = Path.Combine(outDir, "img-load.docx");
        var doc = new Document();
        doc.AddImage(ImageSpec.Inline(Png120x60, "image/png", 72, 36));
        doc.SaveTo(path);

        Check("load: Document.Load populates Images", () =>
        {
            var loaded = Document.Load(path);
            AssertTrue(loaded.Images.Count == 1, $"images {loaded.Images.Count}");
            AssertTrue(loaded.Images[0].Bytes.Length == Png120x60.Length, "bytes match");
        });
    }

    private static void TemplateTextToImage(string outDir)
    {
        var tpl = Path.Combine(outDir, "img-tpl-src.docx");
        var doc = new Document();
        doc.AddParagraph("Company: {{Logo}}");
        doc.AddParagraph("Signature below:");
        doc.AddParagraph("{{Sign}}");
        doc.SaveTo(tpl);

        var outPath = Path.Combine(outDir, "img-tpl-out.docx");
        DocxTemplate.Fill(tpl, outPath,
            textValues: new System.Collections.Generic.Dictionary<string, string>(),
            imageValues: new System.Collections.Generic.Dictionary<string, ImageSpec>
            {
                ["Logo"] = ImageSpec.Inline(Png80x40, "image/png", 60, 30),
                ["Sign"] = ImageSpec.Inline(Jpeg100x50, "image/jpeg", 80, 40),
            });

        Check("template text-to-image: placeholders became drawings", () =>
        {
            var xml = ReadPartXml(outPath, "word/document.xml");
            AssertContains(xml, "<wp:inline", "inline drawing inserted");
            AssertTrue(!xml.Contains("{{Logo}}") && !xml.Contains("{{Sign}}"), "placeholders consumed");
        });
        Check("template text-to-image: media + content types added", () =>
        {
            var parts = ListParts(outPath);
            AssertTrue(parts.Any(p => p.StartsWith("word/media/image")), "media added");
            var ct = ReadPartXml(outPath, "[Content_Types].xml");
            AssertTrue(ct.Contains("image/png") && ct.Contains("image/jpeg"), "content types declared");
        });
        Check("template text-to-image: reader sees two images", () =>
        {
            using var reader = DocxReader.Open(outPath);
            AssertTrue(reader.Images().Count == 2, "two images read back");
        });
        Check("template text-to-image: surrounding text preserved", () =>
        {
            using var reader = DocxReader.Open(outPath);
            var text = reader.ExtractText();
            AssertContains(text, "Company:", "leading text kept");
        });
    }

    private static void TemplateReplaceByAltText(string outDir)
    {
        // Build a template that already has an image with alt text "Brand".
        var tpl = Path.Combine(outDir, "img-replace-src.docx");
        var doc = new Document();
        doc.AddImage(new ImageSpec { Bytes = Png80x40, ContentType = "image/png", WidthPt = 60, HeightPt = 30, AltText = "Brand" });
        doc.SaveTo(tpl);

        var outPath = Path.Combine(outDir, "img-replace-out.docx");
        DocxTemplate.Fill(tpl, outPath,
            textValues: new System.Collections.Generic.Dictionary<string, string>(),
            imageValues: new System.Collections.Generic.Dictionary<string, ImageSpec>
            {
                ["Brand"] = ImageSpec.FromBytes(Png120x60), // swap bytes, keep size/pos
            });

        Check("template replace-by-alt-text: bytes swapped", () =>
        {
            using var reader = DocxReader.Open(outPath);
            var img = reader.Images().Single();
            AssertTrue(img.Bytes.Length == Png120x60.Length, "new bytes in place");
            // Size stays as the template defined it (60x30), not the new image's intrinsic size.
            AssertTrue(Math.Abs(img.WidthPt - 60) < 0.5, $"size preserved {img.WidthPt}");
        });
    }

    private static void EncryptedRoundTripWithImages(string outDir)
    {
        var path = Path.Combine(outDir, "img-enc.docx");
        var doc = new Document();
        doc.AddImage(ImageSpec.Inline(Png80x40, "image/png", 60, 30));
        doc.SaveTo(path, new EncryptionOptions { Password = "p@ss" });

        Check("encrypted: wrong password rejected", () =>
            AssertThrows<DocxPasswordRequiredException>(
                () => Document.Load(path, "nope"), "wrong password"));
        Check("encrypted: image survives decrypt round-trip", () =>
        {
            var loaded = Document.Load(path, "p@ss");
            AssertTrue(loaded.Images.Count == 1, "image present after decrypt");
            AssertTrue(loaded.Images[0].Bytes.Length == Png80x40.Length, "bytes intact");
        });
    }
}
