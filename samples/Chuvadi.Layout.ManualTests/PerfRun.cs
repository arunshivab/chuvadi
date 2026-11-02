using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    /// <summary>
    /// Performance runs (--perf): time to open, to the first page, to the last page and to resolve the page count,
    /// all pages built, and memory, for large generated documents. Numbers are printed, not asserted (machines
    /// differ); the budgets in DESIGN.md section 13 are checked with a 3x margin.
    /// </summary>
    internal static class PerfRun
    {
        private sealed record Row(string Name, int Pages, double OpenMs, double FirstPageMs, double ResolveMs, double AllPagesMs, double PerPageMs, long AllocatedMb, double Budget);

        public static int Run(TextWriter output)
        {
            List<Row> rows = new List<Row>();
            // Warm-up (JIT, font parsing): one small document of each kind.
            Measure("warm-up text", () => DocumentLayout.OpenText(Lorem(2000), null, PreviewScenarios.Options()), 0);
            Measure("warm-up grid", () => DocumentLayout.Open(Grid(500, 8), PreviewScenarios.Options()), 0);

            rows.Add(Measure("text 1 MB (OpenText)", () => DocumentLayout.OpenText(Lorem(1_000_000), null, PreviewScenarios.Options()), 50));
            rows.Add(Measure("flow 3000 paragraphs", () => DocumentLayout.Open(Paragraphs(3000, false), PreviewScenarios.Options()), 50));
            rows.Add(Measure("flow 600 wrapped images", () => DocumentLayout.Open(Paragraphs(600, true), PreviewScenarios.Options()), 100));
            rows.Add(Measure("table 5000 rows x 5", () => DocumentLayout.Open(BigTable(5000), PreviewScenarios.Options()), 100));
            rows.Add(Measure("grid 100k cells", () => DocumentLayout.Open(Grid(20_000, 5), PreviewScenarios.Options()), 100));
            byte[] xlsx = Xlsx(20_000, 5);
            rows.Add(Measure("xlsx 100k cells (import)", () => DocumentLayout.OpenXlsx(new MemoryStream(xlsx), null, PreviewScenarios.Options()), 1500));

            output.WriteLine();
            output.WriteLine("Performance (ms; first page = open to page 1 drawn; per page = average over all pages built again)");
            output.WriteLine($"{"scenario",-28} {"pages",6} {"open",8} {"page 1",8} {"resolve",9} {"all",9} {"/page",7} {"alloc MB",9}");
            int over = 0;
            foreach (Row r in rows)
            {
                output.WriteLine($"{r.Name,-28} {r.Pages,6} {r.OpenMs,8:0.0} {r.FirstPageMs,8:0.0} {r.ResolveMs,9:0.0} {r.AllPagesMs,9:0.0} {r.PerPageMs,7:0.00} {r.AllocatedMb,9}");
                if (r.Budget > 0 && r.OpenMs + r.FirstPageMs > r.Budget * 3)
                {
                    output.WriteLine($"  OVER BUDGET: first page {r.OpenMs + r.FirstPageMs:0.0} ms > 3 x {r.Budget} ms");
                    over++;
                }
            }
            return over;
        }

        private static Row Measure(string name, Func<LaidOutDocument> open, double budgetMs)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long alloc0 = GC.GetTotalAllocatedBytes(true);
            Stopwatch sw = Stopwatch.StartNew();
            using LaidOutDocument doc = open();
            double openMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            doc.GetPage(0);
            double first = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            int pages = doc.ResolvePageCount();
            double resolve = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            long glyphs = 0;
            for (int i = 0; i < pages; i++)
            {
                glyphs += doc.GetPage(i).DisplayList.Count;
            }
            double all = sw.Elapsed.TotalMilliseconds;
            long allocated = (GC.GetTotalAllocatedBytes(true) - alloc0) / (1024 * 1024);
            return new Row(name, pages, openMs, first, resolve, all, pages > 0 ? all / pages : 0, allocated, budgetMs);
        }

        internal static string Lorem(int chars)
        {
            string para = "Chuvadi.Layout paginates documents into fixed pages with print-faithful line and page breaks, "
                + "so a viewer, a printer and a PDF export all show the same pages. Lines break by the Unicode rules; "
                + "fonts resolve to metric-compatible substitutes when the originals are missing.";
            StringBuilder sb = new StringBuilder(chars + para.Length);
            int n = 0;
            while (sb.Length < chars)
            {
                sb.Append(para).Append(' ').Append(n.ToString(CultureInfo.InvariantCulture));
                sb.Append(++n % 4 == 0 ? "\n\n" : " ");
            }
            return sb.ToString();
        }

        private static FlowDocument Paragraphs(int count, bool images)
        {
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            d.Sections.Add(s);
            string text = Lorem(600);
            LayoutImage img = PreviewScenarios.SamplePng();
            for (int i = 0; i < count; i++)
            {
                FlowParagraph p = FlowParagraph.Of(text, PreviewScenarios.Body, new ParagraphFormat { SpaceAfter = 8, Alignment = ParagraphAlignment.Justify });
                if (images)
                {
                    p.Inlines.Insert(0, new AnchoredImageInline(img, 90, 50, PreviewScenarios.Body)
                    {
                        Wrap = i % 3 == 2 ? FloatWrap.Tight : FloatWrap.Square,
                        HorizontalAlignment = (i % 3) switch { 0 => FloatAlignment.Start, 1 => FloatAlignment.End, _ => FloatAlignment.Center },
                    });
                }
                s.Blocks.Add(p);
            }
            return d;
        }

        private static FlowDocument BigTable(int rows)
        {
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            d.Sections.Add(s);
            BorderLine thin = new BorderLine(0.5, LayoutColor.Black);
            FlowTable t = new FlowTable { Format = new TableFormat { BorderTop = thin, BorderBottom = thin, BorderLeft = thin, BorderRight = thin, BorderInsideHorizontal = thin, BorderInsideVertical = thin } };
            foreach (double w in new[] { 50.0, 170, 70, 70, 90 })
            {
                t.ColumnWidths.Add(w);
            }
            FlowTableRow header = new FlowTableRow { IsHeader = true };
            foreach (string h in new[] { "No", "Description", "Qty", "Rate", "Amount" })
            {
                FlowTableCell c = new FlowTableCell();
                c.Blocks.Add(FlowParagraph.Of(h, PreviewScenarios.Body with { Bold = true }));
                header.Cells.Add(c);
            }
            t.Rows.Add(header);
            for (int r = 0; r < rows; r++)
            {
                FlowTableRow row = new FlowTableRow();
                foreach (string v in new[] { (r + 1).ToString(CultureInfo.InvariantCulture), r % 7 == 0 ? "A longer description that wraps onto a second line in its cell" : "Item " + r, (r % 9 + 1).ToString(CultureInfo.InvariantCulture), "125.00", ((r % 9 + 1) * 125).ToString("N2", CultureInfo.InvariantCulture) })
                {
                    FlowTableCell c = new FlowTableCell();
                    c.Blocks.Add(FlowParagraph.Of(v, PreviewScenarios.Body));
                    row.Cells.Add(c);
                }
                t.Rows.Add(row);
            }
            s.Blocks.Add(t);
            return d;
        }

        private static byte[] Xlsx(int rows, int cols)
        {
            Chuvadi.Sheets.Excel.Workbook wb = new Chuvadi.Sheets.Excel.Workbook();
            Chuvadi.Sheets.Excel.Sheet s = wb.AddSheet("Data");
            Chuvadi.Sheets.Excel.CellStyle num = new Chuvadi.Sheets.Excel.CellStyleBuilder().Format("#,##0.00").Build();
            for (int r = 1; r <= rows; r++)
            {
                for (int c = 1; c <= cols; c++)
                {
                    if (c == 1)
                    {
                        s.Cell(r, c).Value = "Row " + r.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        s.Cell(r, c).Value = (r * 31.5) + c;
                        s.Cell(r, c).Style = num;
                    }
                }
            }
            s.PageSetup.PrintGridlines = true;
            using MemoryStream ms = new MemoryStream();
            wb.SaveTo(ms);
            return ms.ToArray();
        }

        private static GridWorkbook Grid(int rows, int cols)
        {
            GridWorkbook book = new GridWorkbook();
            GridSheet s = new GridSheet("Data");
            book.Sheets.Add(s);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    s.Set(r, c, c == 0 ? "Row " + r.ToString(CultureInfo.InvariantCulture) : ((r * 31) + c).ToString(CultureInfo.InvariantCulture), c == 0 ? CellValueKind.Text : CellValueKind.Number);
                }
            }
            s.PageSetup = new GridPageSetup { PrintGridlines = true };
            return book;
        }
    }
}
