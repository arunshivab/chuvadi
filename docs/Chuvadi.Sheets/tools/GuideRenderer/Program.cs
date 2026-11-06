using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Layout;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Markup;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Pdf;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

namespace Chuvadi.Sheets.GuideRenderer;

/// <summary>
/// Renders the Chuvadi.Sheets guide (docs/Chuvadi.Sheets/GUIDE.md) to PDF with Chuvadi's own tools.
///
///   dotnet run -c Release --project docs/Chuvadi.Sheets/tools/GuideRenderer -- [GUIDE.md] [out.pdf] [--png folder]
///
/// Defaults: docs/Chuvadi.Sheets/GUIDE.md in, docs/Chuvadi.Sheets/Chuvadi-Sheets-Guide-v2.0.pdf out, both found
/// from the current folder upwards. --png also writes every page as a 96 dpi PNG, for checking by eye.
/// Fonts: only the OFL fonts bundled in Chuvadi.Layout.Fonts and Chuvadi.Layout's own LiPi Sans (no installed
/// fonts), so the output is the same on every machine. Every layout or PDF warning is printed; any warning fails
/// the run, so a missing glyph or an unreadable image cannot slip into the published PDF.
/// </summary>
internal static class Program
{
    private const string Title = "Chuvadi.Sheets 2.0 — The Guide";
    private const string Author = "Dr. Arun Shiva B";

    private static int Main(string[] args)
    {
        string? input = null, output = null, pngDir = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--png" && i + 1 < args.Length) { pngDir = args[++i]; continue; }
            if (input is null) input = args[i];
            else if (output is null) output = args[i];
            else { Console.Error.WriteLine("Usage: GuideRenderer [GUIDE.md] [out.pdf] [--png folder]"); return 2; }
        }

        string docsDir = FindDocsDir();
        input ??= Path.Combine(docsDir, "GUIDE.md");
        output ??= Path.Combine(docsDir, "Chuvadi-Sheets-Guide-v2.0.pdf");
        if (!File.Exists(input)) { Console.Error.WriteLine($"Not found: {input}"); return 2; }

        var warnings = new List<string>();
        var options = new LayoutOptions
        {
            Shaper = new PdfShaper(),                       // complex scripts (the Tamil and Devanagari name on the title page)
            Culture = CultureInfo.InvariantCulture,
        };
        options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));

        var import = new MarkdownImportOptions
        {
            Title = Title,
            Page = new PageGeometry                         // A4; narrow side margins so code lines of about 100 characters fit
            {
                Width = 595.3, Height = 841.9,
                MarginTop = 56, MarginBottom = 56, MarginLeft = 50, MarginRight = 50,
                HeaderDistance = 30, FooterDistance = 30,
            },
            PageBreakBeforeHeading1 = true,
            PageNumbers = true,
            PageNumberText = "Chuvadi.Sheets 2.0 — The Guide   ·   {page} / {pages}",
            Styles = MarkdownStyleSheet.Default with
            {
                BodyFont = "Carlito",
                BodySize = 10.5,
                HeadingFont = "Carlito",
                HeadingSizes = new[] { 22.0, 15, 12.5, 11, 10.5, 10.5 },
                CodeFont = "Liberation Mono",
                CodeScale = 0.78,
            },
        };

        string source = File.ReadAllText(input, new UTF8Encoding(false));

        // Lay out, read each chapter's page from the laid-out text, write the page numbers into the Contents
        // table, and lay out again until the numbers are stable (the Contents page itself never moves pages).
        string markdown = source;
        LaidOutDocument doc = MarkdownLayout.Open(markdown, import, options);
        for (int pass = 0; pass < 3; pass++)
        {
            doc.ResolvePageCount();                     // final page count first, so "{pages}" is exact on every page
            string numbered = WithContentsPages(source, doc);
            if (numbered == markdown) break;
            doc.Dispose();
            markdown = numbered;
            doc = MarkdownLayout.Open(markdown, import, options);
        }
        using var finalDoc = doc;

        var pdfOptions = new LayoutPdfOptions
        {
            Title = Title,
            Author = Author,
            Subject = "Chuvadi.Sheets 2.0.2: writing, reading, styling, protecting and encrypting xlsx files in pure .NET",
            Warning = w => warnings.Add($"pdf: {w}"),
        };

        var raw = new MemoryStream();
        doc.ToPdf(raw, pdfOptions);

        // Chuvadi.Layout.Pdf writes page content uncompressed; Flate-compress it (lossless) with Chuvadi.Pdf.
        var pdfBytes = new MemoryStream();
        using (var written = PdfDocument.Open(new MemoryStream(raw.ToArray(), writable: false)))
        {
            var result = PdfCompressor.Compress(written, pdfBytes, new CompressionOptions { MaxDeflateStreams = true });
            if (result.SkipReason != CompressionSkipReason.None) warnings.Add($"pdf: compression skipped ({result.SkipReason})");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllBytes(output, pdfBytes.ToArray());

        if (pngDir is not null)
        {
            Directory.CreateDirectory(pngDir);
            for (int p = 0; p < doc.PageCount; p++)
                File.WriteAllBytes(Path.Combine(pngDir, $"page-{p + 1:000}.png"), doc.GetPage(p).ToPng(96, pdfOptions));
        }

        foreach (var w in doc.Warnings) warnings.Add($"layout: {w}");   // after rendering: pages are laid out lazily
        Console.WriteLine($"{input} -> {output}");
        Console.WriteLine($"  {doc.PageCount} pages, {pdfBytes.Length:N0} bytes ({raw.Length:N0} before compression)");
        foreach (var w in warnings) Console.WriteLine("  WARNING " + w);
        if (warnings.Count > 0)
        {
            Console.WriteLine($"  {warnings.Count} warning(s): fix the guide before publishing the PDF.");
            return 1;
        }
        Console.WriteLine("  no warnings");
        return 0;
    }

    /// <summary>
    /// The guide with "(page)" after each chapter in the Contents section: "# N Title" headings are found as a
    /// line of their own in the laid-out page text, after the Contents page.
    /// </summary>
    private static string WithContentsPages(string markdown, LaidOutDocument doc)
    {
        var chapters = new List<string>();
        foreach (var line in markdown.Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line.TrimEnd('\r'), @"^# (\d+ .+)$");
            if (m.Success) chapters.Add(m.Groups[1].Value.Trim());
        }
        var pageOf = new Dictionary<string, int>();
        int contentsPage = -1;
        for (int p = 0; p < doc.PageCount; p++)
        {
            foreach (var raw in doc.GetPage(p).GetText().Split('\n'))
            {
                string line = raw.Trim();
                if (contentsPage < 0 && line == "Contents") contentsPage = p;
                else if (contentsPage >= 0 && p > contentsPage && chapters.Contains(line) && !pageOf.ContainsKey(line))
                    pageOf[line] = p + 1;
            }
        }
        int start = markdown.IndexOf("\n# Contents", StringComparison.Ordinal);
        if (start < 0) return markdown;
        int end = markdown.IndexOf("\n# ", start + 1, StringComparison.Ordinal);
        if (end < 0) end = markdown.Length;
        string section = markdown.Substring(start, end - start);
        foreach (var title in chapters.OrderByDescending(t => t.Length))
            if (pageOf.TryGetValue(title, out int page))
                section = System.Text.RegularExpressions.Regex.Replace(section,
                    @"(?<=^|[|·] )" + System.Text.RegularExpressions.Regex.Escape(title) + @"(?= [·|])",
                    $"{title} ({page})", System.Text.RegularExpressions.RegexOptions.Multiline);
        return markdown.Substring(0, start) + section + markdown.Substring(end);
    }

    /// <summary>docs/Chuvadi.Sheets, found from the current folder upwards.</summary>
    private static string FindDocsDir()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "Chuvadi.Sheets");
            if (Directory.Exists(candidate)) return candidate;
            if (dir.Name == "Chuvadi.Sheets" && dir.Parent?.Name == "docs") return dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
