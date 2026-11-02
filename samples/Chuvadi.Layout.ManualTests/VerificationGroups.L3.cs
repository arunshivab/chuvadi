using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L3Groups()
        {
            yield return L3_ShowcasePaginatesWithMetricCompatibleFonts;
            yield return L3_TextImportPaginatesAndHonoursFormFeed;
            yield return L3_LazyFirstPageOfLargeDocument;
            yield return L3_PagesRebuildIdenticallyFromCheckpoints;
            yield return L3_UntrustedInputLimits;
        }

        private static void L3_ShowcasePaginatesWithMetricCompatibleFonts()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.FlowShowcase(), PreviewScenarios.Options());
            int n = doc.ResolvePageCount();
            Check(n >= 2, "showcase should span pages, got " + n);
            LaidOutPage p1 = doc.GetPage(0);
            Check(p1.GetText().StartsWith("Chuvadi.Layout flow showcase", StringComparison.Ordinal), "page 1 starts with the title");
            Check(p1.DisplayList.OfType<LinkOp>().Any(l => l.Uri == "https://example.org"), "hyperlink op present");
            Check(doc.FontSubstitutions.Any(s => s.RequestedFamily == "Cambria" && s.MetricCompatible), "Cambria substitution reported");
            foreach (LaidOutPage page in doc.GetPages())
            {
                foreach (LayoutBox line in page.Boxes.Where(b => b.Kind == LayoutBoxKind.Line))
                {
                    Check(line.Rect.Right <= page.WidthPt - 72 + 0.05 && line.Rect.Bottom <= page.HeightPt - 72 + 0.05, $"line outside the body on page {page.Index + 1}: {line.Rect}");
                }
            }
        }

        private static void L3_TextImportPaginatesAndHonoursFormFeed()
        {
            using LaidOutDocument doc = DocumentLayout.OpenText("first page\fsecond page", null, PreviewScenarios.Options());
            Check(doc.ResolvePageCount() == 2, "form feed must start a new page");
            Check(doc.GetPage(1).GetText() == "second page", "text after the form feed");
        }

        private static void L3_LazyFirstPageOfLargeDocument()
        {
            string para = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 12));
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            d.Sections.Add(s);
            for (int i = 0; i < 3000; i++)
            {
                s.Blocks.Add(FlowParagraph.Of(para, PreviewScenarios.Body, new ParagraphFormat { Alignment = ParagraphAlignment.Justify, SpaceAfter = 8 }));
            }
            using LaidOutDocument doc = DocumentLayout.Open(d, PreviewScenarios.Options());
            Stopwatch sw = Stopwatch.StartNew();
            doc.GetPage(0);
            double first = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            doc.GetPage(1);
            double second = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            int pages = doc.ResolvePageCount();
            double all = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"      perf: page 1 {first:0} ms, page 2 {second:0} ms, all {pages} pages {all:0} ms");
            Check(first < 1000 && second < 300, "Viewer targets: page 1 < 1 s, next page < 0.3 s");
        }

        private static void L3_PagesRebuildIdenticallyFromCheckpoints()
        {
            LayoutOptions o = PreviewScenarios.Options();
            o.PageCacheSize = 1;
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.FlowShowcase(), o);
            int n = doc.ResolvePageCount();
            string[] forward = Enumerable.Range(0, n).Select(i => doc.GetPage(i).GetText()).ToArray();
            for (int i = n - 1; i >= 0; i--)
            {
                Check(doc.GetPage(i).GetText() == forward[i], "page " + (i + 1) + " differs when rebuilt");
            }
        }

        private static void L3_UntrustedInputLimits()
        {
            LayoutOptions o = PreviewScenarios.Options();
            o.Limits.MaxPages = 2;
            using LaidOutDocument doc = DocumentLayout.OpenText(string.Join("\n", Enumerable.Range(0, 1000)), null, o);
            try
            {
                doc.ResolvePageCount();
                throw new InvalidOperationException("MaxPages was not enforced");
            }
            catch (LayoutLimitExceededException ex)
            {
                Check(ex.LimitName == "MaxPages", "wrong limit: " + ex.LimitName);
            }
        }
    }
}
