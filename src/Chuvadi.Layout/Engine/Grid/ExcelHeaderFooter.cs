using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine.Flow;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Grid
{
    /// <summary>Values substituted for Excel header/footer codes.</summary>
    internal readonly record struct HeaderFooterValues(int Page, int Pages, string SheetName, string FileName, string FilePath, DateTimeOffset Now, CultureInfo Culture);

    /// <summary>
    /// Parses Excel header/footer strings (ECMA-376 Part 1, 18.3.1.46 evenFooter and siblings) into three
    /// sections of formatted runs: &amp;L &amp;C &amp;R; &amp;P (&amp;P+n, &amp;P-n) &amp;N &amp;D &amp;T &amp;F &amp;Z &amp;A;
    /// &amp;"font,style" &amp;nn &amp;B &amp;I &amp;U &amp;E &amp;S &amp;X &amp;Y &amp;K; &amp;&amp;; &amp;G marks where a picture goes (<see cref="PictureSlot"/>).
    /// </summary>
    internal static class ExcelHeaderFooter
    {
        /// <summary>Where an &amp;G picture goes in a section.</summary>
        public sealed class PictureSlot : FlowInline
        {
            public PictureSlot(CharacterFormat format) : base(format)
            {
            }
        }

        public sealed class Section
        {
            public List<FlowInline> Inlines { get; } = new List<FlowInline>();
        }

        public static (Section Left, Section Center, Section Right, bool HasPicture) Parse(string spec, CharacterFormat baseFormat, HeaderFooterValues v)
        {
            Section left = new Section(), center = new Section(), right = new Section();
            Section current = center;
            CharacterFormat f = baseFormat;
            StringBuilder text = new StringBuilder();
            bool picture = false;

            void Flush()
            {
                if (text.Length > 0)
                {
                    string[] lines = text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i > 0)
                        {
                            current.Inlines.Add(new BreakInline(BreakKind.Line, f));
                        }
                        if (lines[i].Length > 0)
                        {
                            current.Inlines.Add(new TextRun(lines[i], f));
                        }
                    }
                    text.Clear();
                }
            }

            int i = 0;
            while (i < spec.Length)
            {
                char c = spec[i];
                if (c != '&' || i + 1 >= spec.Length)
                {
                    text.Append(c);
                    i++;
                    continue;
                }
                char code = spec[i + 1];
                i += 2;
                switch (code)
                {
                    case '&':
                        text.Append('&');
                        break;
                    case 'L':
                    case 'l':
                        Flush();
                        current = left;
                        f = baseFormat;
                        break;
                    case 'C':
                    case 'c':
                        Flush();
                        current = center;
                        f = baseFormat;
                        break;
                    case 'R':
                    case 'r':
                        Flush();
                        current = right;
                        f = baseFormat;
                        break;
                    case 'P':
                    case 'p':
                        int page = v.Page;
                        if (i < spec.Length && (spec[i] == '+' || spec[i] == '-'))
                        {
                            int sign = spec[i] == '+' ? 1 : -1;
                            int j = i + 1;
                            while (j < spec.Length && char.IsAsciiDigit(spec[j]))
                            {
                                j++;
                            }
                            if (j > i + 1 && int.TryParse(spec.AsSpan(i + 1, j - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int delta))
                            {
                                page += sign * delta;
                                i = j;
                            }
                        }
                        text.Append(page.ToString(CultureInfo.InvariantCulture));
                        break;
                    case 'N':
                    case 'n':
                        text.Append(v.Pages.ToString(CultureInfo.InvariantCulture));
                        break;
                    case 'D':
                    case 'd':
                        text.Append(v.Now.ToString("d", v.Culture));
                        break;
                    case 'T':
                    case 't':
                        text.Append(v.Now.ToString("t", v.Culture));
                        break;
                    case 'F':
                    case 'f':
                        text.Append(v.FileName);
                        break;
                    case 'Z':
                    case 'z':
                        text.Append(v.FilePath);
                        break;
                    case 'A':
                    case 'a':
                        text.Append(v.SheetName);
                        break;
                    case 'G':
                    case 'g':
                        Flush();
                        current.Inlines.Add(new PictureSlot(f));
                        picture = true;
                        break;
                    case 'B':
                    case 'b':
                        Flush();
                        f = f with { Bold = !f.Bold };
                        break;
                    case 'I':
                    case 'i':
                        Flush();
                        f = f with { Italic = !f.Italic };
                        break;
                    case 'U':
                    case 'u':
                        Flush();
                        f = f with { Underline = f.Underline == UnderlineStyle.Single ? UnderlineStyle.None : UnderlineStyle.Single };
                        break;
                    case 'E':
                    case 'e':
                        Flush();
                        f = f with { Underline = f.Underline == UnderlineStyle.Double ? UnderlineStyle.None : UnderlineStyle.Double };
                        break;
                    case 'S':
                    case 's':
                        Flush();
                        f = f with { Strikethrough = !f.Strikethrough };
                        break;
                    case 'X':
                    case 'x':
                        Flush();
                        f = f with { VerticalAlignment = f.VerticalAlignment == VerticalTextAlignment.Superscript ? VerticalTextAlignment.Baseline : VerticalTextAlignment.Superscript };
                        break;
                    case 'Y':
                    case 'y':
                        Flush();
                        f = f with { VerticalAlignment = f.VerticalAlignment == VerticalTextAlignment.Subscript ? VerticalTextAlignment.Baseline : VerticalTextAlignment.Subscript };
                        break;
                    case 'K':
                    case 'k':
                        Flush();
                        if (i + 6 <= spec.Length && uint.TryParse(spec.AsSpan(i, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                        {
                            f = f with { Color = new LayoutColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) };
                            i += 6;
                        }
                        else if (i + 6 <= spec.Length)
                        {
                            i += 6; // theme colour "ttSnnn": not resolved here
                        }
                        break;
                    case '"':
                        Flush();
                        int close = spec.IndexOf('"', i);
                        if (close < 0)
                        {
                            i = spec.Length;
                            break;
                        }
                        string[] parts = spec.Substring(i, close - i).Split(',');
                        i = close + 1;
                        if (parts.Length > 0 && parts[0] != "-" && parts[0].Length > 0)
                        {
                            f = f with { FontFamily = parts[0] };
                        }
                        if (parts.Length > 1)
                        {
                            string style = parts[1].ToLowerInvariant();
                            f = f with { Bold = style.Contains("bold", StringComparison.Ordinal), Italic = style.Contains("italic", StringComparison.Ordinal) };
                        }
                        break;
                    default:
                        if (char.IsAsciiDigit(code))
                        {
                            Flush();
                            int j = i - 1;
                            while (j < spec.Length && j < i + 2 && char.IsAsciiDigit(spec[j]))
                            {
                                j++;
                            }
                            if (int.TryParse(spec.AsSpan(i - 1, j - (i - 1)), NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 0)
                            {
                                f = f with { FontSize = size };
                            }
                            i = j;
                        }
                        break;
                }
            }
            Flush();
            return (left, center, right, picture);
        }
    }

    internal sealed partial class GridEngine
    {
        private void EmitHeaderFooter(GridSheet sheet, GridPageRecord rec, int pageIndex, double scale, List<LayoutOp> ops, List<LayoutBox> boxes)
        {
            GridPageSetup ps = sheet.PageSetup;
            bool first = ps.DifferentFirst && rec.SheetPageIndex == 0;
            bool even = ps.DifferentOddEven && rec.PageNumber % 2 == 0;
            string? header = first ? ps.FirstHeader : even ? ps.EvenHeader : ps.OddHeader;
            string? footer = first ? ps.FirstFooter : even ? ps.EvenFooter : ps.OddFooter;
            double fontScale = ps.HeaderFooterScaleWithDocument ? scale : 1;
            CharacterFormat baseFormat = sheet.NormalFont with { FontSize = sheet.NormalFont.FontSize };
            HeaderFooterValues v = new HeaderFooterValues(rec.PageNumber, _pageCount, sheet.Name,
                _ctx.Options.DocumentName ?? string.Empty, _ctx.Options.DocumentName ?? string.Empty,
                _ctx.Options.Now ?? DateTimeOffset.Now, _ctx.Options.Culture);
            double left = ps.HeaderFooterAlignWithMargins ? ps.MarginLeft : 0.7 * 72;
            double width = ps.PageWidth - left - (ps.HeaderFooterAlignWithMargins ? ps.MarginRight : 0.7 * 72);
            string kind = first ? "FIRST" : even ? "EVEN" : string.Empty;
            if (!string.IsNullOrEmpty(header))
            {
                EmitHeaderFooterBlock(header, baseFormat, fontScale, v, left, width, ps.HeaderMargin, true, kind, sheet, ops, boxes);
            }
            if (!string.IsNullOrEmpty(footer))
            {
                EmitHeaderFooterBlock(footer, baseFormat, fontScale, v, left, width, ps.PageHeight - ps.FooterMargin, false, kind, sheet, ops, boxes);
            }
        }

        private void EmitHeaderFooterBlock(string spec, CharacterFormat baseFormat, double fontScale, HeaderFooterValues v, double left, double width,
            double anchorY, bool isHeader, string kind, GridSheet sheet, List<LayoutOp> ops, List<LayoutBox> boxes)
        {
            (ExcelHeaderFooter.Section l, ExcelHeaderFooter.Section c, ExcelHeaderFooter.Section r, bool _) = ExcelHeaderFooter.Parse(spec, baseFormat, v);
            (ExcelHeaderFooter.Section Section, ParagraphAlignment Align, char Letter)[] parts =
            {
                (l, ParagraphAlignment.Left, 'L'), (c, ParagraphAlignment.Center, 'C'), (r, ParagraphAlignment.Right, 'R'),
            };
            foreach ((ExcelHeaderFooter.Section section, ParagraphAlignment align, char letter) in parts)
            {
                if (section.Inlines.Count == 0)
                {
                    continue;
                }
                FlowParagraph p = new FlowParagraph { Format = new ParagraphFormat { Alignment = align, WidowControl = false }, MarkFormat = Scaled(baseFormat, fontScale) };
                foreach (FlowInline inline in section.Inlines)
                {
                    if (inline is ExcelHeaderFooter.PictureSlot)
                    {
                        // VML shape ids: LH, CH, RH, LF, CF, RF, plus EVEN or FIRST.
                        string id = letter.ToString() + (isHeader ? "H" : "F") + kind;
                        if (sheet.PageSetup.Pictures is { } pictures && pictures.TryGetValue(id, out GridHeaderFooterPicture? pic))
                        {
                            double w = (pic.Width ?? (pic.Image.PixelWidth * 0.75)) * fontScale;
                            double h = (pic.Height ?? (pic.Image.PixelHeight * 0.75)) * fontScale;
                            if (w > 0 && h > 0)
                            {
                                p.Inlines.Add(new ImageInline(pic.Image, w, h, Scaled(inline.Format, fontScale)));
                            }
                        }
                        else
                        {
                            _ctx.Warn(LayoutWarningCodes.ImagePlaceholder, LayoutWarningSeverity.ContentLoss,
                                "A header or footer picture (&G) has no image data (linked or not loaded) and is not drawn.", sheet.Name);
                        }
                        continue;
                    }
                    p.Inlines.Add(inline switch
                    {
                        TextRun t => new TextRun(t.Text, Scaled(t.Format, fontScale)),
                        BreakInline b => new BreakInline(b.Kind, Scaled(b.Format, fontScale)),
                        _ => inline,
                    });
                }
                (MeasuredParagraph m, List<FittedLine> lines) = MeasureCell(p, width);
                double total = 0;
                foreach (FittedLine line in lines)
                {
                    total += line.Height;
                }
                double y = isHeader ? anchorY : anchorY - total;
                StringBuilder ignore = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                {
                    FlowEmitter.EmitLine(new PlacedLine { Paragraph = m, Line = lines[i], LineIndex = i, AreaLeft = left, AreaWidth = width, Top = y, Path = isHeader ? "header" : "footer" },
                        _ctx, ops, new List<LayoutBox>(), ignore);
                    y += lines[i].Height;
                }
                boxes.Add(new LayoutBox(isHeader ? LayoutBoxKind.Header : LayoutBoxKind.Footer,
                    new LayoutRect(left, isHeader ? anchorY : anchorY - total, width, total), (isHeader ? "header:" : "footer:") + align.ToString()));
            }
        }
    }
}
