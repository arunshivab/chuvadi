using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Pdf.Authoring;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>
    /// Checks the harness itself without a real corpus: lays out a text sample, writes "reference" PDFs that draw
    /// each laid-out line with chuvadi-pdf's DrawText (so the PDF has a ToUnicode map, as Word's PDFs do), then
    /// runs the comparison against a faithful reference and against references with one change each.
    /// </summary>
    internal static class SelfTest
    {
        private sealed record Case(string Name, Func<List<PageText>, List<PageText>> Alter, bool PagesEqual, bool TextEqual, bool GeometryPass);

        public static int Run(string folder, TextWriter output)
        {
            string corpus = Path.Combine(folder, "self-test-corpus");
            Directory.CreateDirectory(corpus);
            string text = SampleText();
            RunSettings settings = RunSettings.Default with { Tolerance = 0.5, SubstituteTolerance = 0.5 };
            using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Runner.Options(settings));
            DocumentText laid = LayoutText.Read(doc);
            FontFace face = doc.GetPage(0).DisplayList.OfType<GlyphRunOp>().First().Font;

            Case[] cases =
            {
                new Case("faithful", p => p, true, true, true),
                new Case("line-moved", p => MoveLine(p, 0, 4, 3.0), true, true, false),
                new Case("word-changed", p => ChangeWord(p, 1), true, false, false),
                new Case("page-missing", p => p.Take(p.Count - 1).ToList(), false, false, false),
                new Case("line-to-next-page", p => MoveLastLineDown(p, 0), true, false, false),
            };
            List<EntryResult> results = new List<EntryResult>();
            int failures = 0;
            foreach (Case c in cases)
            {
                string source = Path.Combine(corpus, c.Name + ".txt");
                string pdf = Path.Combine(corpus, c.Name + ".pdf");
                File.WriteAllText(source, text, new UTF8Encoding(false));
                File.WriteAllBytes(pdf, ReferenceFrom(c.Alter(laid.Pages.ToList()), face));
                EntryResult r = Runner.Run(new CorpusEntry(source, pdf, "self-test (generated)", string.Empty), settings);
                results.Add(r);
                FileComparison? cmp = r.Comparison;
                bool ok = cmp is not null && cmp.PageCountEqual == c.PagesEqual && cmp.TextEqual == c.TextEqual && cmp.GeometryPass == c.GeometryPass;
                if (c.Name == "faithful" && cmp is not null)
                {
                    // The reader must recover what was drawn: positions to PDF number precision.
                    ok &= cmp.MaxBaselineDelta < 0.05 && cmp.MaxLeftDelta < 0.05;
                }
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{(ok ? "PASS" : "FAIL")}  self-test {c.Name}: pages {cmp?.ReferencePages}/{cmp?.LayoutPages}, tier A {(cmp?.TextEqual == true ? "pass" : "fail")}, geometry {(cmp?.GeometryPass == true ? "pass" : "fail")}, max dy {cmp?.MaxBaselineDelta:0.###} dx {cmp?.MaxLeftDelta:0.###} ({r.Message})"));
                if (!ok)
                {
                    failures++;
                }
            }
            Report.Write(folder, results, settings);
            output.WriteLine("Self-test report: " + Path.GetFullPath(Path.Combine(folder, "acceptance-report.md")));
            return failures;
        }

        internal static string SampleText()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i <= 90; i++)
            {
                sb.Append("Line ").Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append(": the acceptance harness compares page count, page breaks, text and line positions with a reference PDF.");
                sb.Append(i % 7 == 0 ? "\n\n" : "\n");
            }
            return sb.ToString();
        }

        /// <summary>A PDF that draws each line's text at its left edge and baseline with DrawText.</summary>
        internal static byte[] ReferenceFrom(List<PageText> pages, FontFace face)
        {
            PdfDocumentBuilder b = PdfDocumentBuilder.Create();
            b.AddTrueTypeFont("Ref", face.Data.ToArray());
            foreach (PageText p in pages)
            {
                PageBuilder pb = b.AddPage(new PageSize(p.Width, p.Height));
                foreach (TextLine l in p.Lines)
                {
                    // DrawText puts the baseline at y + size.
                    pb.DrawText(l.Text, l.Left, l.Baseline - l.Size, "Ref", l.Size, Color.FromBytes(0, 0, 0));
                }
            }
            using MemoryStream ms = new MemoryStream();
            b.Save(ms);
            return ms.ToArray();
        }

        private static List<PageText> MoveLine(List<PageText> pages, int page, int line, double dy)
        {
            List<PageText> copy = pages.ToList();
            List<TextLine> lines = copy[page].Lines.ToList();
            lines[line] = lines[line] with { Baseline = lines[line].Baseline + dy };
            copy[page] = copy[page] with { Lines = lines };
            return copy;
        }

        private static List<PageText> ChangeWord(List<PageText> pages, int page)
        {
            List<PageText> copy = pages.ToList();
            List<TextLine> lines = copy[page].Lines.ToList();
            int k = lines.FindIndex(l => l.Text.Contains("harness", StringComparison.Ordinal));
            lines[k] = new TextLine(lines[k].Baseline, lines[k].Left, lines[k].Right, lines[k].Size, lines[k].Text.Replace("harness", "harnesses", StringComparison.Ordinal));
            copy[page] = copy[page] with { Lines = lines };
            return copy;
        }

        /// <summary>A page break one line earlier: the last line of the page moves to the top of the next page.</summary>
        private static List<PageText> MoveLastLineDown(List<PageText> pages, int page)
        {
            List<PageText> copy = pages.ToList();
            List<TextLine> a = copy[page].Lines.ToList();
            List<TextLine> b = copy[page + 1].Lines.ToList();
            TextLine last = a[^1];
            a.RemoveAt(a.Count - 1);
            double step = b.Count > 1 ? b[1].Baseline - b[0].Baseline : last.Size * 1.2;
            List<TextLine> moved = new List<TextLine> { new TextLine(b[0].Baseline, last.Left, last.Right, last.Size, last.Text) };
            moved.AddRange(b.Select(l => new TextLine(l.Baseline + step, l.Left, l.Right, l.Size, l.Text)));
            copy[page] = copy[page] with { Lines = a };
            copy[page + 1] = copy[page + 1] with { Lines = moved };
            return copy;
        }
    }
}
