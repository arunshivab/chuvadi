using System.IO;
using Chuvadi.Docs.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        /// <summary>A two-section clinic letter written as .docx with Chuvadi.Docs, then laid out through the docx importer.</summary>
        internal static byte[] DocxShowcase()
        {
            Document doc = new Document();
            doc.Page.Size = PageSize.A4;
            doc.Header.Add(new Paragraph().Text("Example Medical Center", TextFormat.BoldText).Tab().Text("Radiation Oncology"));
            doc.Header[0].Configure(f => f.TabStops.Add(new TabStop(451, TabAlignment.Right)));
            doc.Footer.Add(new Paragraph().PageNumber(includeTotal: true));
            doc.Footer[0].Alignment = ParagraphAlignment.Center;
            doc.Section.Watermark = new TextWatermark("CONFIDENTIAL");

            doc.AddHeading("Treatment Summary", 1);
            doc.AddParagraph().Text("Patient: ").Text("A. Sample", TextFormat.BoldText).Text("    Date: 28 October 2026");
            doc.AddParagraph("The course of radiotherapy was completed as planned. The following items were reviewed with the patient and the referring physician:")
                .Configure(f => { f.SpaceAfterPt = 6; f.Alignment = ParagraphAlignment.Justify; });
            doc.AddBullet("Daily image guidance with cone-beam CT");
            doc.AddBullet("Weekly physician review and toxicity grading");
            doc.AddBullet("Dosimetric verification of the plan", 1);
            doc.AddNumbered("Follow-up in six weeks");
            doc.AddNumbered("Blood counts before the visit");
            doc.AddHeading("Dose Summary", 2);
            DocTable t = doc.AddTable(4);
            t.ColumnWidthsPt = new double[] { 150, 100, 100, 101 };
            t.HeaderRow("Site", "Dose (Gy)", "Fractions", "Technique").Shade("1F3A60");
            foreach (TableCell c in t.Rows[0].Cells)
            {
                foreach (Paragraph p in c.Paragraphs)
                {
                    p.Alignment = ParagraphAlignment.Center;
                }
            }
            t.AddRow("Prostate", "70.0", "28", "VMAT");
            t.AddRow("Pelvic nodes", "50.4", "28", "VMAT");
            TableRow total = t.AddRow("Total treatment time", "", "", "6 weeks");
            total.Cell(0).ColumnSpan = 3;
            doc.AddParagraph("Tamil: \u0B9A\u0BBF\u0B95\u0BBF\u0B9A\u0BCD\u0B9A\u0BC8 \u0BAE\u0BC1\u0B9F\u0BBF\u0BA8\u0BCD\u0BA4\u0BA4\u0BC1.")
                .Configure(f => f.SpaceBeforePt = 12);

            Section annex = doc.AddSectionBreak(SectionStart.NextPage);
            annex.Page.Orientation = PageOrientation.Landscape;
            annex.PageNumbering.Format = NumberFormat.LowerRoman;
            annex.PageNumbering.Start = 1;
            doc.AddHeading("Annex: Fraction Log", 1);
            DocTable log = doc.AddTable(6);
            log.HeaderRow("Fraction", "Date", "Site", "MU", "Imaging", "Signed");
            for (int i = 1; i <= 28; i++)
            {
                log.AddRow(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "2026-09-" + (i % 28 + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                    i % 2 == 0 ? "Prostate" : "Pelvic nodes", (480 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), i % 5 == 0 ? "CBCT" : "kV", "RTT");
            }
            using MemoryStream ms = new MemoryStream();
            doc.SaveTo(ms);
            return ms.ToArray();
        }

        internal static LayoutOptions DocxOptions()
        {
            LayoutOptions o = Options();
            o.DocumentName = "summary.docx";
            return o;
        }
    }
}
