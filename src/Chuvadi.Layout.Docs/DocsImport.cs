using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using D = Chuvadi.Docs.Word;

namespace Chuvadi.Layout.Docs
{
    /// <summary>One docx → flow IR conversion (see <see cref="DocsImporter"/>).</summary>
    internal sealed partial class DocsImport
    {
        private readonly D.Document _doc;
        private readonly DocsImportOptions _o;
        private readonly LayoutOptions _lo;
        private readonly Action<LayoutWarning> _warn;
        private readonly CancellationToken _ct;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private readonly DocsTheme _theme;
        private readonly IReadOnlyDictionary<D.Paragraph, D.ListLabel> _labels;
        private readonly Dictionary<D.Paragraph, FlowParagraph> _converted = new Dictionary<D.Paragraph, FlowParagraph>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<D.HeaderFooterContent, IList<FlowBlock>> _stories = new Dictionary<D.HeaderFooterContent, IList<FlowBlock>>(ReferenceEqualityComparer.Instance);
        private CharacterFormat? _defaultMark;

        /// <summary>What text is drawn over (cell, then paragraph shading), for Word's automatic text colour.</summary>
        private LayoutColor? _behind;
        private PageGeometry _page = PageGeometry.A4;
        private long _blocks;

        public DocsImport(D.Document doc, DocsImportOptions options, LayoutOptions layoutOptions, Action<LayoutWarning> warn, CancellationToken ct)
        {
            _doc = doc;
            _o = options;
            _lo = layoutOptions;
            _warn = warn;
            _ct = ct;
            _theme = DocsTheme.Parse(doc.ThemeXml);
            _labels = doc.GetListLabels();
        }

        public FlowDocument Run()
        {
            // Tabs beyond the last stop advance by the document's default tab stop (settings w:defaultTabStop; 36 pt).
            FlowDocument flow = new FlowDocument { Title = _lo.DocumentName, DefaultTabStop = _doc.EffectiveDefaultTabStopPt };
            flow.EmbeddedFonts = EmbeddedFonts(_doc, _warn);
            ReadNoteReferences();
            if (_doc.FootnoteProperties.Position is D.NotePosition.BeneathText)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Footnotes placed beneath the text are laid out at the foot of the page.");
            }
            IReadOnlyList<D.Section> sections = _doc.Sections;
            bool oddEven = _doc.DifferentOddAndEvenPages ?? AnyEvenContent(sections);
            HeaderFooterSet? previous = null;
            FlowWatermark? previousWatermark = null;
            for (int i = 0; i < sections.Count; i++)
            {
                D.Section s = sections[i];
                FlowSection fs = Section(s, i);
                if (_o.IncludeHeadersAndFooters)
                {
                    fs.HeadersFooters = HeadersFooters(s, previous, oddEven, fs.Page);
                    previous = fs.HeadersFooters;
                }
                if (_o.IncludeWatermarks)
                {
                    // A watermark lives in the header parts; a section whose headers are all linked shows the previous one.
                    fs.Watermark = s.Watermark is D.TextWatermark wm ? Watermark(wm) : AllHeadersLinked(s) ? previousWatermark : null;
                    previousWatermark = fs.Watermark;
                }
                flow.Sections.Add(fs);
            }
            if (flow.Sections.Count == 0)
            {
                flow.Sections.Add(new FlowSection { Page = PaperDefaults.PageForCulture(_lo.Culture) });
            }

            // Body: a section ends at the paragraph that carries its properties.
            int si = 0;
            foreach (object block in _doc.Blocks)
            {
                _ct.ThrowIfCancellationRequested();
                FlowSection target = flow.Sections[Math.Min(si, flow.Sections.Count - 1)];
                _page = target.Page;
                if (Block(block, null, 1, FirstColumnWidth(target)) is FlowBlock fb)
                {
                    target.Blocks.Add(fb);
                }
                if (block is D.Paragraph p && p.SectionProperties is not null)
                {
                    si++;
                }
            }
            AppendEndnotes(flow);
            return flow;
        }

        /// <summary>
        /// The width body content is read at: the section's column (its first column when the columns are unequal;
        /// tables without column widths are then sized again in the column they are placed in).
        /// </summary>
        private static double FirstColumnWidth(FlowSection s)
        {
            double width = s.Page.ContentWidth;
            if (s.ColumnWidths.Count > 0)
            {
                return Math.Max(1, s.ColumnWidths[0].Width);
            }
            int n = Math.Max(1, s.ColumnCount);
            return n == 1 ? width : Math.Max(1, (width - (Math.Max(0, s.ColumnSpacing) * (n - 1))) / n);
        }

        private void Warn(string code, LayoutWarningSeverity severity, string message, string? location = null)
        {
            if (_warned.Add(code + "\u0000" + message))
            {
                _warn(new LayoutWarning(code, severity, message, location));
            }
        }

        private static bool AnyEvenContent(IReadOnlyList<D.Section> sections)
        {
            foreach (D.Section s in sections)
            {
                if (HasContent(s.EvenPageHeader) || HasContent(s.EvenPageFooter))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasContent(D.HeaderFooterContent c) => c.Count > 0 || c.Blocks.Count > 0;

        private static bool AllHeadersLinked(D.Section s)
            => !HasContent(s.Header) && s.Header.LinkToPrevious && !HasContent(s.FirstPageHeader) && s.FirstPageHeader.LinkToPrevious
               && !HasContent(s.EvenPageHeader) && s.EvenPageHeader.LinkToPrevious;

        // ---- Sections ----

        private FlowSection Section(D.Section s, int index)
        {
            D.PageSetup ps = s.Page;
            double width = ps.WidthPt, height = ps.HeightPt;
            if (!ps.SizeStated)
            {
                // D-30: the section states no paper size; Word uses its locale's default (Letter in the Letter
                // regions, A4 elsewhere), and so does Layout, from LayoutOptions.Culture.
                PaperSize paper = PaperDefaults.ForCulture(_lo.Culture);
                bool landscape = ps.Orientation == D.PageOrientation.Landscape;
                (width, height) = landscape ? (paper.Height, paper.Width) : (paper.Width, paper.Height);
            }
            PageGeometry page = new PageGeometry
            {
                Width = width,
                Height = height,
                // A negative top or bottom margin is "exactly": the header or footer never pushes the text.
                MarginTop = Math.Abs(ps.TopMarginPt),
                MarginBottom = Math.Abs(ps.BottomMarginPt),
                MarginLeft = ps.LeftMarginPt,
                MarginRight = ps.RightMarginPt,
                HeaderDistance = ps.HeaderDistancePt,
                FooterDistance = ps.FooterDistancePt,
                Gutter = ps.GutterPt,
                GutterAtTop = _doc.GutterAtTop,
                MirrorMargins = _doc.MirrorMargins,
            };
            string where = "section " + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            // ECMA-376 17.6.4: with w:equalWidth="0" the w:col elements give each column's width and the space after it.
            FlowSection fs = new FlowSection
            {
                Page = page,
                Start = s.Start switch
                {
                    D.SectionStart.Continuous => SectionStart.Continuous,
                    D.SectionStart.EvenPage => SectionStart.EvenPage,
                    D.SectionStart.OddPage => SectionStart.OddPage,
                    D.SectionStart.NextColumn => SectionStart.NextColumn,
                    _ => SectionStart.NextPage,
                },
                ColumnCount = s.Columns.EqualWidth == false && s.Columns.Columns.Count > 0 ? s.Columns.Columns.Count : Math.Max(1, s.Columns.Count ?? 1),
                ColumnSpacing = s.Columns.SpacingPt is double sp && sp >= 0 ? sp : 36,
                ColumnSeparator = s.Columns.Separator == true,
                PageNumberStart = s.PageNumbering.Start,
                PageNumberFormat = PageFormat(s.PageNumbering.Format),
            };
            if (s.Columns.EqualWidth == false && s.Columns.Columns.Count > 0)
            {
                foreach (D.ColumnDefinition c in s.Columns.Columns)
                {
                    fs.ColumnWidths.Add(new FlowColumn(c.WidthPt, c.SpaceAfterPt));
                }
            }
            if (s.PageNumbering.ChapterStyle is not null)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Chapter numbers in page numbers are not shown.", where);
            }
            if (s.LineNumbering is not null)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Line numbers are not drawn.", where);
            }
            if (s.PageBorders is not null)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Page borders are not drawn.", where);
            }
            if (s.VerticalAlignment is D.SectionVerticalAlignment va && va != D.SectionVerticalAlignment.Top)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, $"Vertical page alignment ({va}) is not applied; text starts at the top.", where);
            }
            if (s.TextDirection is D.CellTextDirection td && td != D.CellTextDirection.LeftToRightTopToBottom)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Vertical section text direction is laid out horizontally.", where);
            }
            if (s.Grid?.Type is D.DocumentGridType gt && gt != D.DocumentGridType.Default)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "The East Asian document grid is not applied.", where);
            }
            return fs;
        }

        private PageNumberFormat PageFormat(D.NumberFormat? f)
        {
            switch (f)
            {
                case null:
                case D.NumberFormat.Decimal:
                    return PageNumberFormat.Decimal;
                case D.NumberFormat.UpperRoman:
                    return PageNumberFormat.UpperRoman;
                case D.NumberFormat.LowerRoman:
                    return PageNumberFormat.LowerRoman;
                case D.NumberFormat.UpperLetter:
                    return PageNumberFormat.UpperLetter;
                case D.NumberFormat.LowerLetter:
                    return PageNumberFormat.LowerLetter;
                default:
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, $"Page number format {f} is shown as decimal.");
                    return PageNumberFormat.Decimal;
            }
        }

        private HeaderFooterSet HeadersFooters(D.Section s, HeaderFooterSet? previous, bool oddEven, PageGeometry page)
        {
            double width = page.ContentWidth;
            _page = page;
            return new HeaderFooterSet
            {
                DefaultHeader = Story(s.Header, previous?.DefaultHeader, width),
                FirstPageHeader = Story(s.FirstPageHeader, previous?.FirstPageHeader, width),
                EvenPageHeader = Story(s.EvenPageHeader, previous?.EvenPageHeader, width),
                DefaultFooter = Story(s.Footer, previous?.DefaultFooter, width),
                FirstPageFooter = Story(s.FirstPageFooter, previous?.FirstPageFooter, width),
                EvenPageFooter = Story(s.EvenPageFooter, previous?.EvenPageFooter, width),
                DifferentFirstPage = s.DifferentFirstPage,
                DifferentOddEven = oddEven,
            };
        }

        /// <summary>
        /// One header or footer slot with Word's linking: content of its own; else, when linked, the previous section's
        /// slot of the same kind; else blank. Never null, so the engine does not inherit a slot a second time.
        /// </summary>
        private IList<FlowBlock> Story(D.HeaderFooterContent content, IList<FlowBlock>? previous, double width)
        {
            if (!HasContent(content))
            {
                return content.LinkToPrevious && previous is not null ? previous : new List<FlowBlock>();
            }
            if (_stories.TryGetValue(content, out IList<FlowBlock>? done))
            {
                return done;
            }
            List<FlowBlock> blocks = new List<FlowBlock>();
            _inStory = true;
            try
            {
                foreach (object b in content.Blocks)
                {
                    if (Block(b, null, 1, width) is FlowBlock fb)
                    {
                        blocks.Add(fb);
                    }
                }
            }
            finally
            {
                _inStory = false;
            }
            _stories[content] = blocks;
            return blocks;
        }

        private FlowWatermark Watermark(D.TextWatermark wm) => new FlowWatermark(wm.Text)
        {
            FontFamily = string.IsNullOrWhiteSpace(wm.Font) ? "Calibri" : wm.Font,
            Color = DocsTheme.TryHex(wm.ColorHex?.TrimStart('#'), out LayoutColor c) ? c : new LayoutColor(0xC0, 0xC0, 0xC0),
            Opacity = double.IsFinite(wm.Opacity) ? Math.Clamp(wm.Opacity, 0, 1) : 0.5,
            Diagonal = wm.Diagonal,
            Width = wm.WidthPt,
            Height = wm.HeightPt,
        };

        // ---- Blocks ----

        private FlowBlock? Block(object block, D.TableCellRef? cell, int depth, double width)
        {
            Shared.Check(nameof(LayoutLimits.MaxBlocks), _lo.Limits.MaxBlocks, ++_blocks);
            return block switch
            {
                D.Paragraph p => Paragraph(p, cell),
                D.DocTable t => Table(t, depth, width),
                _ => null,
            };
        }
    }
}
