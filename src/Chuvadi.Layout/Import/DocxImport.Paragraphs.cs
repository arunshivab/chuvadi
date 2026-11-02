using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using D = Chuvadi.Docs.Word;

namespace Chuvadi.Layout.Import
{
    internal sealed partial class DocxImport
    {
        // ---- Paragraphs ----

        private FlowParagraph? Paragraph(D.Paragraph p, D.TableCellRef? cell)
        {
            D.ParagraphFormat pf = cell is D.TableCellRef cr ? _doc.GetEffectiveParagraphFormat(p, cr) : _doc.GetEffectiveParagraphFormat(p);
            LayoutColor? outer = _behind;
            try
            {
                _behind = Shade(pf.Shading) ?? outer;
                return ParagraphCore(p, pf, cell);
            }
            finally
            {
                _behind = outer;
            }
        }

        private FlowParagraph? ParagraphCore(D.Paragraph p, D.ParagraphFormat pf, D.TableCellRef? cell)
        {
            D.TextFormat markEffective = Effective(p, new D.Run(string.Empty, pf.MarkFormat ?? D.TextFormat.None), cell);
            CharacterFormat mark = Character(markEffective);
            FlowParagraph fp = new FlowParagraph
            {
                Format = ParagraphFormatOf(p, pf),
                MarkFormat = mark,
            };
            bool allHidden = mark.Hidden;
            bool outerInCell = _inCell;
            _inCell = cell is not null;
            try
            {
                return ParagraphRuns(p, pf, cell, fp, mark, allHidden);
            }
            finally
            {
                _inCell = outerInCell;
            }
        }

        private FlowParagraph? ParagraphRuns(D.Paragraph p, D.ParagraphFormat pf, D.TableCellRef? cell, FlowParagraph fp, CharacterFormat mark, bool allHidden)
        {
            if (p.PageBreakBefore)
            {
                // A page break written as the paragraph's first run (Docs reads it into PageBreakBefore). It is a break
                // character, not "page break before": a paragraph holding only the break leaves its mark on the old page.
                fp.Inlines.Add(new BreakInline(BreakKind.Page, mark));
            }
            foreach (D.Run run in p.Runs)
            {
                D.TextFormat rf = Effective(p, run, cell);
                CharacterFormat cf = Character(rf);
                if (Inline(run, cf) is FlowInline inline)
                {
                    fp.Inlines.Add(inline);
                    allHidden &= cf.Hidden;
                }
            }
            if (allHidden)
            {
                // Hidden text including the paragraph mark: Word shows nothing for the paragraph.
                return null;
            }
            if (_labels.TryGetValue(p, out D.ListLabel? label) && label.Text.Length > 0)
            {
                fp.ListLabel = new ListLabel(label.Text, Character(label.Format), label.Suffix switch
                {
                    D.ListSuffix.Space => ListLabelSuffix.Space,
                    D.ListSuffix.Nothing => ListLabelSuffix.Nothing,
                    _ => ListLabelSuffix.Tab,
                });
                if (label.LevelDefinition.Alignment is D.ListLabelAlignment la && la is not (D.ListLabelAlignment.Left or D.ListLabelAlignment.Start))
                {
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, $"{la}-aligned list numbers are drawn left-aligned.");
                }
            }
            _converted[p] = fp;
            return fp;
        }

        private D.TextFormat Effective(D.Paragraph p, D.Run run, D.TableCellRef? cell)
            => cell is D.TableCellRef cr ? _doc.GetEffectiveRunFormat(p, run, cr) : _doc.GetEffectiveRunFormat(p, run);

        private FlowInline? Inline(D.Run run, CharacterFormat cf)
        {
            // Zero-width markers (bookmarks, field begin/separate/end, comment ranges), tracked deletions (the final
            // view), and comment anchors and marks (balloons are not drawn) take no room.
            if (run.IsMarker || run.IsDeleted || run.CommentReference is not null || run.IsAnnotationMark)
            {
                return null;
            }
            if (run.NoteReference is D.Note note)
            {
                return NoteReference(run, note, cf);
            }
            if (run.IsNoteMark)
            {
                return _noteMark is { Length: > 0 } mark ? new TextRun(mark, cf) : null;
            }
            if (run.Symbol is D.SymbolChar sym)
            {
                // A symbol character (w:sym): the code in the symbol's font, for every script slot.
                string font = string.IsNullOrEmpty(sym.Font) ? cf.FontFamily : sym.Font;
                return new TextRun(sym.Code.ToString(), cf with { FontFamily = font, HighAnsiFontFamily = font, EastAsianFontFamily = font, ComplexScriptFontFamily = font })
                {
                    HyperlinkUri = run.HyperlinkUrl,
                };
            }
            if (run.TextBox is D.TextBox box)
            {
                return TextBox(box, cf);
            }
            if (run.Image is D.ImageSpec img)
            {
                return _o.IncludeImages ? Image(img, cf) : null;
            }
            if (run.Break is D.BreakType bt)
            {
                return new BreakInline(bt switch
                {
                    D.BreakType.Page => BreakKind.Page,
                    D.BreakType.Column => BreakKind.Column,
                    _ => BreakKind.Line,
                }, cf);
            }
            if (run.PositionalTab is D.PositionalTab ptab)
            {
                return new TabInline(cf)
                {
                    Positional = new PositionalTab(
                        ptab.Alignment switch
                        {
                            D.PositionalTabAlignment.Center => TabAlignment.Center,
                            D.PositionalTabAlignment.Right => TabAlignment.Right,
                            _ => TabAlignment.Left,
                        },
                        ptab.RelativeTo == D.PositionalTabBase.Indent,
                        Leader(ptab.Leader)),
                };
            }
            if (run.IsTab)
            {
                return new TabInline(cf);
            }
            if (run.FieldInstruction is string instruction)
            {
                string name = FieldName(instruction);
                FieldKind kind = name switch
                {
                    "PAGE" => FieldKind.Page,
                    "NUMPAGES" => FieldKind.NumPages,
                    "SECTIONPAGES" => FieldKind.SectionPages,
                    _ => FieldKind.Other,
                };
                return new FieldInline(kind, cf, run.TextContent);
            }
            if (run.TextContent.Length == 0)
            {
                return null;
            }
            return new TextRun(run.TextContent, cf) { HyperlinkUri = run.HyperlinkUrl };
        }

        private static TabLeader Leader(D.TabLeader leader) => leader switch
        {
            D.TabLeader.Dot => TabLeader.Dot,
            D.TabLeader.Hyphen => TabLeader.Hyphen,
            D.TabLeader.Underscore => TabLeader.Underscore,
            D.TabLeader.Heavy => TabLeader.Heavy,
            D.TabLeader.MiddleDot => TabLeader.MiddleDot,
            _ => TabLeader.None,
        };

        private static string FieldName(string instruction)
        {
            ReadOnlySpan<char> s = instruction.AsSpan().Trim();
            int end = 0;
            while (end < s.Length && !char.IsWhiteSpace(s[end]) && s[end] != '\\')
            {
                end++;
            }
            return s[..end].ToString().ToUpperInvariant();
        }

        private ParagraphFormat ParagraphFormatOf(D.Paragraph p, D.ParagraphFormat f)
        {
            double firstLine = f.HangingIndentPt is double hanging ? -hanging : f.FirstLineIndentPt ?? 0;
            (LineSpacingRule rule, double spacing) = f.LineSpacing switch
            {
                { Rule: D.LineSpacingRule.Exactly } ls => (LineSpacingRule.Exact, ls.Value),
                { Rule: D.LineSpacingRule.AtLeast } ls => (LineSpacingRule.AtLeast, ls.Value),
                { } ls => (LineSpacingRule.Multiple, ls.Value),
                null => (LineSpacingRule.Multiple, 1.0),
            };
            List<TabStop> tabs = new List<TabStop>();
            foreach (D.TabStop t in f.TabStops)
            {
                if (t.Alignment == D.TabAlignment.Clear)
                {
                    continue;
                }
                tabs.Add(new TabStop(t.PositionPt, t.Alignment switch
                {
                    D.TabAlignment.Center => TabAlignment.Center,
                    D.TabAlignment.Right => TabAlignment.Right,
                    D.TabAlignment.Decimal => TabAlignment.Decimal,
                    D.TabAlignment.Bar => TabAlignment.Bar,
                    _ => TabAlignment.Left,
                }, Leader(t.Leader)));
            }
            if (f.Borders is { Between: not null } or { Bar: not null })
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Between and bar paragraph borders are not drawn.");
            }
            if (f.RightToLeft == true)
            {
                Warn(LayoutWarningCodes.BidiNotSupported, LayoutWarningSeverity.Fidelity, "Right-to-left paragraphs are laid out left-to-right.");
            }
            return new ParagraphFormat
            {
                Alignment = f.Alignment switch
                {
                    D.ParagraphAlignment.Center => ParagraphAlignment.Center,
                    D.ParagraphAlignment.Right => ParagraphAlignment.Right,
                    D.ParagraphAlignment.Justify or D.ParagraphAlignment.LowKashida or D.ParagraphAlignment.MediumKashida or D.ParagraphAlignment.HighKashida => ParagraphAlignment.Justify,
                    D.ParagraphAlignment.Distribute => ParagraphAlignment.Distribute,
                    _ => ParagraphAlignment.Left,
                },
                LeftIndent = f.IndentLeftPt ?? 0,
                RightIndent = f.IndentRightPt ?? 0,
                FirstLineIndent = firstLine,
                SpaceBefore = f.SpaceBeforeAuto == true ? _o.AutoParagraphSpacing : f.SpaceBeforePt ?? 0,
                SpaceAfter = f.SpaceAfterAuto == true ? _o.AutoParagraphSpacing : f.SpaceAfterPt ?? 0,
                ContextualSpacing = f.ContextualSpacing == true,
                LineSpacingRule = rule,
                LineSpacing = spacing,
                KeepWithNext = f.KeepWithNext == true,
                KeepLinesTogether = f.KeepLinesTogether == true,
                PageBreakBefore = f.StartOnNewPage == true,
                // Word lays paragraphs out with widow/orphan control unless a style or the paragraph turns it off.
                WidowControl = f.WidowControl ?? true,
                TabStops = tabs,
                Background = Shade(f.Shading),
                BorderTop = Border(f.Borders?.Top),
                BorderBottom = Border(f.Borders?.Bottom),
                BorderLeft = Border(f.Borders?.Left),
                BorderRight = Border(f.Borders?.Right),
                StyleId = p.StyleId,
            };
        }

        // ---- Character formatting ----

        private CharacterFormat Character(D.TextFormat f)
        {
            string? Theme(D.ThemeFont? tf) => tf is D.ThemeFont t ? _theme.Font(t) : null;
            string ascii = Theme(f.AsciiThemeFont) ?? f.AsciiFont ?? f.HighAnsiFont ?? "Times New Roman";
            string? highAnsi = Theme(f.HighAnsiThemeFont) ?? f.HighAnsiFont;
            string? eastAsia = Theme(f.EastAsiaThemeFont) ?? f.EastAsiaFont;
            string? complex = Theme(f.ComplexScriptThemeFont) ?? f.ComplexScriptFont;
            if (f.Outline == true || f.Shadow == true || f.Emboss == true || f.Imprint == true)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Outline, shadow, emboss and engrave text effects are drawn as plain text.");
            }
            LayoutColor? background = Highlight(f.Highlight) ?? Shade(f.Shading) ?? (f.ShadingHex is string sh && DocxTheme.TryHex(sh, out LayoutColor shc) ? shc : null);
            // Automatic colour ("auto" or none): black, or white over a dark background, as Word draws it.
            LayoutColor color = TextColor(f.ThemeColor, f.ThemeColorTint, f.ThemeColorShade, f.ColorHex)
                ?? (IsDark(background ?? _behind) ? LayoutColor.White : LayoutColor.Black);
            return new CharacterFormat
            {
                FontFamily = ascii,
                HighAnsiFontFamily = highAnsi == ascii ? null : highAnsi,
                EastAsianFontFamily = eastAsia == ascii ? null : eastAsia,
                ComplexScriptFontFamily = complex == ascii ? null : complex,
                FontSize = f.SizePt > 0 ? f.SizePt : 10,
                ComplexScriptFontSize = f.SizeComplexScriptPt > 0 ? f.SizeComplexScriptPt : null,
                Bold = f.Bold,
                Italic = f.Italic,
                ComplexScriptBold = f.BoldComplexScript,
                ComplexScriptItalic = f.ItalicComplexScript,
                Underline = Underline(f.EffectiveUnderline),
                UnderlineColor = f.UnderlineColorHex is string uc && DocxTheme.TryHex(uc, out LayoutColor ul) ? ul : null,
                Strikethrough = f.Strikethrough,
                DoubleStrikethrough = f.DoubleStrikethrough == true,
                Color = color,
                Background = background,
                Capitals = f.Caps == true ? TextCapitals.AllCaps : f.SmallCaps == true ? TextCapitals.SmallCaps : TextCapitals.None,
                VerticalAlignment = f.VerticalPosition switch
                {
                    D.VerticalTextPosition.Superscript => VerticalTextAlignment.Superscript,
                    D.VerticalTextPosition.Subscript => VerticalTextAlignment.Subscript,
                    _ => VerticalTextAlignment.Baseline,
                },
                CharacterSpacing = f.CharacterSpacingPt ?? 0,
                HorizontalScalePercent = f.HorizontalScalePercent is int w && w > 0 ? w : 100,
                KerningMinimumSize = f.KerningMinSizePt ?? 0,
                BaselineShift = f.BaselineShiftPt ?? 0,
                Hidden = f.Hidden == true && !_o.IncludeHiddenText,
                Language = f.Language ?? f.ComplexScriptLanguage,
            };
        }

        /// <summary>
        /// Whether automatic text over <paramref name="c"/> turns white: perceived brightness below half. (Word does not
        /// document its threshold; this one is subject to the acceptance corpus.)
        /// </summary>
        private static bool IsDark(LayoutColor? c)
            => c is LayoutColor v && v.A > 0 && ((0.299 * v.R) + (0.587 * v.G) + (0.114 * v.B)) < 128;

        private static UnderlineStyle Underline(D.UnderlineStyle? u) => u switch
        {
            null or D.UnderlineStyle.None => UnderlineStyle.None,
            D.UnderlineStyle.Single => UnderlineStyle.Single,
            D.UnderlineStyle.Words => UnderlineStyle.Words,
            D.UnderlineStyle.Double => UnderlineStyle.Double,
            D.UnderlineStyle.Thick => UnderlineStyle.Thick,
            D.UnderlineStyle.Dotted => UnderlineStyle.Dotted,
            D.UnderlineStyle.DottedHeavy => UnderlineStyle.DottedHeavy,
            D.UnderlineStyle.Dash => UnderlineStyle.Dash,
            D.UnderlineStyle.DashedHeavy => UnderlineStyle.DashHeavy,
            D.UnderlineStyle.DashLong => UnderlineStyle.DashLong,
            D.UnderlineStyle.DashLongHeavy => UnderlineStyle.DashLongHeavy,
            D.UnderlineStyle.DotDash => UnderlineStyle.DotDash,
            D.UnderlineStyle.DashDotHeavy => UnderlineStyle.DotDashHeavy,
            D.UnderlineStyle.DotDotDash => UnderlineStyle.DotDotDash,
            D.UnderlineStyle.DashDotDotHeavy => UnderlineStyle.DotDotDashHeavy,
            D.UnderlineStyle.Wave => UnderlineStyle.Wave,
            D.UnderlineStyle.WavyHeavy => UnderlineStyle.WavyHeavy,
            D.UnderlineStyle.WavyDouble => UnderlineStyle.WavyDouble,
            _ => UnderlineStyle.Single,
        };

        /// <summary>A text or line colour: the theme colour (with tint/shade) wins over the hex value; "auto" is null.</summary>
        private LayoutColor? TextColor(D.ThemeColor? theme, byte? tint, byte? shade, string? hex)
        {
            if (theme is D.ThemeColor t)
            {
                return _theme.Color(t, tint, shade);
            }
            return hex is not null && DocxTheme.TryHex(hex, out LayoutColor c) ? c : null;
        }

        private static LayoutColor? Highlight(string? name) => name switch
        {
            null or "none" => null,
            "yellow" => new LayoutColor(0xFF, 0xFF, 0x00),
            "green" => new LayoutColor(0x00, 0xFF, 0x00),
            "cyan" => new LayoutColor(0x00, 0xFF, 0xFF),
            "magenta" => new LayoutColor(0xFF, 0x00, 0xFF),
            "blue" => new LayoutColor(0x00, 0x00, 0xFF),
            "red" => new LayoutColor(0xFF, 0x00, 0x00),
            "darkBlue" => new LayoutColor(0x00, 0x00, 0x80),
            "darkCyan" => new LayoutColor(0x00, 0x80, 0x80),
            "darkGreen" => new LayoutColor(0x00, 0x80, 0x00),
            "darkMagenta" => new LayoutColor(0x80, 0x00, 0x80),
            "darkRed" => new LayoutColor(0x80, 0x00, 0x00),
            "darkYellow" => new LayoutColor(0x80, 0x80, 0x00),
            "darkGray" => new LayoutColor(0x80, 0x80, 0x80),
            "lightGray" => new LayoutColor(0xC0, 0xC0, 0xC0),
            "black" => LayoutColor.Black,
            "white" => LayoutColor.White,
            _ => null,
        };

        /// <summary>
        /// The colour a shading paints: the fill for a clear pattern, the pattern colour for a solid one, and a blend
        /// for percentage patterns. Stripes and crosses are drawn as a blend too (FillApproximated).
        /// </summary>
        private LayoutColor? Shade(D.Shading? s)
        {
            if (s is null || s.Pattern == D.ShadingPattern.Nil)
            {
                return null;
            }
            LayoutColor? fill = s.ThemeFill is D.ThemeColor tf ? _theme.Color(tf) : s.FillHex is string fh && DocxTheme.TryHex(fh, out LayoutColor f) ? f : null;
            LayoutColor pattern = s.ColorHex is string ch && DocxTheme.TryHex(ch, out LayoutColor pc) ? pc : LayoutColor.Black;
            double share;
            switch (s.Pattern)
            {
                case D.ShadingPattern.Clear:
                    return fill;
                case D.ShadingPattern.Solid:
                    return pattern;
                default:
                    string name = s.Pattern.ToString();
                    if (name.StartsWith("Percent", StringComparison.Ordinal) && int.TryParse(name.AsSpan(7), out int pct))
                    {
                        share = pct / 100.0;
                    }
                    else
                    {
                        share = name.StartsWith("Thin", StringComparison.Ordinal) ? 0.25 : 0.5;
                        Warn(LayoutWarningCodes.FillApproximated, LayoutWarningSeverity.Fidelity, "Striped and cross-hatched shading is drawn as one blended colour.");
                    }
                    break;
            }
            LayoutColor back = fill ?? LayoutColor.White;
            return new LayoutColor(
                (byte)Math.Round((back.R * (1 - share)) + (pattern.R * share)),
                (byte)Math.Round((back.G * (1 - share)) + (pattern.G * share)),
                (byte)Math.Round((back.B * (1 - share)) + (pattern.B * share)));
        }

        private BorderLine Border(D.BorderSpec? b)
        {
            if (b is null || b.Style is D.BorderStyle.None or D.BorderStyle.Nil || !(b.WidthPt > 0))
            {
                return BorderLine.None;
            }
            LayoutColor color = TextColor(b.ThemeColor, null, null, b.ColorHex) ?? LayoutColor.Black;
            LineDash dash = b.Style switch
            {
                D.BorderStyle.Dotted => LineDash.Dot,
                D.BorderStyle.Dashed or D.BorderStyle.DashSmallGap => LineDash.Dash,
                D.BorderStyle.DotDash or D.BorderStyle.DashDotStroked => LineDash.DashDot,
                D.BorderStyle.DotDotDash => LineDash.DashDotDot,
                _ => LineDash.Solid,
            };
            bool dbl = b.Style is D.BorderStyle.Double or D.BorderStyle.Triple or D.BorderStyle.DoubleWave
                or D.BorderStyle.ThinThickSmallGap or D.BorderStyle.ThickThinSmallGap or D.BorderStyle.ThinThickThinSmallGap
                or D.BorderStyle.ThinThickMediumGap or D.BorderStyle.ThickThinMediumGap or D.BorderStyle.ThinThickThinMediumGap
                or D.BorderStyle.ThinThickLargeGap or D.BorderStyle.ThickThinLargeGap or D.BorderStyle.ThinThickThinLargeGap;
            if (b.RawStyleToken is not null)
            {
                Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, "Art borders are drawn as single lines.");
            }
            return new BorderLine(b.WidthPt, color, dash, b.SpacePt, dbl);
        }

        /// <summary>The paragraph mark of an empty table cell: the document default run formatting.</summary>
        private CharacterFormat DefaultMark()
        {
            if (_defaultMark is null)
            {
                D.Paragraph probe = new D.Paragraph();
                _defaultMark = Character(_doc.GetEffectiveRunFormat(probe, new D.Run(string.Empty, D.TextFormat.None)));
            }
            return _defaultMark;
        }
    }
}
