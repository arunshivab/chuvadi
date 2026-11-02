using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using Chuvadi.Markup;
using M = Chuvadi.Markup.Markdown;

namespace Chuvadi.Layout.Markup
{
    /// <summary>
    /// Builds Chuvadi.Layout's flow document from Chuvadi.Markup's Markdown AST (CommonMark 0.31.2 with the GFM
    /// extensions): headings, paragraphs, emphasis, strong, strikethrough, code spans and blocks, block quotes, bullet,
    /// ordered and task lists (tight and loose), tables with column alignment, thematic breaks, links, images and hard
    /// breaks. Text is never altered. Raw HTML follows <see cref="MarkdownImportOptions.RawHtml"/>.
    /// </summary>
    public static class MarkdownImporter
    {
        /// <summary>Parses Markdown text and builds the flow document.</summary>
        /// <exception cref="LayoutLimitExceededException">The input exceeds a Chuvadi.Markup or <see cref="LayoutLimits"/> limit.</exception>
        public static FlowDocument Import(string markdown, MarkdownImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(markdown);
            layoutOptions ??= new LayoutOptions();
            options ??= new MarkdownImportOptions();
            M.MarkdownDocument ast = Parse(markdown, options, layoutOptions, cancellationToken);
            return Import(ast, options, layoutOptions, warning, cancellationToken);
        }

        /// <summary>Builds the flow document from a Markdown AST (parsed, or built in code).</summary>
        public static FlowDocument Import(M.MarkdownDocument document, MarkdownImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            return new MarkdownImport(options ?? new MarkdownImportOptions(), layoutOptions ?? new LayoutOptions(), warning ?? (_ => { }), cancellationToken).Run(document);
        }

        /// <summary>The parser options: the caller's, or GFM with limits derived from the layout limits.</summary>
        internal static M.MarkdownOptions ParserOptions(MarkdownImportOptions o, LayoutLimits limits)
        {
            if (o.Parser is M.MarkdownOptions given)
            {
                return given;
            }
            M.MarkdownOptions p = M.MarkdownOptions.Gfm();
            p.Limits = new MarkupLimits
            {
                MaxInputChars = (int)Math.Min(int.MaxValue, limits.MaxSourceBytes),
                MaxNodes = (int)Math.Clamp(limits.MaxBlocks * 16, 1024, 4L * 1024 * 1024),
                MaxTableCells = (int)Math.Clamp(limits.MaxCellsPerSheet, 1024, 1024L * 1024),
                // Deeper Markdown nesting is flattened by the parser (its content is kept).
                MaxNestingDepth = Math.Clamp(limits.MaxNestingDepth * 4, 16, 512),
            };
            return p;
        }

        internal static M.MarkdownDocument Parse(string markdown, MarkdownImportOptions o, LayoutOptions lo, CancellationToken ct)
        {
            try
            {
                return M.MarkdownParser.Parse(markdown, ParserOptions(o, lo.Limits), ct);
            }
            catch (MarkupLimitExceededException ex)
            {
                throw new LayoutLimitExceededException("Markup" + ex.LimitName, ex.Limit, ex.Actual);
            }
        }
    }

    /// <summary>One Markdown → flow conversion.</summary>
    internal sealed class MarkdownImport
    {
        private readonly MarkdownImportOptions _o;
        private readonly MarkdownStyleSheet _s;
        private readonly LayoutOptions _lo;
        private readonly Action<LayoutWarning> _warn;
        private readonly CancellationToken _ct;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private int _headings1;
        private long _blocks;
        private string? _title;
        private double _contentWidth;

        public MarkdownImport(MarkdownImportOptions options, LayoutOptions layoutOptions, Action<LayoutWarning> warn, CancellationToken ct)
        {
            _o = options;
            _s = options.Styles ?? MarkdownStyleSheet.Default;
            _lo = layoutOptions;
            _warn = warn;
            _ct = ct;
        }

        /// <summary>Where a block sits: its indent, quote depth, and the list item spacing in force.</summary>
        private readonly record struct Context(double Indent, int Quote, int ListDepth, bool Tight);

        public FlowDocument Run(M.MarkdownDocument ast)
        {
            PageGeometry page = _o.Page ?? PaperDefaults.PageForCulture(_lo.Culture);
            _contentWidth = page.ContentWidth;
            FlowSection section = new FlowSection { Page = page };
            Blocks(ast.Children, section.Blocks, new Context(0, 0, 0, false));
            TrimLastSpacing(section.Blocks);
            if (_o.PageNumbers)
            {
                section.HeadersFooters = new HeaderFooterSet { DefaultFooter = new List<FlowBlock> { Footer() } };
            }
            FlowDocument doc = new FlowDocument { Title = _o.Title ?? _title ?? _lo.DocumentName };
            doc.Sections.Add(section);
            return doc;
        }

        private void Warn(string code, LayoutWarningSeverity severity, string message)
        {
            if (_warned.Add(code + "\u0000" + message))
            {
                _warn(new LayoutWarning(code, severity, message));
            }
        }

        private FlowParagraph Footer() => Shared.PageFooter(_o.PageNumberText, _s.Body with { FontSize = _s.FooterSize, Color = _s.MutedColor });

        // ---- Blocks ----

        private void Blocks(IReadOnlyList<M.MarkdownNode> nodes, IList<FlowBlock> into, Context c)
        {
            foreach (M.MarkdownNode node in nodes)
            {
                _ct.ThrowIfCancellationRequested();
                Limits.Check(nameof(LayoutLimits.MaxBlocks), _lo.Limits.MaxBlocks, ++_blocks);
                switch (node)
                {
                    case M.Heading h:
                        into.Add(Heading(h, c));
                        break;
                    case M.Paragraph p:
                        into.Add(Paragraph(p, c, c.Tight ? _s.TightItemSpacing : _s.BlockSpacing));
                        break;
                    case M.CodeBlock code:
                        into.Add(CodeBlock(code, c));
                        break;
                    case M.BlockQuote q:
                    {
                        Context inner = c with { Indent = c.Indent + _s.Indent, Quote = c.Quote + 1, Tight = false };
                        int start = into.Count;
                        Blocks(q.Children, into, inner);
                        if (into.Count == start)
                        {
                            into.Add(Empty(inner));
                        }
                        break;
                    }
                    case M.ListBlock list:
                        List(list, into, c);
                        break;
                    case M.ThematicBreak:
                        into.Add(new FlowParagraph
                        {
                            Format = Base(c, _s.BlockSpacing) with { BorderBottom = new BorderLine(1.5, _s.RuleColor, Space: 0) },
                            MarkFormat = _s.Body with { FontSize = 4 },
                        });
                        break;
                    case M.Table t:
                        into.Add(Table(t, c));
                        into.Add(new FlowParagraph { Format = Base(c, 0) with { LineSpacingRule = LineSpacingRule.Exact, LineSpacing = _s.BlockSpacing }, MarkFormat = _s.Body });
                        break;
                    case M.HtmlBlock html:
                        if (RawHtmlText(html.Html.TrimEnd('\n')) is string raw)
                        {
                            FlowParagraph hp = new FlowParagraph { Format = Base(c, _s.BlockSpacing), MarkFormat = BodyFor(c) };
                            hp.Inlines.Add(new TextRun(raw, BodyFor(c)));
                            into.Add(hp);
                        }
                        break;
                }
            }
        }

        private string? RawHtmlText(string html)
        {
            switch (_o.RawHtml)
            {
                case M.RawHtmlMode.Escape:
                    return html;
                case M.RawHtmlMode.Passthrough:
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss, "Raw HTML in Markdown is not laid out in this version; it was left out.");
                    return null;
                default:
                    return null;
            }
        }

        private CharacterFormat BodyFor(Context c) => c.Quote > 0 ? _s.Body with { Color = _s.QuoteTextColor } : _s.Body;

        /// <summary>Indents stop growing at half the text width, so deep nesting still leaves room for text.</summary>
        private double Indent(double indent) => Math.Min(indent, Math.Max(0, _contentWidth / 2));

        private ParagraphFormat Base(Context c, double after) => new ParagraphFormat
        {
            LeftIndent = Indent(c.Indent),
            SpaceAfter = after,
            LineSpacing = _s.LineSpacing,
            BorderLeft = c.Quote > 0 ? new BorderLine(3, _s.QuoteBarColor, Space: 8) : BorderLine.None,
        };

        private FlowParagraph Empty(Context c) => new FlowParagraph { Format = Base(c, _s.BlockSpacing), MarkFormat = BodyFor(c) };

        private FlowParagraph Heading(M.Heading h, Context c)
        {
            int level = Math.Clamp(h.Level, 1, 6);
            IReadOnlyList<double> sizes = _s.HeadingSizes;
            double size = sizes.Count >= level ? sizes[level - 1] : _s.BodySize;
            CharacterFormat f = new CharacterFormat
            {
                FontFamily = _s.HeadingFont,
                FontSize = size,
                Bold = true,
                Color = level == 6 ? _s.MutedColor : _s.HeadingColor,
            };
            if (level == 1)
            {
                _title ??= h.ToPlainText().Trim();
                _headings1++;
            }
            ParagraphFormat pf = Base(c, level <= 2 ? 8 : 6) with
            {
                SpaceBefore = level <= 2 ? 18 : 12,
                KeepWithNext = true,
                KeepLinesTogether = true,
                PageBreakBefore = level == 1 && _o.PageBreakBeforeHeading1 && _headings1 > 1,
                BorderBottom = level <= 2 && _s.HeadingRules ? new BorderLine(0.75, _s.RuleColor, Space: 3) : BorderLine.None,
                StyleId = "Heading" + level.ToString(CultureInfo.InvariantCulture),
            };
            FlowParagraph p = new FlowParagraph { Format = pf, MarkFormat = f };
            Inlines(h.Children, p, f, null);
            return p;
        }

        private FlowParagraph Paragraph(M.Paragraph para, Context c, double after)
        {
            CharacterFormat f = BodyFor(c);
            FlowParagraph p = new FlowParagraph { Format = Base(c, after), MarkFormat = f };
            Inlines(para.Children, p, f, null);
            return p;
        }

        private FlowParagraph CodeBlock(M.CodeBlock code, Context c)
        {
            CharacterFormat f = MarkdownStyleSheet.Code(BodyFor(c), _s);
            string text = code.Code.EndsWith('\n') ? code.Code[..^1] : code.Code;
            FlowParagraph p = new FlowParagraph
            {
                Format = Base(c, _s.BlockSpacing) with
                {
                    Background = _s.CodeBackground,
                    LineSpacing = 1.0,
                    // Long lines wrap at the margin; the box keeps a little air on each side.
                    BorderTop = new BorderLine(0.1, _s.CodeBackground, Space: 6),
                    BorderBottom = new BorderLine(0.1, _s.CodeBackground, Space: 6),
                    BorderLeft = c.Quote > 0 ? new BorderLine(3, _s.QuoteBarColor, Space: 8) : new BorderLine(0.1, _s.CodeBackground, Space: 6),
                    BorderRight = new BorderLine(0.1, _s.CodeBackground, Space: 6),
                    LeftIndent = Indent(c.Indent) + 6,
                    RightIndent = 6,
                    KeepLinesTogether = text.Split('\n').Length <= 12,
                },
                MarkFormat = f,
            };
            if (text.Length > 0)
            {
                p.Inlines.Add(new TextRun(text, f));
            }
            return p;
        }

        private void List(M.ListBlock list, IList<FlowBlock> into, Context c)
        {
            int number = list.Start;
            Context inner = c with { Indent = c.Indent + _s.Indent, ListDepth = c.ListDepth + 1, Tight = list.IsTight };
            IReadOnlyList<string> bullets = _s.Bullets.Count > 0 ? _s.Bullets : new[] { "\u2022" };
            foreach (M.MarkdownNode child in list.Children)
            {
                if (child is not M.ListItem item)
                {
                    continue;
                }
                string label = item.TaskState switch
                {
                    M.TaskState.Checked => _s.TaskDone,
                    M.TaskState.Unchecked => _s.TaskOpen,
                    _ => list.ListKind == M.ListKind.Ordered
                        ? number.ToString(CultureInfo.InvariantCulture) + list.Delimiter.ToString()
                        : bullets[c.ListDepth % bullets.Count],
                };
                number++;
                int start = into.Count;
                Blocks(item.Children, into, inner);
                if (into.Count == start)
                {
                    into.Add(Empty(inner));
                }
                // The label goes on the item's first paragraph, hanging into the indent.
                if (into[start] is FlowParagraph first)
                {
                    // The hanging indent is the label's tab stop (as in Word).
                    first.Format = first.Format with { FirstLineIndent = -_s.Indent };
                    first.ListLabel = new ListLabel(label, first.MarkFormat with { Bold = false, Italic = false, Strikethrough = false, Background = null });
                }
                else if (into[start] is FlowTable)
                {
                    FlowParagraph holder = new FlowParagraph { Format = Base(inner, 0) with { FirstLineIndent = -_s.Indent }, MarkFormat = BodyFor(inner) };
                    holder.ListLabel = new ListLabel(label, BodyFor(inner));
                    into.Insert(start, holder);
                }
            }
            // A tight list is followed by the normal block spacing.
            if (list.IsTight && into.Count > 0 && into[^1] is FlowParagraph last && c.ListDepth == 0)
            {
                last.Format = last.Format with { SpaceAfter = _s.BlockSpacing };
            }
        }

        private FlowTable Table(M.Table t, Context c)
        {
            int columns = Math.Max(1, t.Alignments.Count);
            BorderLine line = new BorderLine(0.75, _s.TableBorderColor);
            FlowTable ft = new FlowTable
            {
                Format = new Model.TableFormat
                {
                    Indent = Indent(c.Indent),
                    CellMarginLeft = _s.TableCellPadding,
                    CellMarginRight = _s.TableCellPadding,
                    CellMarginTop = 2,
                    CellMarginBottom = 2,
                    BorderTop = line,
                    BorderBottom = line,
                    BorderLeft = line,
                    BorderRight = line,
                    BorderInsideHorizontal = line,
                    BorderInsideVertical = line,
                },
            };
            // Column widths follow the longest cell text, within bounds, sharing the available width.
            double[] weight = new double[columns];
            foreach (M.MarkdownNode rowNode in t.Children)
            {
                int ci = 0;
                foreach (M.MarkdownNode cellNode in rowNode.Children)
                {
                    if (ci < columns)
                    {
                        weight[ci] = Math.Max(weight[ci], cellNode.ToPlainText().Length);
                    }
                    ci++;
                }
            }
            double total = 0;
            for (int i = 0; i < columns; i++)
            {
                weight[i] = Math.Clamp(weight[i], 3, 40);
                total += weight[i];
            }
            double available = Math.Max(36, _contentWidth - Indent(c.Indent));
            for (int i = 0; i < columns; i++)
            {
                ft.ColumnWidths.Add(available * weight[i] / total);
            }
            foreach (M.MarkdownNode rowNode in t.Children)
            {
                if (rowNode is not M.TableRow row)
                {
                    continue;
                }
                FlowTableRow fr = new FlowTableRow { IsHeader = row.IsHeader, CantSplit = true };
                int ci = 0;
                foreach (M.MarkdownNode cellNode in row.Children)
                {
                    if (ci >= columns)
                    {
                        break;
                    }
                    M.TableAlignment align = cellNode is M.TableCell cell ? cell.Alignment : M.TableAlignment.None;
                    if (align == M.TableAlignment.None && ci < t.Alignments.Count)
                    {
                        align = t.Alignments[ci];
                    }
                    CharacterFormat f = row.IsHeader ? _s.Body with { Bold = true } : _s.Body;
                    FlowParagraph p = new FlowParagraph
                    {
                        Format = new ParagraphFormat
                        {
                            LineSpacing = _s.LineSpacing,
                            Alignment = align switch
                            {
                                M.TableAlignment.Center => ParagraphAlignment.Center,
                                M.TableAlignment.Right => ParagraphAlignment.Right,
                                _ => ParagraphAlignment.Left,
                            },
                        },
                        MarkFormat = f,
                    };
                    Inlines(cellNode.Children, p, f, null);
                    FlowTableCell fc = new FlowTableCell { Background = row.IsHeader ? _s.TableHeaderBackground : null };
                    fc.Blocks.Add(p);
                    fr.Cells.Add(fc);
                    ci++;
                }
                while (fr.Cells.Count < columns)
                {
                    FlowTableCell filler = new FlowTableCell();
                    filler.Blocks.Add(new FlowParagraph { MarkFormat = _s.Body });
                    fr.Cells.Add(filler);
                }
                ft.Rows.Add(fr);
            }
            return ft;
        }

        /// <summary>The last block ends the page flow: its trailing space is not needed.</summary>
        private static void TrimLastSpacing(IList<FlowBlock> blocks)
        {
            if (blocks.Count > 0 && blocks[^1] is FlowParagraph p && p.Inlines.Count == 0 && p.ListLabel is null && !p.Format.BorderBottom.IsVisible)
            {
                blocks.RemoveAt(blocks.Count - 1);
            }
        }

        // ---- Inlines ----

        private void Inlines(IReadOnlyList<M.MarkdownNode> nodes, FlowParagraph p, CharacterFormat f, string? link)
        {
            foreach (M.MarkdownNode node in nodes)
            {
                switch (node)
                {
                    case M.TextInline t:
                        Text(p, t.Text, f, link);
                        break;
                    case M.CodeSpan code:
                        Text(p, code.Code, MarkdownStyleSheet.Code(f, _s) with { Background = _s.CodeBackground }, link);
                        break;
                    case M.Emphasis e:
                        Inlines(e.Children, p, f with { Italic = true }, link);
                        break;
                    case M.Strong s:
                        Inlines(s.Children, p, f with { Bold = true }, link);
                        break;
                    case M.Strikethrough st:
                        Inlines(st.Children, p, f with { Strikethrough = true }, link);
                        break;
                    case M.Link l:
                    {
                        string? target = Link(l.IsEmail && !l.Destination.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? "mailto:" + l.Destination : l.Destination);
                        CharacterFormat lf = f with { Color = _s.LinkColor, Underline = _s.UnderlineLinks ? UnderlineStyle.Single : f.Underline };
                        Inlines(l.Children, p, lf, target ?? link);
                        break;
                    }
                    case M.Image img:
                        Image(img, p, f, link);
                        break;
                    case M.HtmlInline html:
                        if (RawHtmlText(html.Html) is string raw)
                        {
                            Text(p, raw, f, link);
                        }
                        break;
                    case M.HardBreak:
                        p.Inlines.Add(new BreakInline(BreakKind.Line, f));
                        break;
                    case M.SoftBreak:
                        Text(p, " ", f, link);
                        break;
                    default:
                        if (node.Children.Count > 0)
                        {
                            Inlines(node.Children, p, f, link);
                        }
                        break;
                }
            }
        }

        private static void Text(FlowParagraph p, string text, CharacterFormat f, string? link)
        {
            if (text.Length == 0)
            {
                return;
            }
            // Markdown text never carries tabs or newlines that are meant as layout (except in code).
            p.Inlines.Add(new TextRun(text, f) { HyperlinkUri = link });
        }

        private string? Link(string destination)
        {
            if (_o.LinkResolver is Func<string, string?> resolve)
            {
                return resolve(destination);
            }
            return Shared.DefaultLink(destination);
        }

        private void Image(M.Image img, FlowParagraph p, CharacterFormat f, string? link)
        {
            byte[]? bytes = null;
            if (_o.DataUriImages && img.Destination.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                bytes = Shared.DataUri(img.Destination, _lo.Limits);
            }
            else if (_o.ImageResolver is Func<string, byte[]?> resolve)
            {
                bytes = resolve(img.Destination);
            }
            string alt = img.ToPlainText();
            if (bytes is null || bytes.Length == 0)
            {
                Warn(LayoutWarningCodes.ImagePlaceholder, LayoutWarningSeverity.ContentLoss,
                    "An image was not available (no ImageResolver, or it returned nothing); its alternative text is printed.");
                Text(p, "[" + (alt.Length > 0 ? alt : "image") + "]", f with { Italic = true, Color = _s.MutedColor }, link);
                return;
            }
            Limits.Check(nameof(LayoutLimits.MaxResourceBytes), _lo.Limits.MaxResourceBytes, bytes.LongLength);
            LayoutImage image = LayoutImage.FromEncoded(bytes);
            double dpi = _o.ImageDpi > 0 ? _o.ImageDpi : 96;
            double w = image.PixelWidth > 0 ? image.PixelWidth * 72 / dpi : 144;
            double h = image.PixelHeight > 0 ? image.PixelHeight * 72 / dpi : 108;
            double max = Math.Max(36, _contentWidth - p.Format.LeftIndent - p.Format.RightIndent);
            if (w > max)
            {
                h *= max / w;
                w = max;
            }
            p.Inlines.Add(new ImageInline(image, w, h, f) { Description = alt });
        }
    }
}
