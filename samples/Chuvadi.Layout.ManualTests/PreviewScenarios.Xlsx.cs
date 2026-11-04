using System.Globalization;
using System.IO;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        /// <summary>A charge schedule written as .xlsx with Chuvadi.Sheets, then laid out through the xlsx importer.</summary>
        internal static byte[] XlsxShowcase()
        {
            Workbook wb = new Workbook();
            Sheet s = wb.AddSheet("Charges");
            CellStyle title = new CellStyleBuilder().Font("Cambria", 16).Bold().Foreground("1F3A60").HAlign(HorizontalAlign.Center).Build();
            CellStyle head = new CellStyleBuilder().Bold().Foreground("FFFFFF").Background("1F3A60").BorderAll(BorderStyle.Thin, "404040").HAlign(HorizontalAlign.Center).WrapText().Build();
            CellStyle text = new CellStyleBuilder().BorderAll(BorderStyle.Thin, "404040").WrapText().VAlign(VerticalAlign.Top).Build();
            CellStyle money = new CellStyleBuilder().BorderAll(BorderStyle.Thin, "404040").Format("#,##0.00;[Red](#,##0.00)").Build();
            s.Cell("A1").Value = "Schedule of Charges \u2014 Radiation Oncology";
            s.Cell("A1").Style = title;
            s.MergeCells("A1:E1");
            s.Rows[1].Height = 28;
            string[] heads = { "Code", "Service", "Unit", "Rate", "Change" };
            for (int c = 0; c < heads.Length; c++)
            {
                s.Cell(3, c + 1).Value = heads[c];
                s.Cell(3, c + 1).Style = head;
            }
            string[] services =
            {
                "Consultation", "CT simulation", "IMRT planning with dosimetric verification and patient-specific QA", "IMRT fraction", "VMAT fraction",
                "SRS single fraction", "Brachytherapy HDR session", "Weekly port film", "Mould room immobilisation (thermoplastic mask)", "Follow-up visit",
            };
            for (int i = 0; i < 70; i++)
            {
                int r = 4 + i;
                s.Cell(r, 1).Value = "RT-" + (101 + i).ToString(CultureInfo.InvariantCulture);
                s.Cell(r, 1).Style = text;
                s.Cell(r, 2).Value = services[i % services.Length];
                s.Cell(r, 2).Style = text;
                s.Cell(r, 3).Value = i % 3 == 0 ? "per visit" : "each";
                s.Cell(r, 3).Style = text;
                s.Cell(r, 4).Value = 1500 + (i * 875.5);
                s.Cell(r, 4).Style = money;
                s.Cell(r, 5).Value = ((i * 37) % 21) - 10.0;
                s.Cell(r, 5).Style = money;
            }
            s.Columns[1].Width = 10;
            s.Columns[2].Width = 42;
            s.Columns[3].Width = 11;
            s.Columns[4].Width = 14;
            s.Columns[5].Width = 12;
            s.AddConditionalFormat("D4:D73", ConditionalFormat.DataBar.Green);
            s.AddConditionalFormat("E4:E73", ConditionalFormat.CellIs(ComparisonOp.LessThan, 0, new CellStyleBuilder().Background("FFC7CE").Foreground("9C0006").Build()));
            s.PageSetup.PaperSizeCode = 9;
            s.PageSetup.PrintTitleRows = (3, 3);
            s.PageSetup.CenterHorizontally = true;
            s.PageSetup.HeaderFooter.OddHeader = "&L&\"Calibri,Bold\"Example Medical Center&R&A";
            s.PageSetup.HeaderFooter.OddFooter = "&LPrinted &D&CPage &P of &N&R&F";
            using MemoryStream ms = new MemoryStream();
            wb.SaveTo(ms);
            return ms.ToArray();
        }
    }
}
