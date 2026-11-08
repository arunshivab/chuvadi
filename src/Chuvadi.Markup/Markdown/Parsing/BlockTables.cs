using System.Collections.Generic;
using System.Text;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>GFM tables (GFM 0.29 "Tables (extension)").</summary>
    internal sealed partial class BlockParser
    {
        private int _tableCells;

        /// <summary>One cell of a table row: its trimmed content in the source.</summary>
        private readonly record struct CellRange(int Start, int End);

        // TRACE R-MD-0184
        /// <summary>
        /// Splits a table row [start, end) at unescaped pipes. A leading pipe and a trailing pipe are dropped;
        /// each cell is trimmed of spaces and tabs.
        /// </summary>
        private List<CellRange> SplitRow(int start, int end)
        {
            while (end > start && IsSpaceOrTab(_src[end - 1])) end--;
            var cells = new List<CellRange>();
            int i = start;
            if (i < end && _src[i] == '|') i++;
            int cellStart = i;
            bool endedWithPipe = false;
            while (i < end)
            {
                char c = _src[i];
                if (c == '\\' && i + 1 < end && CharUtil.IsAsciiPunctuation(_src[i + 1]))
                {
                    i += 2;
                    endedWithPipe = false;
                    continue;
                }
                if (c == '|')
                {
                    cells.Add(Trimmed(cellStart, i));
                    cellStart = i + 1;
                    endedWithPipe = true;
                    i++;
                    continue;
                }
                endedWithPipe = false;
                i++;
            }
            if (!endedWithPipe || cellStart < end) cells.Add(Trimmed(cellStart, end));
            return cells;
        }

        /// <summary>Whether the row [start, end) holds a pipe and only empty cells (after R-MD-0262's trimming).</summary>
        private bool OnlyPipes(int start, int end)
        {
            if (_src.IndexOf('|', start, end - start) < 0) return false;
            foreach (var cell in SplitRow(start, end))
                if (cell.End > cell.Start) return false;
            return true;
        }

        /// <summary>A GFM 0.29 whitespace char inside a line: space, tab, U+000B or U+000C.</summary>
        private static bool IsGfmSpace(char c) => c == ' ' || c == '\t' || c == '\v' || c == '\f';

        // TRACE R-MD-0262
        // a cell that follows a pipe also loses U+000B and U+000C at its start; a row's first cell without a leading
        // pipe keeps them, like a paragraph line; every cell keeps them at its end (D-232, G5)
        private CellRange Trimmed(int start, int end)
        {
            bool afterPipe = start > 0 && _src[start - 1] == '|';
            while (start < end && (IsSpaceOrTab(_src[start]) || (afterPipe && (_src[start] == '\v' || _src[start] == '\f')))) start++;
            while (end > start && IsSpaceOrTab(_src[end - 1])) end--;
            return new CellRange(start, end);
        }

        // TRACE R-MD-0185
        /// <summary>The alignments of a delimiter row, or null if the line is not one.</summary>
        private List<TableAlignment>? DelimiterRow(int start, int end)
        {
            // TRACE R-MD-0233
            // the end of a delimiter row (only) also loses U+000B and U+000C, GFM 0.29 whitespace characters
            while (end > start && (IsSpaceOrTab(_src[end - 1]) || _src[end - 1] == '\v' || _src[end - 1] == '\f')) end--;
            var cells = SplitRow(start, end);
            if (cells.Count == 0) return null;
            var result = new List<TableAlignment>(cells.Count);
            foreach (var cell in cells)
            {
                int a = cell.Start, b = cell.End;
                // TRACE R-MD-0259
                // in a delimiter row U+000B and U+000C are whitespace at the start, inside and at the end of each cell
                // (D-232, G5 and N8; R5-0027 and D-157); header and body cells keep them
                while (a < b && IsGfmSpace(_src[a])) a++;
                while (b > a && IsGfmSpace(_src[b - 1])) b--;
                if (a >= b) return null;
                bool left = _src[a] == ':';
                bool right = b - 1 > a && _src[b - 1] == ':';
                if (left) a++;
                if (right) b--;
                if (a >= b) return null;
                for (int k = a; k < b; k++)
                    if (_src[k] != '-') return null;
                result.Add(left && right ? TableAlignment.Center : left ? TableAlignment.Left : right ? TableAlignment.Right : TableAlignment.None);
            }
            return result;
        }

        /// <summary>
        /// Turns the last line of the open paragraph into a table header when this line is a delimiter row with the
        /// same number of cells. Lines before the header stay a paragraph.
        /// </summary>
        private bool TryTable(OpenBlock paragraph)
        {
            int first = _line.FirstNonSpace;
            var alignments = DelimiterRow(first, _line.LineEnd);
            if (alignments is null) return false;
            var lines = paragraph.Lines!;
            if (lines.Count == 0) return false;
            var header = lines[lines.Count - 1];
            // TRACE R-MD-0263
            // once a delimiter row was refused in a paragraph, no later line of it starts a table; a row of only
            // pipes is no header (D-232, G4 and N7)
            if (paragraph.RefusedDelimiterLine >= 0 || OnlyPipes(header.Start, header.End)) return false;
            var headerCells = SplitRow(header.Start, header.End);
            // TRACE R-MD-0186
            if (headerCells.Count != alignments.Count)
            {
                paragraph.RefusedDelimiterLine = lines.Count;
                return false;
            }
            // TRACE R-MD-0187
            if (alignments.Count > _options.Limits.MaxTableColumns) return false;
            if ((long)_tableCells + alignments.Count > _options.Limits.MaxTableCells) return false;

            var parent = paragraph.Parent!;
            lines.RemoveAt(lines.Count - 1);
            // TRACE R-MD-0188
            if (lines.Count > 0)
            {
                paragraph.End = header.Start;
                Close(paragraph, displaced: false);
            }
            else
            {
                parent.Node.RemoveChild(paragraph.Node);
                parent.ChildCount--;
                parent.OpenChild = null;
            }

            var table = new Table();
            table.Alignments.AddRange(alignments);
            var block = new OpenBlock(BlockKind.Table, table, parent) { Start = header.Start, End = _line.LineEnd };
            parent.Node.AppendChildUnchecked(table);
            parent.ChildCount++;
            parent.OpenChild = block;
            block.TableColumns = alignments.Count;
            AddRow(block, header.Start, header.End, isHeader: true);
            // TRACE R-MD-0194
            block.End = _line.LineEnd;
            FinishNonBlankLine(block);
            // TRACE R-MD-0264
            // a delimiter row ending in U+000B or U+000C counts as a blank line for the looseness of the lists around
            // the table (fitted to dc-1337, the only frozen case: "- o\n  -\v\n-" is a loose list)
            int rowEnd = _line.LineEnd;
            while (rowEnd > first && IsSpaceOrTab(_src[rowEnd - 1])) rowEnd--;
            if (rowEnd > first && (_src[rowEnd - 1] == '\v' || _src[rowEnd - 1] == '\f'))
                for (var b = block; b is not null && b.Kind != BlockKind.BlockQuote; b = b.Parent) b.EndsWithBlank = true;
            return true;
        }

        /// <summary>Adds a body row; false if the cell limit refuses it.</summary>
        private bool TryAddBodyRow(OpenBlock table)
        {
            // TRACE R-MD-0189
            if ((long)_tableCells + table.TableColumns > _options.Limits.MaxTableCells) return false;
            AddRow(table, _line.FirstNonSpace, _line.LineEnd, isHeader: false);
            return true;
        }

        // TRACE R-MD-0190
        /// <summary>
        /// Adds a row: cells beyond the column count are ignored, missing cells are added empty at the end of the
        /// row; each cell takes the column's alignment.
        /// </summary>
        private void AddRow(OpenBlock table, int start, int end, bool isHeader)
        {
            var node = (Table)table.Node;
            var cells = SplitRow(start, end);
            var row = new TableRow { IsHeader = isHeader, Span = new SourceSpan(start, end - start) };
            node.AppendChildUnchecked(row);
            _nodes.Take();
            int rowEnd = end;
            for (int k = 0; k < table.TableColumns; k++)
            {
                var cell = new TableCell { Alignment = node.Alignments[k] };
                row.AppendChildUnchecked(cell);
                _nodes.Take();
                if (k < cells.Count)
                {
                    var range = cells[k];
                    var content = CellContent(range.Start, range.End);
                    // TRACE R-MD-0195
                    cell.Span = new SourceSpan(range.Start, content.Text.Length);
                    if (range.End > range.Start) InlineBlocks.Add((cell, content));
                }
                else
                {
                    cell.Span = new SourceSpan(rowEnd, 0);
                }
            }
            _tableCells += table.TableColumns;
            table.End = end;
        }

        // TRACE R-MD-0191
        /// <summary>The inline content of a cell: an escaped pipe loses its backslash, so "\|" reads as "|".</summary>
        private InlineContent CellContent(int start, int end)
        {
            var sb = new StringBuilder(end - start);
            // TRACE R-MD-0224
            // positions inside the cell are counted on its text, so they map to the source from the cell start on
            var textStarts = new List<int> { 0 };
            var sourceStarts = new List<int> { start };
            for (int i = start; i < end; i++)
            {
                if (_src[i] == '\\' && i + 1 < end && CharUtil.IsAsciiPunctuation(_src[i + 1]))
                {
                    if (_src[i + 1] == '|') sb.Append('|');
                    else sb.Append(_src, i, 2);
                    i++;
                    continue;
                }
                sb.Append(_src[i]);
            }
            sb.Replace('\0', '�');
            var breaks = new int[textStarts.Count];
            return new InlineContent(sb.ToString(), textStarts.ToArray(), sourceStarts.ToArray(), breaks);
        }
    }
}
