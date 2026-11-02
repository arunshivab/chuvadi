using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    /// <summary>
    /// Writes diagnostic SVG pages (glyphs drawn from their outlines) for visual verification. Each scenario
    /// writes &lt;name&gt;-p&lt;n&gt;.svg; "-boxes" variants overlay line (blue) and paragraph (green) boxes.
    /// </summary>
    internal static partial class PreviewScenarios
    {
        public static int WriteAll(string folder)
        {
            Directory.CreateDirectory(folder);
            int files = 0;
            foreach ((string name, Func<LaidOutDocument> open) in Scenarios())
            {
                using LaidOutDocument doc = open();
                int n = doc.ResolvePageCount();
                for (int i = 0; i < n; i++)
                {
                    LaidOutPage page = doc.GetPage(i);
                    File.WriteAllText(Path.Combine(folder, $"{name}-p{i + 1}.svg"), DiagnosticSvg.Render(page.DisplayList), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(folder, $"{name}-p{i + 1}-boxes.svg"), DiagnosticSvg.Render(page.DisplayList, true, page.Boxes), new UTF8Encoding(false));
                    files += 2;
                }
                StringBuilder report = new StringBuilder();
                report.AppendLine($"{name}: {n} page(s)");
                foreach (FontSubstitution s in doc.FontSubstitutions)
                {
                    report.AppendLine($"  font: {s.RequestedFamily} -> {s.UsedFace} ({s.Kind}, metric-compatible {s.MetricCompatible}, script {s.Script})");
                }
                foreach (LayoutWarning w in doc.Warnings)
                {
                    report.AppendLine("  warning: " + w);
                }
                File.WriteAllText(Path.Combine(folder, $"{name}-report.txt"), report.ToString(), new UTF8Encoding(false));
                files++;
            }
            return files;
        }

        internal static LayoutOptions Options()
        {
            LayoutOptions o = new LayoutOptions { Now = new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.FromHours(5.5)), DocumentName = "showcase.docx" };
            o.FontSources.Add(BundledFontSource.Instance);
            return o;
        }

        private static IEnumerable<(string, Func<LaidOutDocument>)> Scenarios()
        {
            yield return ("flow-showcase", () => DocumentLayout.Open(FlowShowcase(), Options()));
            yield return ("text-import", () => DocumentLayout.OpenText(TextSample(), null, Options()));
            foreach ((string, Func<LaidOutDocument>) s in LaterScenarios())
            {
                yield return s;
            }
        }

        private static partial IEnumerable<(string, Func<LaidOutDocument>)> LaterScenarios();

        internal static readonly CharacterFormat Body = new CharacterFormat { FontFamily = "Calibri", FontSize = 11 };

        internal static FlowDocument FlowShowcase()
        {
            CharacterFormat h1 = Body with { FontFamily = "Cambria", FontSize = 18, Bold = true, Color = new LayoutColor(0x1F, 0x3A, 0x60) };
            ParagraphFormat h1p = new ParagraphFormat { SpaceBefore = 12, SpaceAfter = 6, KeepWithNext = true, BorderBottom = new BorderLine(0.75, new LayoutColor(0x1F, 0x3A, 0x60), Space: 2) };
            ParagraphFormat bodyP = new ParagraphFormat { SpaceAfter = 8, LineSpacing = 1.08, Alignment = ParagraphAlignment.Justify };
            FlowDocument d = new FlowDocument { Title = "Chuvadi.Layout showcase" };
            FlowSection s = new FlowSection();
            d.Sections.Add(s);
            CharacterFormat small = Body with { FontSize = 9, Color = new LayoutColor(0x59, 0x59, 0x59) };
            FlowParagraph header = new FlowParagraph
            {
                Format = new ParagraphFormat { TabStops = new[] { new TabStop(451.3, TabAlignment.Right) }, BorderBottom = new BorderLine(0.5, new LayoutColor(0x99, 0x99, 0x99), Space: 1) },
                MarkFormat = small,
            };
            header.Inlines.Add(new TextRun("Chuvadi.Layout \u2014 design preview", small));
            header.Inlines.Add(new TabInline(small));
            header.Inlines.Add(new FieldInline(FieldKind.Date, small) { FormatString = "dd MMM yyyy" });
            FlowParagraph footer = new FlowParagraph { Format = new ParagraphFormat { Alignment = ParagraphAlignment.Center }, MarkFormat = small };
            footer.Inlines.Add(new TextRun("Page ", small));
            footer.Inlines.Add(new FieldInline(FieldKind.Page, small, "1"));
            footer.Inlines.Add(new TextRun(" of ", small));
            footer.Inlines.Add(new FieldInline(FieldKind.NumPages, small, "1"));
            s.HeadersFooters = new HeaderFooterSet
            {
                DefaultHeader = new List<FlowBlock> { header },
                DefaultFooter = new List<FlowBlock> { footer },
            };
            s.Blocks.Add(FlowParagraph.Of("Chuvadi.Layout flow showcase", h1, h1p));
            string lorem = "Chuvadi.Layout paginates flow documents into fixed pages with print-faithful line and page breaks. "
                + "Text is shaped, measured with the font's own advances, and broken at Unicode line-break opportunities; "
                + "justified lines are stretched at their spaces, and the last line of a paragraph stays ragged. ";
            s.Blocks.Add(FlowParagraph.Of(lorem + lorem, Body, bodyP));

            FlowParagraph styled = new FlowParagraph { Format = bodyP with { Alignment = ParagraphAlignment.Left }, MarkFormat = Body };
            styled.Inlines.Add(new TextRun("Runs can be ", Body));
            styled.Inlines.Add(new TextRun("bold", Body with { Bold = true }));
            styled.Inlines.Add(new TextRun(", ", Body));
            styled.Inlines.Add(new TextRun("italic", Body with { Italic = true }));
            styled.Inlines.Add(new TextRun(", ", Body));
            styled.Inlines.Add(new TextRun("underlined", Body with { Underline = UnderlineStyle.Single }));
            styled.Inlines.Add(new TextRun(", ", Body));
            styled.Inlines.Add(new TextRun("struck", Body with { Strikethrough = true }));
            styled.Inlines.Add(new TextRun(", ", Body));
            styled.Inlines.Add(new TextRun("highlighted", Body with { Background = new LayoutColor(0xFF, 0xF2, 0x00) }));
            styled.Inlines.Add(new TextRun(", coloured ", Body));
            styled.Inlines.Add(new TextRun("red", Body with { Color = new LayoutColor(0xC0, 0, 0) }));
            styled.Inlines.Add(new TextRun(", E = mc", Body));
            styled.Inlines.Add(new TextRun("2", Body with { VerticalAlignment = VerticalTextAlignment.Superscript }));
            styled.Inlines.Add(new TextRun(", H", Body));
            styled.Inlines.Add(new TextRun("2", Body with { VerticalAlignment = VerticalTextAlignment.Subscript }));
            styled.Inlines.Add(new TextRun("O, ", Body));
            styled.Inlines.Add(new TextRun("Small Caps", Body with { Capitals = TextCapitals.SmallCaps }));
            styled.Inlines.Add(new TextRun(", in Arial, ", Body));
            styled.Inlines.Add(new TextRun("Times New Roman", Body with { FontFamily = "Times New Roman" }));
            styled.Inlines.Add(new TextRun(" and ", Body));
            styled.Inlines.Add(new TextRun("Courier New", Body with { FontFamily = "Courier New" }));
            styled.Inlines.Add(new TextRun(", with a link to ", Body));
            styled.Inlines.Add(new TextRun("example.org", Body with { Color = new LayoutColor(0x05, 0x63, 0xC1), Underline = UnderlineStyle.Single }) { HyperlinkUri = "https://example.org" });
            styled.Inlines.Add(new TextRun(".", Body));
            s.Blocks.Add(styled);

            ParagraphFormat toc = new ParagraphFormat { TabStops = new[] { new TabStop(451, TabAlignment.Right, TabLeader.Dot) }, SpaceAfter = 2 };
            foreach ((string title, int page) in new[] { ("1  Introduction", 1), ("2  Fonts and metrics", 3), ("3  Line breaking", 7), ("4  Pagination", 12) })
            {
                FlowParagraph e = new FlowParagraph { Format = toc, MarkFormat = Body };
                e.Inlines.Add(new TextRun(title + "\t" + page.ToString(System.Globalization.CultureInfo.InvariantCulture), Body));
                s.Blocks.Add(e);
            }

            ParagraphFormat listP = new ParagraphFormat { LeftIndent = 36, FirstLineIndent = -18, SpaceAfter = 3 };
            string[] items = { "Bullets hang in the indent; text aligns at the left indent.", "Long items wrap and continue at the indent, not under the bullet, which is how Word draws hanging indents in lists.", "Third item." };
            foreach (string item in items)
            {
                FlowParagraph li = FlowParagraph.Of(item, Body, listP);
                li.ListLabel = new ListLabel("\u2022", Body with { FontFamily = "Arial" });
                s.Blocks.Add(li);
            }
            for (int i = 0; i < 3; i++)
            {
                FlowParagraph li = FlowParagraph.Of("Numbered item " + (i + 1) + ".", Body, listP);
                li.ListLabel = new ListLabel((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", Body);
                s.Blocks.Add(li);
            }

            s.Blocks.Add(FlowParagraph.Of("Centred line", Body with { Italic = true }, new ParagraphFormat { Alignment = ParagraphAlignment.Center, SpaceBefore = 6, SpaceAfter = 6 }));
            s.Blocks.Add(FlowParagraph.Of("Right-aligned line", Body, new ParagraphFormat { Alignment = ParagraphAlignment.Right, SpaceAfter = 6 }));
            s.Blocks.Add(FlowParagraph.Of("Shaded paragraph with a border all round.", Body,
                new ParagraphFormat { Background = new LayoutColor(0xEA, 0xF1, 0xFB), SpaceAfter = 10, SpaceBefore = 4,
                    BorderTop = new BorderLine(0.5, LayoutColor.Black, Space: 1), BorderBottom = new BorderLine(0.5, LayoutColor.Black, Space: 1),
                    BorderLeft = new BorderLine(0.5, LayoutColor.Black, Space: 4), BorderRight = new BorderLine(0.5, LayoutColor.Black, Space: 4) }));
            s.Blocks.Add(FlowParagraph.Of("Indic scripts (shaped by Chuvadi.Layout.Pdf; nominal glyphs here): \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD \u0939\u093F\u0928\u094D\u0926\u0940 \u0D2E\u0D32\u0D2F\u0D3E\u0D33\u0D02", Body, bodyP with { Alignment = ParagraphAlignment.Left }));

            s.Blocks.Add(FlowParagraph.Of("Pagination", h1, h1p));
            FlowParagraph withImage = FlowParagraph.Of("A floating image anchored to this paragraph with top-and-bottom wrapping: the text continues below it.", Body, bodyP with { Alignment = ParagraphAlignment.Left });
            withImage.Inlines.Insert(0, new AnchoredImageInline(SamplePng(), 160, 60, Body)
            {
                Wrap = FloatWrap.TopAndBottom,
                HorizontalAlignment = FloatAlignment.Center,
                DistanceBottom = 6,
            });
            s.Blocks.Add(withImage);
            for (int i = 1; i <= 14; i++)
            {
                s.Blocks.Add(FlowParagraph.Of($"Paragraph {i}. " + lorem, Body, bodyP));
            }
            return d;
        }

        /// <summary>A small generated PNG (a colour gradient) built with the BCL's zlib.</summary>
        internal static LayoutImage SamplePng()
        {
            const int W = 64, H = 24;
            byte[] raw = new byte[H * ((W * 3) + 1)];
            for (int y = 0; y < H; y++)
            {
                int row = y * ((W * 3) + 1);
                for (int x = 0; x < W; x++)
                {
                    raw[row + 1 + (x * 3)] = (byte)(0x1F + (x * 3));
                    raw[row + 2 + (x * 3)] = (byte)(0x3A + (y * 4));
                    raw[row + 3 + (x * 3)] = 0x90;
                }
            }
            using MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            byte[] ihdr = new byte[13];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, W);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), H);
            ihdr[8] = 8;
            ihdr[9] = 2;
            Chunk(png, "IHDR", ihdr);
            using (MemoryStream z = new MemoryStream())
            {
                using (System.IO.Compression.ZLibStream zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    zs.Write(raw);
                }
                Chunk(png, "IDAT", z.ToArray());
            }
            Chunk(png, "IEND", Array.Empty<byte>());
            return new LayoutImage(png.ToArray(), LayoutImageFormat.Png, W, H);
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            s.Write(len);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes);
            s.Write(data);
            uint crc = Crc32(typeBytes, data);
            byte[] c = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(c, crc);
            s.Write(c);
        }

        private static uint Crc32(byte[] a, byte[] b)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte[] part in new[] { a, b })
            {
                foreach (byte x in part)
                {
                    crc ^= x;
                    for (int k = 0; k < 8; k++)
                    {
                        crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                    }
                }
            }
            return ~crc;
        }

        private static string TextSample()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("CHUVADI.LAYOUT TEXT IMPORT\r\n\r\nName\tQty\tPrice\r\nApples\t12\t3.50\r\nPears\t7\t4.25\r\n\r\n");
            for (int i = 1; i <= 80; i++)
            {
                sb.Append("Line ").Append(i).Append(": the quick brown fox jumps over the lazy dog, and keeps running until it wraps onto the next line.\r\n");
            }
            sb.Append("\fThis starts on a new page after a form feed.\r\n");
            return sb.ToString();
        }
    }
}
