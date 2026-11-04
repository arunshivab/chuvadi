using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using Chuvadi.Sheets.Model;
using SheetsBorder = Chuvadi.Sheets.Model.Border;
using SheetsUnderline = Chuvadi.Sheets.Model.UnderlineStyle;
using LayoutUnderline = Chuvadi.Layout.Model.UnderlineStyle;

namespace Chuvadi.Layout.Sheets
{
    /// <summary>Settings for <see cref="SheetsImporter"/>.</summary>
    public sealed record SheetsImportOptions
    {
        /// <summary>Sheets to import, by name, in this order. Null imports every sheet in workbook order.</summary>
        public IReadOnlyList<string>? SheetNames { get; init; }

        /// <summary>Import hidden and very hidden sheets too (Excel never prints them). Default false.</summary>
        public bool IncludeHiddenSheets { get; init; }

        /// <summary>
        /// Paper size code (ECMA-376 ST_PaperSize: 1 Letter, 9 A4, ...) for sheets whose file states none. Null (the
        /// default) follows <see cref="LayoutOptions.Culture"/>'s region, like a Windows printer default (D-30): Letter
        /// in the Letter countries, A4 elsewhere (see <see cref="PaperDefaults"/>).
        /// </summary>
        public int? DefaultPaperSizeCode { get; init; }

        /// <summary>Apply conditional formats (fills, fonts, borders, number formats, data bars). Default true.</summary>
        public bool ApplyConditionalFormats { get; init; } = true;

        /// <summary>"Today" for time-period conditional formats. Null uses <see cref="LayoutOptions.Now"/>, then the clock.</summary>
        public DateTime? Today { get; init; }

        /// <summary>Culture for display text (number formats). Null uses <see cref="LayoutOptions.Culture"/>.</summary>
        public CultureInfo? DisplayCulture { get; init; }

        /// <summary>Load picture bytes (pictures are drawn). False draws placeholders and reads less. Default true.</summary>
        public bool LoadImages { get; init; } = true;

        /// <summary>Honour the "black and white" print setting: no fills, text and borders in black. Default true.</summary>
        public bool HonourBlackAndWhite { get; init; } = true;

        /// <summary>Honour "draft quality": no gridlines, pictures, shapes or charts. Default true.</summary>
        public bool HonourDraft { get; init; } = true;

        /// <summary>Cap on conditional-format evaluations per sheet (cells × ranges); beyond it, formats are not applied and a warning says so.</summary>
        public long MaxConditionalFormatCells { get; init; } = 2_000_000;
    }

    /// <summary>
    /// Builds the grid IR from an .xlsx workbook read by Chuvadi.Sheets, the way Excel prints it: the displayed text
    /// of each cell (Chuvadi.Sheets renders the number format; values are never recalculated or altered), the
    /// effective style (cell, then row, then column), merges, column widths and row heights, page setup, print
    /// areas and titles, manual page breaks, conditional formats, pictures, shapes and chart placeholders.
    /// </summary>
    public static class SheetsImporter
    {
        private static readonly HashSet<string> ErrorLiterals = new HashSet<string>(StringComparer.Ordinal)
        {
            "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A", "#GETTING_DATA", "#SPILL!", "#CALC!", "#FIELD!", "#BLOCKED!", "#CONNECT!", "#BUSY!", "#UNKNOWN!",
        };

        /// <summary>Reads an .xlsx (or an encrypted one, with <see cref="LayoutOptions.Password"/>) and builds the grid IR.</summary>
        /// <exception cref="LayoutFormatException">The data is not a readable workbook, or the password is missing or wrong.</exception>
        /// <exception cref="LayoutLimitExceededException">The workbook exceeds a <see cref="LayoutLimits"/> limit.</exception>
        public static GridWorkbook Import(Stream source, SheetsImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            layoutOptions ??= new LayoutOptions();
            options ??= new SheetsImportOptions();
            Workbook wb = Load(source, options, layoutOptions, cancellationToken);
            return Import(wb, options, layoutOptions, warning, cancellationToken);
        }

        /// <summary>Loads a workbook with Chuvadi.Sheets under <paramref name="layoutOptions"/>' limits, mapping its errors to Layout's.</summary>
        internal static Workbook Load(Stream source, SheetsImportOptions? options, LayoutOptions layoutOptions, CancellationToken cancellationToken)
        {
            options ??= new SheetsImportOptions();
            LayoutLimits limits = layoutOptions.Limits;
            XlsxReaderOptions ro = new XlsxReaderOptions
            {
                IncludeStyledEmptyCells = true,
                LoadImageData = options.LoadImages,
                CancellationToken = cancellationToken,
                MaxPartBytes = limits.MaxSourceBytes,
                MaxTotalUncompressedBytes = limits.MaxLayoutBytes,
                MaxCompressionRatio = 1000,
                MaxPackageEntries = 50_000,
                MaxRowsPerSheet = 1_048_576,
                DisplayCulture = options.DisplayCulture ?? layoutOptions.Culture,
            };
            Workbook wb;
            try
            {
                wb = Workbook.Load(source, layoutOptions.Password, ro);
            }
            catch (XlsxPasswordRequiredException ex)
            {
                throw new LayoutFormatException("The workbook is encrypted: " + ex.Message + " Set LayoutOptions.Password.", ex);
            }
            catch (XlsxLimitExceededException ex)
            {
                throw new LayoutLimitExceededException(ex.LimitName, (long)ex.Limit, (long)ex.Actual);
            }
            catch (Exception ex) when (ex is XlsxFormatException or InvalidDataException or System.Xml.XmlException or Chuvadi.Sheets.Zip.ZipFormatException)
            {
                throw new LayoutFormatException("The workbook could not be read: " + ex.Message, ex);
            }
            return wb;
        }

        /// <summary>Builds the grid IR from a workbook already loaded with Chuvadi.Sheets (load it with <c>IncludeStyledEmptyCells = true</c> so that formatted empty cells print).</summary>
        public static GridWorkbook Import(Workbook workbook, SheetsImportOptions? options = null, LayoutOptions? layoutOptions = null,
            Action<LayoutWarning>? warning = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            options ??= new SheetsImportOptions();
            layoutOptions ??= new LayoutOptions();
            Importer imp = new Importer(workbook, options, layoutOptions, warning ?? (_ => { }), cancellationToken);
            return imp.Run();
        }

        private sealed class Importer
        {
            private readonly Workbook _wb;
            private readonly SheetsImportOptions _o;
            private readonly LayoutOptions _lo;
            private readonly Action<LayoutWarning> _warn;
            private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
            private readonly CancellationToken _ct;
            private readonly NumberFormatOptions _nf;
            private readonly Dictionary<CellStyle, GridCellStyle> _styleCache = new Dictionary<CellStyle, GridCellStyle>();
            private CharacterFormat _normal = new CharacterFormat { FontFamily = "Calibri", FontSize = 11 };
            private bool _blackAndWhite;
            private bool _showFormulas;
            private Sheet? _sheet;
            private bool _hyperlinks;
            private string _sheetName = string.Empty;

            public Importer(Workbook wb, SheetsImportOptions o, LayoutOptions lo, Action<LayoutWarning> warn, CancellationToken ct)
            {
                _wb = wb;
                _o = o;
                _lo = lo;
                _warn = warn;
                _ct = ct;
                _nf = new NumberFormatOptions { Culture = o.DisplayCulture ?? lo.Culture, Date1904 = wb.Date1904 };
            }

            public GridWorkbook Run()
            {
                GridWorkbook book = new GridWorkbook();
                _normal = NormalFont();
                List<Sheet> sheets = new List<Sheet>();
                if (_o.SheetNames is IReadOnlyList<string> names)
                {
                    foreach (string n in names)
                    {
                        Sheet? found = null;
                        foreach (Sheet sh in _wb.Sheets)
                        {
                            if (string.Equals(sh.Name, n, StringComparison.OrdinalIgnoreCase))
                            {
                                found = sh;
                                break;
                            }
                        }
                        sheets.Add(found ?? throw new ArgumentException($"The workbook has no sheet named '{n}'.", nameof(SheetsImportOptions.SheetNames)));
                    }
                }
                else
                {
                    sheets.AddRange(_wb.Sheets);
                }
                foreach (LoadWarning lw in _wb.LoadWarnings)
                {
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss, lw.Message, lw.SheetName);
                }

                // Chart sheets print one page each, in tab order among the worksheets (their WorkbookIndex).
                Dictionary<int, ChartSheet> charts = new Dictionary<int, ChartSheet>();
                foreach (ChartSheet cs in _wb.ChartSheets)
                {
                    if (_o.SheetNames is null || System.Linq.Enumerable.Contains(_o.SheetNames, cs.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        charts[cs.WorkbookIndex] = cs;
                    }
                }
                int tab = 0;
                foreach (Sheet sh in sheets)
                {
                    _ct.ThrowIfCancellationRequested();
                    // Skip tab positions taken by chart sheets (printing them there) until this worksheet's position.
                    while (charts.ContainsKey(tab))
                    {
                        ChartSheet cs = charts[tab];
                        charts.Remove(tab);
                        if (cs.Visibility == SheetVisibility.Visible || _o.IncludeHiddenSheets)
                        {
                            book.Sheets.Add(ImportChartSheet(cs));
                        }
                        tab++;
                    }
                    tab++;
                    if (sh.Visibility != SheetVisibility.Visible && !_o.IncludeHiddenSheets)
                    {
                        continue;
                    }
                    book.Sheets.Add(ImportSheet(sh));
                    if (sh.PageSetup.CellComments == PrintCellComments.AtEnd && sh.Comments.Count > 0)
                    {
                        book.Sheets.Add(CommentsAtEnd(sh, book.Sheets[^1]));
                    }
                }
                foreach (int index in new SortedSet<int>(charts.Keys))
                {
                    ChartSheet cs = charts[index];
                    if (cs.Visibility == SheetVisibility.Visible || _o.IncludeHiddenSheets)
                    {
                        book.Sheets.Add(ImportChartSheet(cs));
                    }
                }
                return book;
            }

            /// <summary>A chart sheet: one page with the chart (drawn as a labelled placeholder) filling the printable area.</summary>
            private GridSheet ImportChartSheet(ChartSheet cs)
            {
                _sheetName = cs.Name;
                GridSheet g = new GridSheet(cs.Name) { NormalFont = _normal };
                GridPageSetup setup = PageSetupOf(cs.PageSetup, false) with { FitToPage = true, FitToWidth = 1, FitToHeight = 1, PrintGridlines = false, PrintHeadings = false };
                g.PageSetup = setup;
                double w = Math.Max(36, setup.PageWidth - setup.MarginLeft - setup.MarginRight);
                double h = Math.Max(36, setup.PageHeight - setup.MarginTop - setup.MarginBottom);
                g.Drawings.Add(new GridDrawing(GridDrawingKind.Chart) { X = 0, Y = 0, Width = w, Height = h, Name = cs.Name, Text = cs.Chart?.Title ?? cs.Name });
                return g;
            }

            /// <summary>
            /// "Print comments at end of sheet": after the sheet's pages, one entry per comment, "Cell: A1" then
            /// "Comment: text", with the sheet's page setup (no print area, titles, gridlines or headings).
            /// </summary>
            private GridSheet CommentsAtEnd(Sheet sh, GridSheet printed)
            {
                GridSheet g = new GridSheet(sh.Name) { NormalFont = _normal, DefaultRowHeight = printed.DefaultRowHeight, BaseColumnWidth = printed.BaseColumnWidth };
                g.PageSetup = printed.PageSetup with { PrintGridlines = false, PrintHeadings = false, FitToPage = false, Scale = 100, CenterHorizontally = false, CenterVertically = false };
                // One column as wide as the printable area (character units at the Normal font's ~7 px digit, as Calibri 11).
                double printable = g.PageSetup.PageWidth - g.PageSetup.MarginLeft - g.PageSetup.MarginRight;
                g.Columns[0] = new GridColumn(Math.Max(10, ((printable / 0.75) - 12) / 7));
                GridCellStyle style = new GridCellStyle { Font = _normal, WrapText = true, VerticalAlignment = CellVerticalAlignmentKind.Top };
                int row = 0;
                foreach (CellComment c in sh.Comments)
                {
                    g.SetCell(row, 0, new GridCell("Cell: " + c.Reference + "\nComment: " + c.Text, CellValueKind.Text, style));
                    row += 2;
                }
                return g;
            }

            private void Warn(string code, LayoutWarningSeverity severity, string message, string? location = null)
            {
                if (_warned.Add(code + "\u0000" + (location ?? _sheetName)))
                {
                    _warn(new LayoutWarning(code, severity, message, location ?? _sheetName));
                }
            }

            private CharacterFormat NormalFont()
            {
                CellStyleTable st = _wb.StyleTable;
                int fontId = st.CellStyleFormats.Count > 0 ? st.CellStyleFormats[0].FontId : 0;
                FontEntry? f = fontId >= 0 && fontId < st.Fonts.Count ? st.Fonts[fontId] : (st.Fonts.Count > 0 ? st.Fonts[0] : null);
                string? name = f?.Name;
                if (name is null && f is not null && f.Scheme != FontScheme.None)
                {
                    name = f.Scheme == FontScheme.Major ? st.Theme.MajorFont.Latin : st.Theme.MinorFont.Latin;
                }
                return new CharacterFormat
                {
                    FontFamily = string.IsNullOrWhiteSpace(name) ? "Calibri" : name,
                    FontSize = f?.Size is double size && size > 0 ? size : 11,
                };
            }

            private GridSheet ImportSheet(Sheet sh)
            {
                _sheetName = sh.Name;
                _sheet = sh;
                _hyperlinks = sh.Hyperlinks.Count > 0;
                GridSheet g = new GridSheet(sh.Name)
                {
                    Hidden = false,
                    NormalFont = _normal,
                    DefaultRowHeight = sh.Format.DefaultRowHeight > 0 ? sh.Format.DefaultRowHeight : 15,
                    DefaultColumnWidth = sh.Format.DefaultColumnWidth is double dw && dw > 0 ? dw : null,
                    BaseColumnWidth = sh.Format.BaseColumnWidth,
                };
                PageSetup ps = sh.PageSetup;
                _blackAndWhite = ps.BlackAndWhite && _o.HonourBlackAndWhite;
                bool draft = ps.Draft && _o.HonourDraft;
                g.RightToLeft = sh.View.RightToLeft;
                g.RowsHiddenByDefault = sh.Format.ZeroHeight;
                _showFormulas = sh.View.ShowFormulas;

                // Columns (1-based spans in the file) and rows.
                foreach (ColumnRange cr in sh.ColumnRanges)
                {
                    if (cr.Width <= 0 && !cr.Hidden)
                    {
                        continue;
                    }
                    int last = Math.Min(cr.Max, 16384);
                    for (int c = Math.Max(1, cr.Min); c <= last; c++)
                    {
                        g.Columns[c - 1] = new GridColumn(cr.Width > 0 ? cr.Width : 0, cr.Hidden);
                    }
                }
                foreach (Row r in sh.DefinedRows)
                {
                    // With zeroHeight every row in the file says whether it is shown, so all of them are recorded.
                    if (r.Height > 0 || r.Hidden || g.RowsHiddenByDefault)
                    {
                        g.Rows[r.Number - 1] = new GridRow(r.Height > 0 ? r.Height : null, r.Hidden);
                    }
                }
                foreach (string m in sh.MergeRanges)
                {
                    if (TryRange(m, out CellRange range))
                    {
                        g.MergedRanges.Add(range);
                    }
                }

                // Cells.
                bool showZeros = sh.View.ShowZeros;
                int maxRow = -1, maxCol = -1, minRow = int.MaxValue, minCol = int.MaxValue;
                long count = 0;
                foreach (Cell cell in sh.PopulatedCells)
                {
                    if ((++count & 0x3FF) == 0)
                    {
                        _ct.ThrowIfCancellationRequested();
                    }
                    Shared.Check(nameof(LayoutLimits.MaxCellsPerSheet), _lo.Limits.MaxCellsPerSheet, count);
                    if (cell.Row is < 1 or > 1048576 || cell.Column is < 1 or > 16384)
                    {
                        Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss, "A cell outside Excel's grid was skipped.");
                        continue;
                    }
                    CellStyle style = sh.GetEffectiveStyle(cell.Row, cell.Column) ?? cell.Style ?? default;
                    GridCell? gc = ImportCell(cell, style, showZeros, ps.Errors, null);
                    if (gc is null)
                    {
                        continue;
                    }
                    g.SetCell(cell.Row - 1, cell.Column - 1, gc);
                    if (g.RowsHiddenByDefault && !g.Rows.ContainsKey(cell.Row - 1))
                    {
                        // A row element in the file without hidden="1" is shown even when rows are hidden by default.
                        g.Rows[cell.Row - 1] = new GridRow(null, false);
                    }
                    maxRow = Math.Max(maxRow, cell.Row - 1);
                    maxCol = Math.Max(maxCol, cell.Column - 1);
                    minRow = Math.Min(minRow, cell.Row - 1);
                    minCol = Math.Min(minCol, cell.Column - 1);
                }
                if (_o.ApplyConditionalFormats && sh.ConditionalFormats.Count > 0 && maxRow >= 0)
                {
                    ApplyConditionalFormats(sh, g, new CellRange(minRow, minCol, maxRow, maxCol), showZeros, ps.Errors);
                }

                g.PageSetup = PageSetupOf(ps, draft);
                if (!draft && sh.HeaderFooterPictures.Count > 0)
                {
                    Dictionary<string, GridHeaderFooterPicture> pictures = new Dictionary<string, GridHeaderFooterPicture>(StringComparer.OrdinalIgnoreCase);
                    foreach (HeaderFooterPicture pic in sh.HeaderFooterPictures)
                    {
                        if (pic.GetImageBytes() is { Length: > 0 } bytes)
                        {
                            Shared.Check(nameof(LayoutLimits.MaxResourceBytes), _lo.Limits.MaxResourceBytes, bytes.LongLength);
                            pictures[pic.Id] = new GridHeaderFooterPicture(LayoutImage.FromEncoded(bytes), pic.WidthPoints, pic.HeightPoints);
                        }
                    }
                    g.PageSetup = g.PageSetup with { Pictures = pictures };
                }
                if (ps.CellComments == PrintCellComments.AsDisplayed && !draft)
                {
                    // Comments that are always shown print in their boxes, like a text box over the cells.
                    foreach (CellComment c in sh.Comments)
                    {
                        if (c.Visible && c.Anchor is CommentAnchor a)
                        {
                            g.Drawings.Add(new GridDrawing(GridDrawingKind.Shape)
                            {
                                From = new GridAnchorPoint(a.TopRow - 1, a.LeftColumn - 1, a.LeftOffset * 0.75, a.TopOffset * 0.75),
                                To = new GridAnchorPoint(a.BottomRow - 1, a.RightColumn - 1, a.RightOffset * 0.75, a.BottomOffset * 0.75),
                                Geometry = "rect",
                                Fill = _blackAndWhite ? null : new LayoutColor(0xFF, 0xFF, 0xE1),
                                Line = LayoutColor.Black,
                                LineWidth = 0.75,
                                Text = c.Text,
                                Name = "Comment " + c.Reference,
                            });
                        }
                    }
                }
                foreach (string area in ps.PrintAreas)
                {
                    if (TryArea(area, maxRow, maxCol, out CellRange pa))
                    {
                        g.PrintAreas.Add(pa);
                    }
                }
                if (ps.PrintAreas.Count == 0 && ps.PrintAreaFormula is string paf)
                {
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, $"The print area '{paf}' could not be read; the used range is printed.");
                }
                if (ps.PrintTitleRows is (int tr0, int tr1))
                {
                    g.PrintTitleRows = (tr0 - 1, tr1 - 1);
                }
                if (ps.PrintTitleColumns is (int tc0, int tc1))
                {
                    g.PrintTitleColumns = (tc0 - 1, tc1 - 1);
                }
                foreach (PageBreak b in ps.RowBreaks)
                {
                    if (b.Manual && b.Id >= 1)
                    {
                        g.RowBreaks.Add(b.Id);
                    }
                }
                foreach (PageBreak b in ps.ColumnBreaks)
                {
                    if (b.Manual && b.Id >= 1)
                    {
                        g.ColumnBreaks.Add(b.Id);
                    }
                }
                if (!draft)
                {
                    foreach (DrawingObject d in sh.Drawings)
                    {
                        if (ImportDrawing(d, null) is GridDrawing gd)
                        {
                            g.Drawings.Add(gd);
                        }
                    }
                }
                return g;
            }

            /// <summary>The grid cell for a Sheets cell; null for a cell with neither a value nor a visible style.</summary>
            private GridCell? ImportCell(Cell cell, CellStyle style, bool showZeros, PrintErrors errors, ConditionalFormatResult? cf)
            {
                object? value = cell.Value;
                CellValueKind kind = KindOf(value);
                if (_showFormulas && cell.Formula is string formula)
                {
                    // "Show formulas" view: Excel prints the formula text, left-aligned, instead of the result.
                    CellStyle plain = style with { NumberFormat = null };
                    GridCellStyle fs = StyleOf(plain, null);
                    if (fs.HorizontalAlignment == CellHorizontalAlignment.General)
                    {
                        fs = fs with { HorizontalAlignment = CellHorizontalAlignment.Left };
                    }
                    return new GridCell("=" + formula, CellValueKind.Text, fs);
                }
                string text = kind == CellValueKind.Empty ? string.Empty : cell.GetDisplayText(_nf);
                string? formatCode = style.NumberFormat;
                if (cf?.NumberFormat is string cfCode && value is not null)
                {
                    formatCode = cfCode;
                    text = NumberFormatter.Format(value, cfCode, _nf);
                }
                string? formatColor = null;
                char? fillChar = null;
                int? fillIndex = null;
                // Only codes with a [colour] section or a '*' fill carry presentation hints; skip the second pass otherwise.
                if (value is not null && kind != CellValueKind.Error && formatCode is not null
                    && (formatCode.Contains('[', StringComparison.Ordinal) || formatCode.Contains('*', StringComparison.Ordinal)))
                {
                    FormattedValue fv = NumberFormatter.FormatDetailed(value, formatCode, _nf);
                    formatColor = fv.Color;
                    if (fv.FillCharacter is char fill && fv.FillIndex is int at && at >= 0 && at <= fv.Text.Length)
                    {
                        // Accounting formats such as _($* #,##0.00_): the currency at the left edge, the number at the right.
                        text = fv.Text;
                        fillChar = fill;
                        fillIndex = at;
                    }
                }
                if (kind == CellValueKind.Number && !showZeros && IsZero(value))
                {
                    text = string.Empty;
                }
                if (kind == CellValueKind.Error)
                {
                    text = errors switch
                    {
                        PrintErrors.Blank => string.Empty,
                        PrintErrors.Dash => "--",
                        PrintErrors.NA => "#N/A",
                        _ => text,
                    };
                }
                GridCellStyle gs = StyleOf(style, formatColor);
                GridDataBar? bar = null;
                if (cf?.DataBar is DataBarResult db)
                {
                    LayoutColor col = Color(db.FillColor ?? db.Bar.Color) ?? new LayoutColor(0x63, 0x8E, 0xC6);
                    bar = new GridDataBar(db.LengthPercent / 100.0, _blackAndWhite ? new LayoutColor(0x80, 0x80, 0x80) : col, db.IsNegative, db.Bar.ShowValue)
                    {
                        BorderColor = _blackAndWhite ? null : Color(db.IsNegative ? db.Bar.NegativeBorderColor ?? db.Bar.BorderColor : db.Bar.BorderColor),
                        Gradient = db.Bar.Gradient,
                    };
                    if (!db.Bar.ShowValue)
                    {
                        text = string.Empty;
                    }
                }
                GridIcon? icon = null;
                if (cf?.Icon is IconResult ir && ir.Count > 0)
                {
                    icon = new GridIcon(ir.IconSet, Math.Clamp(ir.Index, 0, ir.Count - 1), ir.Count);
                    if (!ir.ShowValue)
                    {
                        text = string.Empty;
                    }
                }
                if (kind == CellValueKind.Empty && bar is null && icon is null && style.IsDefault)
                {
                    return null;
                }
                if (text.Length == 0)
                {
                    fillChar = null;
                    fillIndex = null;
                }
                IReadOnlyList<(string Text, CharacterFormat Format)>? runs = text.Length > 0 && cf?.NumberFormat is null && cell.RichText is RichText rt && rt.Runs.Count > 0
                    ? Runs(rt, gs.Font)
                    : null;
                Hyperlink? link = _hyperlinks ? _sheet!.FindHyperlink(cell.Row, cell.Column) : null;
                return new GridCell(text, kind, gs)
                {
                    DataBar = bar,
                    Icon = icon,
                    Runs = runs,
                    FillCharacter = fillChar,
                    FillIndex = fillIndex,
                    HyperlinkUri = link?.Target,
                    HyperlinkLocation = link?.Location,
                };
            }

            /// <summary>Rich-text runs over the cell's font: each run states only what differs (Excel draws the rest in the cell font).</summary>
            private IReadOnlyList<(string Text, CharacterFormat Format)> Runs(RichText rt, CharacterFormat cellFont)
            {
                List<(string, CharacterFormat)> runs = new List<(string, CharacterFormat)>(rt.Runs.Count);
                foreach (RichTextRun run in rt.Runs)
                {
                    if (run.Text.Length == 0)
                    {
                        continue;
                    }
                    CharacterFormat f = cellFont;
                    if (run.Font is RichTextFont rf)
                    {
                        f = f with
                        {
                            FontFamily = !string.IsNullOrWhiteSpace(rf.Name) ? rf.Name
                                : rf.Scheme == FontScheme.Major ? _wb.StyleTable.Theme.MajorFont.Latin ?? f.FontFamily
                                : rf.Scheme == FontScheme.Minor ? _wb.StyleTable.Theme.MinorFont.Latin ?? f.FontFamily
                                : f.FontFamily,
                            FontSize = rf.Size is double size && size > 0 ? size : f.FontSize,
                            Bold = rf.Bold ?? f.Bold,
                            Italic = rf.Italic ?? f.Italic,
                            Strikethrough = rf.Strikethrough ?? f.Strikethrough,
                            Underline = rf.Underline switch
                            {
                                null => f.Underline,
                                SheetsUnderline.Single or SheetsUnderline.SingleAccounting => LayoutUnderline.Single,
                                SheetsUnderline.Double or SheetsUnderline.DoubleAccounting => LayoutUnderline.Double,
                                _ => LayoutUnderline.None,
                            },
                            VerticalAlignment = rf.VerticalAlign switch
                            {
                                null => f.VerticalAlignment,
                                FontVerticalAlign.Superscript => VerticalTextAlignment.Superscript,
                                FontVerticalAlign.Subscript => VerticalTextAlignment.Subscript,
                                _ => VerticalTextAlignment.Baseline,
                            },
                        };
                    }
                    string? runColor = run.ResolvedColor ?? (run.Font?.Color is ColorRef cref ? _wb.StyleTable.ResolveColor(cref) : null);
                    if (!_blackAndWhite && Color(runColor) is LayoutColor rc)
                    {
                        f = f with { Color = rc };
                    }
                    runs.Add((run.Text, f));
                }
                return runs;
            }

            private static CellValueKind KindOf(object? v) => v switch
            {
                null => CellValueKind.Empty,
                bool => CellValueKind.Boolean,
                string s => ErrorLiterals.Contains(s) ? CellValueKind.Error : CellValueKind.Text,
                double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort => CellValueKind.Number,
                DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan => CellValueKind.Number,
                _ => CellValueKind.Text,
            };

            private static bool IsZero(object? v) => v switch
            {
                double d => d == 0,
                float f => f == 0,
                decimal m => m == 0,
                int i => i == 0,
                long l => l == 0,
                _ => false,
            };

            private void ApplyConditionalFormats(Sheet sh, GridSheet g, CellRange used, bool showZeros, PrintErrors errors)
            {
                DateTime today = (_o.Today ?? _lo.Now?.DateTime ?? DateTime.Today).Date;
                ConditionalFormatEvaluator ev = sh.EvaluateConditionalFormats(new ConditionalFormatOptions { Today = today, Date1904 = _wb.Date1904 });
                // Every cell of every formatted range inside the used range (zebra stripes and blank rules colour empty cells too).
                HashSet<(int, int)> seen = new HashSet<(int, int)>();
                long budget = _o.MaxConditionalFormatCells;
                foreach (ConditionalFormatting f in sh.ConditionalFormats)
                {
                    foreach (string rs in f.Ranges)
                    {
                        if (!TryRange(rs, out CellRange r))
                        {
                            continue;
                        }
                        int r0 = Math.Max(r.FirstRow, used.FirstRow), r1 = Math.Min(r.LastRow, used.LastRow);
                        int c0 = Math.Max(r.FirstColumn, used.FirstColumn), c1 = Math.Min(r.LastColumn, used.LastColumn);
                        for (int row = r0; row <= r1; row++)
                        {
                            _ct.ThrowIfCancellationRequested();
                            for (int col = c0; col <= c1; col++)
                            {
                                if (!seen.Add((row, col)))
                                {
                                    continue;
                                }
                                if (--budget < 0)
                                {
                                    Warn(LayoutWarningCodes.ConditionalFormatNotEvaluated, LayoutWarningSeverity.Fidelity,
                                        $"Conditional formats cover more than {_o.MaxConditionalFormatCells} cells; the rest are not applied.");
                                    return;
                                }
                                ConditionalFormatResult? res = ev.Evaluate(row + 1, col + 1);
                                if (res is null)
                                {
                                    continue;
                                }
                                if (res.NotEvaluated.Count > 0)
                                {
                                    Warn(LayoutWarningCodes.ConditionalFormatNotEvaluated, LayoutWarningSeverity.Fidelity,
                                        "Some conditional formats use functions or references that cannot be evaluated; they are not applied.");
                                }
                                if (res.IsEmpty)
                                {
                                    continue;
                                }
                                Cell? cell = sh.FindCell(row + 1, col + 1);
                                CellStyle baseStyle = sh.GetEffectiveStyle(row + 1, col + 1) ?? cell?.Style ?? default;
                                CellStyle applied = res.ApplyTo(baseStyle);
                                GridCell? gc = cell is not null
                                    ? ImportCell(cell, applied, showZeros, errors, res)
                                    : EmptyCell(applied, res);
                                g.SetCell(row, col, gc);
                            }
                        }
                    }
                }
            }

            private GridCell? EmptyCell(CellStyle style, ConditionalFormatResult res)
            {
                return style.IsDefault ? null : new GridCell(string.Empty, CellValueKind.Empty, StyleOf(style, null));
            }

            // ---- Styles ----

            private GridCellStyle StyleOf(CellStyle s, string? formatColor)
            {
                if (formatColor is null && _styleCache.TryGetValue(s, out GridCellStyle? cached))
                {
                    return cached;
                }
                LayoutColor fontColor = (formatColor is not null ? Color(formatColor) : Color(s.FontColor)) ?? LayoutColor.Black;
                if (_blackAndWhite)
                {
                    fontColor = LayoutColor.Black;
                }
                CharacterFormat font = new CharacterFormat
                {
                    FontFamily = string.IsNullOrWhiteSpace(s.FontName) ? _normal.FontFamily : s.FontName,
                    FontSize = s.FontSize is double fs && fs > 0 ? fs : _normal.FontSize,
                    Bold = s.Bold,
                    Italic = s.Italic,
                    Underline = s.UnderlineKind switch
                    {
                        SheetsUnderline.Single or SheetsUnderline.SingleAccounting => LayoutUnderline.Single,
                        SheetsUnderline.Double or SheetsUnderline.DoubleAccounting => LayoutUnderline.Double,
                        _ => s.Underline ? LayoutUnderline.Single : LayoutUnderline.None,
                    },
                    Strikethrough = s.Strikethrough,
                    Color = fontColor,
                    VerticalAlignment = s.FontVerticalPosition switch
                    {
                        FontVerticalAlign.Superscript => VerticalTextAlignment.Superscript,
                        FontVerticalAlign.Subscript => VerticalTextAlignment.Subscript,
                        _ => VerticalTextAlignment.Baseline,
                    },
                };
                GridCellStyle g = new GridCellStyle
                {
                    Font = font,
                    Fill = _blackAndWhite ? null : FillOf(s),
                    BorderTop = BorderOf(s.BorderTop),
                    BorderBottom = BorderOf(s.BorderBottom),
                    BorderLeft = BorderOf(s.BorderLeft),
                    BorderRight = BorderOf(s.BorderRight),
                    BorderDiagonalUp = s.DiagonalUp ? BorderOf(s.BorderDiagonal) : BorderLine.None,
                    BorderDiagonalDown = s.DiagonalDown ? BorderOf(s.BorderDiagonal) : BorderLine.None,
                    HorizontalAlignment = s.HAlign switch
                    {
                        HorizontalAlign.Left => CellHorizontalAlignment.Left,
                        HorizontalAlign.Center => CellHorizontalAlignment.Center,
                        HorizontalAlign.Right => CellHorizontalAlignment.Right,
                        HorizontalAlign.Fill => CellHorizontalAlignment.Fill,
                        HorizontalAlign.Justify => CellHorizontalAlignment.Justify,
                        HorizontalAlign.CenterContinuous => CellHorizontalAlignment.CenterContinuous,
                        HorizontalAlign.Distributed => CellHorizontalAlignment.Distributed,
                        _ => CellHorizontalAlignment.General,
                    },
                    VerticalAlignment = (s.VerticalAlignment ?? s.VAlign) switch
                    {
                        VerticalAlign.Top => CellVerticalAlignmentKind.Top,
                        VerticalAlign.Center => CellVerticalAlignmentKind.Center,
                        VerticalAlign.Justify => CellVerticalAlignmentKind.Justify,
                        VerticalAlign.Distributed => CellVerticalAlignmentKind.Distributed,
                        _ => CellVerticalAlignmentKind.Bottom,
                    },
                    WrapText = s.WrapText,
                    ShrinkToFit = s.ShrinkToFit,
                    Indent = s.Indent,
                    TextRotation = s.TextRotation,
                };
                if (formatColor is null)
                {
                    _styleCache[s] = g;
                }
                return g;
            }

            /// <summary>
            /// A cell fill as one colour. Solid fills are exact. Patterns are drawn as the pattern colour over the
            /// background colour, blended by the pattern's ink coverage; gradients as the average of their stops.
            /// </summary>
            private LayoutColor? FillOf(CellStyle s)
            {
                if (s.FillGradient is GradientFill gf && gf.Stops.Count > 0)
                {
                    int r = 0, gg = 0, b = 0, n = 0;
                    foreach (GradientStop stop in gf.Stops)
                    {
                        if (Color(_wb.StyleTable.ResolveColor(stop.Color)) is LayoutColor c)
                        {
                            r += c.R;
                            gg += c.G;
                            b += c.B;
                            n++;
                        }
                    }
                    if (n == 0)
                    {
                        return null;
                    }
                    Warn(LayoutWarningCodes.FillApproximated, LayoutWarningSeverity.Fidelity, "Gradient cell fills are drawn as the average of their colours.");
                    return new LayoutColor((byte)(r / n), (byte)(gg / n), (byte)(b / n));
                }
                FillPattern? p = s.FillPattern ?? (s.FillColor is not null ? FillPattern.Solid : null);
                if (p is null or FillPattern.None)
                {
                    return null;
                }
                if (p == FillPattern.Solid)
                {
                    return Color(s.FillColor ?? s.FillForegroundColor);
                }
                double coverage = p switch
                {
                    FillPattern.DarkGray => 0.75,
                    FillPattern.MediumGray => 0.5,
                    FillPattern.LightGray => 0.25,
                    FillPattern.Gray125 => 0.125,
                    FillPattern.Gray0625 => 0.0625,
                    FillPattern.DarkHorizontal or FillPattern.DarkVertical or FillPattern.DarkDown or FillPattern.DarkUp or FillPattern.DarkGrid or FillPattern.DarkTrellis => 0.5,
                    _ => 0.25,
                };
                LayoutColor fg = Color(s.FillForegroundColor) ?? LayoutColor.Black;
                LayoutColor bg = Color(s.FillBackgroundColor) ?? LayoutColor.White;
                Warn(LayoutWarningCodes.FillApproximated, LayoutWarningSeverity.Fidelity, "Pattern cell fills are drawn as one blended colour.");
                return new LayoutColor(Mix(fg.R, bg.R, coverage), Mix(fg.G, bg.G, coverage), Mix(fg.B, bg.B, coverage));
            }

            private static byte Mix(byte a, byte b, double t) => (byte)Math.Round((a * t) + (b * (1 - t)));

            /// <summary>Excel border styles as printed: widths in points (DESIGN.md section 10) and dash patterns.</summary>
            private BorderLine BorderOf(SheetsBorder b)
            {
                if (b.IsNone)
                {
                    return BorderLine.None;
                }
                LayoutColor c = _blackAndWhite ? LayoutColor.Black : Color(b.Color) ?? LayoutColor.Black;
                return b.Style switch
                {
                    BorderStyle.Hair => new BorderLine(0.25, c),
                    BorderStyle.Thin => new BorderLine(0.5, c),
                    BorderStyle.Dotted => new BorderLine(0.5, c, LineDash.Dot),
                    BorderStyle.Dashed => new BorderLine(0.5, c, LineDash.Dash),
                    BorderStyle.DashDot => new BorderLine(0.5, c, LineDash.DashDot),
                    BorderStyle.DashDotDot => new BorderLine(0.5, c, LineDash.DashDotDot),
                    BorderStyle.Medium => new BorderLine(1.0, c),
                    BorderStyle.MediumDashed => new BorderLine(1.0, c, LineDash.Dash),
                    BorderStyle.MediumDashDot or BorderStyle.SlantDashDot => new BorderLine(1.0, c, LineDash.DashDot),
                    BorderStyle.MediumDashDotDot => new BorderLine(1.0, c, LineDash.DashDotDot),
                    BorderStyle.Thick => new BorderLine(1.5, c),
                    BorderStyle.Double => new BorderLine(0.5, c, LineDash.Solid, 0, true),
                    _ => new BorderLine(0.5, c),
                };
            }

            private static LayoutColor? Color(string? hex)
            {
                if (string.IsNullOrEmpty(hex))
                {
                    return null;
                }
                try
                {
                    return LayoutColor.FromHex(hex);
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            // ---- Page setup ----

            private GridPageSetup PageSetupOf(PageSetup ps, bool draft)
            {
                // Code 0 (written by some tools) means "not stated", like a missing attribute.
                int code = ps.PaperSizeCode is int pc && pc > 0 ? pc : _o.DefaultPaperSizeCode ?? PaperDefaults.ForCulture(_lo.Culture).SpreadsheetCode;
                PaperSizeInfo? paper = PaperSizeInfo.FromCode(code);
                if (paper is null)
                {
                    // Printer-specific codes have no standard size: fall back by the D-30 policy (the culture's paper).
                    int fallback = _o.DefaultPaperSizeCode is int dc && PaperSizeInfo.FromCode(dc) is not null ? dc : PaperDefaults.ForCulture(_lo.Culture).SpreadsheetCode;
                    paper = PaperSizeInfo.FromCode(fallback);
                    Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity, $"Paper size code {code} is printer-specific or unknown; {paper?.Name ?? "the default paper"} is used.");
                }
                double w = (paper?.WidthMm ?? 215.9) * 72 / 25.4;
                double h = (paper?.HeightMm ?? 279.4) * 72 / 25.4;
                HeaderFooterText hf = ps.HeaderFooter;
                return new GridPageSetup
                {
                    PaperWidth = Math.Min(w, h),
                    PaperHeight = Math.Max(w, h),
                    Landscape = ps.Orientation == PageOrientation.Landscape,
                    MarginLeft = ps.Margins.Left * 72,
                    MarginRight = ps.Margins.Right * 72,
                    MarginTop = ps.Margins.Top * 72,
                    MarginBottom = ps.Margins.Bottom * 72,
                    HeaderMargin = ps.Margins.Header * 72,
                    FooterMargin = ps.Margins.Footer * 72,
                    Scale = ps.Scale,
                    FitToPage = ps.FitToPage,
                    FitToWidth = ps.FitToWidth,
                    FitToHeight = ps.FitToHeight,
                    CenterHorizontally = ps.CenterHorizontally,
                    CenterVertically = ps.CenterVertically,
                    PrintGridlines = ps.PrintGridlines && !draft,
                    PrintHeadings = ps.PrintHeadings,
                    PageOrder = ps.PageOrder == PageOrder.OverThenDown ? GridPageOrder.OverThenDown : GridPageOrder.DownThenOver,
                    FirstPageNumber = ps.UseFirstPageNumber ? ps.FirstPageNumber : null,
                    OddHeader = hf.OddHeader,
                    OddFooter = hf.OddFooter,
                    EvenHeader = hf.EvenHeader,
                    EvenFooter = hf.EvenFooter,
                    FirstHeader = hf.FirstHeader,
                    FirstFooter = hf.FirstFooter,
                    DifferentOddEven = hf.DifferentOddEven,
                    DifferentFirst = hf.DifferentFirst,
                    HeaderFooterAlignWithMargins = hf.AlignWithMargins,
                    HeaderFooterScaleWithDocument = hf.ScaleWithDocument,
                };
            }

            // ---- Ranges ----

            private static bool TryRange(string a1, out CellRange range)
            {
                try
                {
                    range = CellRange.Parse(a1.Trim());
                    return true;
                }
                catch (FormatException)
                {
                    range = default;
                    return false;
                }
            }

            /// <summary>A print area: "A1:D20", whole columns "A:C" (to the last used row) or whole rows "1:5" (to the last used column).</summary>
            private static bool TryArea(string area, int maxRow, int maxCol, out CellRange range)
            {
                string a = area.Replace("$", string.Empty, StringComparison.Ordinal).Trim();
                string[] parts = a.Split(':');
                if (parts.Length == 2 && IsLetters(parts[0]) && IsLetters(parts[1]))
                {
                    int c0 = Model.CellAddress.Parse(parts[0] + "1").Column, c1 = Model.CellAddress.Parse(parts[1] + "1").Column;
                    range = new CellRange(0, Math.Min(c0, c1), Math.Max(0, maxRow), Math.Max(c0, c1));
                    return true;
                }
                if (parts.Length == 2 && IsDigits(parts[0]) && IsDigits(parts[1]))
                {
                    int r0 = int.Parse(parts[0], CultureInfo.InvariantCulture) - 1, r1 = int.Parse(parts[1], CultureInfo.InvariantCulture) - 1;
                    range = new CellRange(Math.Min(r0, r1), 0, Math.Max(r0, r1), Math.Max(0, maxCol));
                    return true;
                }
                return TryRange(a, out range);
            }

            private static bool IsLetters(string s) => s.Length > 0 && s.Length <= 3 && s.TrimStart().Length == s.Length && AllOf(s, char.IsAsciiLetter);

            private static bool IsDigits(string s) => s.Length > 0 && s.Length <= 7 && AllOf(s, char.IsAsciiDigit);

            private static bool AllOf(string s, Func<char, bool> f)
            {
                foreach (char c in s)
                {
                    if (!f(c))
                    {
                        return false;
                    }
                }
                return true;
            }

            // ---- Drawings ----

            private GridDrawing? ImportDrawing(DrawingObject d, DrawingObject? group)
            {
                if (d.Hidden)
                {
                    return null;
                }
                GridDrawingKind kind;
                switch (d.Kind)
                {
                    case DrawingKind.Picture:
                        kind = GridDrawingKind.Picture;
                        break;
                    case DrawingKind.Shape:
                    case DrawingKind.Connector:
                        kind = GridDrawingKind.Shape;
                        break;
                    case DrawingKind.Chart:
                        kind = GridDrawingKind.Chart;
                        break;
                    case DrawingKind.Group:
                        kind = GridDrawingKind.Group;
                        break;
                    default:
                        Warn(LayoutWarningCodes.DrawingApproximated, LayoutWarningSeverity.ContentLoss, $"A drawing of kind {d.Kind} ('{d.Name}') is not printed.");
                        return null;
                }
                LayoutImage? image = null;
                if (kind == GridDrawingKind.Picture)
                {
                    byte[]? bytes = d.GetImageBytes();
                    if (bytes is { Length: > 0 })
                    {
                        Shared.Check(nameof(LayoutLimits.MaxResourceBytes), _lo.Limits.MaxResourceBytes, bytes.LongLength);
                        image = LayoutImage.FromEncoded(bytes);
                    }
                }
                GridDrawing g = new GridDrawing(kind)
                {
                    From = group is null && d.Anchor.From is DrawingPoint f ? Point(f) : null,
                    To = group is null && d.Anchor.Type == DrawingAnchorType.TwoCell && d.Anchor.To is DrawingPoint t ? Point(t) : null,
                    X = group is null && d.Anchor.From is null && d.Anchor.XEmu is long x ? Emu.ToPoints(x) : null,
                    Y = group is null && d.Anchor.From is null && d.Anchor.YEmu is long y ? Emu.ToPoints(y) : null,
                    Width = SizeOf(d.Anchor.WidthEmu ?? d.ExtentWidthEmu),
                    Height = SizeOf(d.Anchor.HeightEmu ?? d.ExtentHeightEmu),
                    RelativeBounds = group is null ? null : Relative(d, group),
                    Name = d.Name,
                    Description = d.Description,
                    Rotation = d.Rotation,
                    FlipHorizontal = d.FlipHorizontal,
                    FlipVertical = d.FlipVertical,
                    Image = image,
                    Crop = d.Crop is PictureCrop c ? (c.Left, c.Top, c.Right, c.Bottom) : null,
                    Geometry = d.Kind == DrawingKind.Connector ? "line" : d.Geometry,
                    Fill = d.NoFill || _blackAndWhite ? null : Color(d.FillColor),
                    Line = d.NoLine ? null : _blackAndWhite && d.LineColor is not null ? LayoutColor.Black : Color(d.LineColor),
                    LineWidth = d.LineWidthEmu is long lw ? Emu.ToPoints(lw) : 0.75,
                    Text = kind == GridDrawingKind.Chart ? d.Chart?.Title : d.Text,
                };
                if (kind == GridDrawingKind.Group)
                {
                    foreach (DrawingObject child in d.Children)
                    {
                        if (ImportDrawing(child, d) is GridDrawing gc)
                        {
                            g.Children.Add(gc);
                        }
                    }
                }
                return g;
            }

            private static double? SizeOf(long? emu) => emu is long e ? Emu.ToPoints(e) : null;

            /// <summary>Sheets reports anchors with 1-based rows and columns; the grid IR is zero-based.</summary>
            private static GridAnchorPoint Point(DrawingPoint p)
                => new GridAnchorPoint(Math.Max(0, p.Row - 1), Math.Max(0, p.Column - 1), Emu.ToPoints(p.ColumnOffsetEmu), Emu.ToPoints(p.RowOffsetEmu));

            /// <summary>A group child's rectangle as fractions of the group's child space.</summary>
            private static LayoutRect? Relative(DrawingObject child, DrawingObject group)
            {
                if (group.ChildExtentWidthEmu is not long gw || group.ChildExtentHeightEmu is not long gh || gw <= 0 || gh <= 0
                    || child.OffsetXEmu is not long cx || child.OffsetYEmu is not long cy || child.ExtentWidthEmu is not long cw || child.ExtentHeightEmu is not long ch)
                {
                    return new LayoutRect(0, 0, 1, 1);
                }
                long gx = group.ChildOffsetXEmu ?? 0, gy = group.ChildOffsetYEmu ?? 0;
                return new LayoutRect((cx - gx) / (double)gw, (cy - gy) / (double)gh, cw / (double)gw, ch / (double)gh);
            }
        }
    }
}
