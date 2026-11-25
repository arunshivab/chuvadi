using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

/// <summary>
/// Docs 2.4.1 (D-406, D-410): every document Docs' default limits let load (content nesting up to
/// <see cref="DocxReaderOptions.MaxNestingDepth"/>, 64; 16 nested text boxes) converts to HTML, Markdown and text on a
/// 256 KB thread stack, the stack of an IIS worker thread. A stack overflow cannot be caught and ends the process.
/// </summary>
public static class DeepDocumentTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    public static void Run(string outDir)
    {
        var shapes = new (string Name, string Body)[]
        {
            ("64 nested tables", Tables(64, "<w:p><w:r><w:t>deep</w:t></w:r></w:p>")),
            ("64 nested hyperlinks", Nest(64, "<w:hyperlink w:anchor=\"a\">", "</w:hyperlink>")),
            ("64 nested insertions", Nest(64, "<w:ins w:id=\"1\" w:author=\"a\">", "</w:ins>")),
            ("32 nested tables with 32 nested hyperlinks inside", Tables(32, Nest(32, "<w:hyperlink w:anchor=\"a\">", "</w:hyperlink>"))),
            ("16 nested text boxes", TextBoxes(16)),
        };
        foreach (var (name, body) in shapes)
            Check($"256 KB thread stack: {name} load with Docs' defaults and export to HTML, HTML preview, Markdown and text", () =>
            {
                var bytes = Package(body);
                string? failure = null;
                var t = new Thread(() =>
                {
                    try
                    {
                        var d = Document.Load(new MemoryStream(bytes));
                        var html = HtmlConversion.Export(d);
                        HtmlConversion.ExportPreview(d);
                        var md = MarkdownConversion.Export(d);
                        var txt = TextConversion.Export(d);
                        if (!html.Contains("deep") || !md.Contains("deep") || !txt.Contains("deep")) failure = "text missing";
                    }
                    catch (Exception e) { failure = e.GetType().Name + ": " + e.Message; }
                }, 256 * 1024);
                t.Start();
                t.Join();
                AssertTrue(failure is null, failure ?? "");
            });
    }

    private static string Repeat(string s, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }

    private static string Nest(int n, string open, string close)
        => "<w:p>" + Repeat(open, n) + "<w:r><w:t>deep</w:t></w:r>" + Repeat(close, n) + "</w:p>";

    private static string Tables(int n, string inner)
        => Repeat("<w:tbl><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid><w:tr><w:tc>", n) + inner + Repeat("</w:tc></w:tr></w:tbl><w:p/>", n);

    private static string TextBoxes(int n)
    {
        const string open = "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"><wp:extent cx=\"635000\" cy=\"635000\"/><wp:docPr id=\"1\" name=\"Box\"/>"
            + "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"" + Wps + "\"><wps:wsp xmlns:wps=\"" + Wps + "\"><wps:cNvSpPr txBox=\"1\"/>"
            + "<wps:txbx><w:txbxContent>";
        const string close = "</w:txbxContent></wps:txbx><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
        return Repeat(open, n) + "<w:p><w:r><w:t>deep</w:t></w:r></w:p>" + Repeat(close, n);
    }

    private static byte[] Package(string body)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{W}\"><w:body>{body}</w:body></w:document>");
        }
        return ms.ToArray();
    }
}
