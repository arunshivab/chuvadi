using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using D = Chuvadi.Docs.Word;

namespace Chuvadi.Layout.Docs
{
    internal sealed partial class DocsImport
    {
        // ---- Footnotes and endnotes (Chuvadi.Docs 1.6.0) ----

        /// <summary>The label of each reference run (doc.GetNoteReferences(): number in the note format, or the custom mark).</summary>
        private Dictionary<D.Run, string>? _noteLabels;

        private readonly Dictionary<D.Note, FlowNote> _notes = new Dictionary<D.Note, FlowNote>(ReferenceEqualityComparer.Instance);
        private readonly List<FlowNote> _endnotes = new List<FlowNote>();

        /// <summary>While a note's content is converted: the label its own mark (w:footnoteRef / w:endnoteRef) draws.</summary>
        private string? _noteMark;

        /// <summary>A paragraph in a table cell is being converted.</summary>
        private bool _inCell;

        /// <summary>A header or footer is being converted.</summary>
        private bool _inStory;

        private void ReadNoteReferences()
        {
            _noteLabels = new Dictionary<D.Run, string>(ReferenceEqualityComparer.Instance);
            foreach (D.NoteReferenceInfo info in _doc.GetNoteReferences())
            {
                if (info.RunIndex >= 0 && info.RunIndex < info.Paragraph.Runs.Count)
                {
                    D.Run run = info.Paragraph.Runs[info.RunIndex];
                    // A custom mark is the text of the run that follows: that run is drawn as it is.
                    _noteLabels[run] = run.CustomMarkFollows ? string.Empty : info.Label;
                }
                if (info.RestartsEachPage)
                {
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity,
                        "Footnote numbering that restarts on each page is numbered through the section.");
                }
            }
        }

        /// <summary>
        /// A footnote or endnote reference: its label in the reference format, and the note's content (converted once).
        /// Footnotes go to the foot of the page holding the reference; endnotes to the end of the document.
        /// </summary>
        private FlowInline NoteReference(D.Run run, D.Note note, CharacterFormat cf)
        {
            if (_noteLabels is null)
            {
                ReadNoteReferences();
            }
            string label = _noteLabels!.TryGetValue(run, out string? l) ? l : string.Empty;
            if ((_inCell || _inStory) && note.Kind == D.NoteKind.Footnote)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss,
                    "Footnotes referenced in a table cell or a header/footer show their number, but their text is not laid out.");
            }
            if (!_notes.TryGetValue(note, out FlowNote? flow))
            {
                flow = new FlowNote(note.Kind == D.NoteKind.Endnote ? FlowNoteKind.Endnote : FlowNoteKind.Footnote);
                _notes[note] = flow;
                string? outer = _noteMark;
                PageGeometry page = _page;
                try
                {
                    _noteMark = label;
                    foreach (object b in note.Blocks)
                    {
                        if (Block(b, null, 1, page.ContentWidth) is FlowBlock fb)
                        {
                            flow.Blocks.Add(fb);
                        }
                    }
                }
                finally
                {
                    _noteMark = outer;
                    _page = page;
                }
                if (flow.Kind == FlowNoteKind.Endnote)
                {
                    _endnotes.Add(flow);
                }
            }
            return new NoteReferenceInline(label, flow, cf);
        }

        /// <summary>
        /// Endnotes at the end of the document, in reference order, under a two-inch separator line (Word's default
        /// position "end of document"; "end of section" is laid out the same way).
        /// </summary>
        private void AppendEndnotes(FlowDocument flow)
        {
            if (_endnotes.Count == 0)
            {
                return;
            }
            if (_doc.EndnoteProperties.Position is D.NotePosition pos && pos != D.NotePosition.DocumentEnd)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, $"Endnotes positioned at {pos} are laid out at the end of the document.");
            }
            FlowSection last = flow.Sections[^1];
            CharacterFormat mark = _endnotes[0].Blocks.Count > 0 && _endnotes[0].Blocks[0] is FlowParagraph first ? first.MarkFormat : CharacterFormat.Default;
            last.Blocks.Add(new FlowParagraph
            {
                // The separator: an empty paragraph whose top border, cut to two inches by its right indent, sits half
                // a line below the text; it stays with the first endnote.
                Format = new ParagraphFormat
                {
                    BorderTop = new BorderLine(0.5, LayoutColor.Black),
                    RightIndent = Math.Max(0, last.Page.ContentWidth - 144),
                    SpaceBefore = mark.FontSize / 2,
                    KeepWithNext = true,
                },
                MarkFormat = mark,
            });
            foreach (FlowNote note in _endnotes)
            {
                foreach (FlowBlock b in note.Blocks)
                {
                    last.Blocks.Add(b);
                }
            }
        }

        // ---- Text boxes (Chuvadi.Docs 1.6.0) ----

        /// <summary>
        /// A text box run: its blocks inside the insets (null = Word's 7.2 / 3.6 pt), its fill and outline, inline at its
        /// run or floating with the same position rules as a picture. Rotation is not applied (a warning says so).
        /// </summary>
        private FlowInline? TextBox(D.TextBox box, CharacterFormat cf)
        {
            PageGeometry page = _page;
            double width = RelativeSize(box.RelativeWidth, true) ?? box.WidthPt;
            double height = RelativeSize(box.RelativeHeight, false) ?? box.HeightPt;
            if (!(width > 0) || !(height > 0))
            {
                return null;
            }
            if (box.RotationDegrees % 360 != 0)
            {
                Warn(LayoutWarningCodes.DrawingApproximated, LayoutWarningSeverity.Fidelity, "Rotated text boxes are drawn upright.");
            }
            D.ImageEffectExtent? insets = box.Insets;
            FlowTextBox flowBox = new FlowTextBox
            {
                InsetLeft = insets?.LeftPt ?? 7.2,
                InsetTop = insets?.TopPt ?? 3.6,
                InsetRight = insets?.RightPt ?? 7.2,
                InsetBottom = insets?.BottomPt ?? 3.6,
                VerticalAlignment = box.VerticalAnchor switch
                {
                    D.TextBoxVerticalAnchor.Center => TextBoxVerticalAlignment.Center,
                    D.TextBoxVerticalAnchor.Bottom => TextBoxVerticalAlignment.Bottom,
                    _ => TextBoxVerticalAlignment.Top,
                },
                AutoFit = box.AutoFit,
                Fill = DocsTheme.TryHex(box.FillHex?.TrimStart('#'), out LayoutColor fill) ? fill : null,
                Line = DocsTheme.TryHex(box.LineHex?.TrimStart('#'), out LayoutColor line) ? line : null,
                LineWidth = box.LineWidthPt ?? 0.75,
            };
            double inner = Math.Max(1, width - flowBox.InsetLeft - flowBox.InsetRight);
            LayoutColor? outer = _behind;
            try
            {
                _behind = flowBox.Fill ?? outer;
                foreach (object b in box.Blocks)
                {
                    if (Block(b, null, 1, inner) is FlowBlock fb)
                    {
                        flowBox.Blocks.Add(fb);
                    }
                }
            }
            finally
            {
                _behind = outer;
                _page = page;
            }
            if (box.Placement == D.ImagePlacement.Inline || box.Position is null)
            {
                return new TextBoxInline(flowBox, width, height, cf);
            }
            Geometry g = Floating(box.Position, width, height, "Text boxes");
            return new AnchoredTextBoxInline(flowBox, width, height, cf)
            {
                HorizontalRelativeTo = g.H,
                VerticalRelativeTo = g.V,
                OffsetX = g.X,
                OffsetY = g.Y,
                HorizontalAlignment = g.HAlign,
                VerticalAlignment = g.VAlign,
                Wrap = g.Wrap,
                WrapSide = g.Side,
                WrapPolygon = g.Polygon,
                DistanceTop = g.Top,
                DistanceBottom = g.Bottom,
                DistanceLeft = g.Left,
                DistanceRight = g.Right,
                Description = box.AltText,
            };
        }

        /// <summary>A relative size (percent of the page, margins or a margin), or null when not given.</summary>
        private double? RelativeSize(D.RelativeSize? size, bool horizontal)
        {
            if (size is not D.RelativeSize rs || !(rs.Percent > 0))
            {
                return null;
            }
            PageGeometry page = _page;
            double frame = rs.RelativeTo switch
            {
                D.RelativeSizeFrame.Page => horizontal ? page.Width : page.Height,
                D.RelativeSizeFrame.LeftMargin or D.RelativeSizeFrame.InsideMargin => page.MarginLeft,
                D.RelativeSizeFrame.RightMargin or D.RelativeSizeFrame.OutsideMargin => page.MarginRight,
                D.RelativeSizeFrame.TopMargin => page.MarginTop,
                D.RelativeSizeFrame.BottomMargin => page.MarginBottom,
                _ => horizontal ? page.ContentWidth : Math.Max(1, page.Height - page.MarginTop - page.MarginBottom),
            };
            return frame * rs.Percent / 100;
        }

        // ---- Embedded fonts (Chuvadi.Docs 1.6.0) ----

        /// <summary>
        /// The fonts embedded in the document (w:embedRegular/Bold/Italic/BoldItalic, already de-obfuscated by Chuvadi.Docs),
        /// as a font source searched before all others when <see cref="LayoutOptions.UseEmbeddedFonts"/> is on. Null when
        /// the document embeds none. A font Layout cannot read is skipped with a warning.
        /// </summary>
        internal static MemoryFontSource? EmbeddedFonts(D.Document doc, Action<LayoutWarning> warn)
        {
            MemoryFontSource? source = null;
            foreach (D.FontInfo font in doc.Fonts)
            {
                foreach (D.EmbeddedFont e in font.Embedded)
                {
                    if (e.Data is not { Length: > 0 } data)
                    {
                        continue;
                    }
                    try
                    {
                        (source ??= new MemoryFontSource("Embedded fonts", FontOrigin.Embedded)).Add(data, font.Name + " (" + e.Style + ")");
                    }
                    catch (FontFormatException ex)
                    {
                        warn(new LayoutWarning(LayoutWarningCodes.EmbeddedFontUnreadable, LayoutWarningSeverity.Fidelity,
                            $"The embedded font {font.Name} ({e.Style}) could not be read: {ex.Message}", font.Name));
                    }
                }
            }
            return source;
        }
    }
}
