using System.Collections.Generic;
using System.Text;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>Recognising the start of each kind of block, and adding line content to leaf blocks.</summary>
    internal sealed partial class BlockParser
    {
        private static bool IsSpaceOrTab(char c) => c == ' ' || c == '\t';

        // ---------------------------------------------------------------- ATX headings

        private bool TryAtxHeading(int first, ref OpenBlock container, ref bool opened, OpenBlock? firstUnmatched)
        {
            int end = _line.LineEnd;
            // TRACE R-MD-0077
            int k = first;
            while (k < end && _src[k] == '#') k++;
            int level = k - first;
            if (level < 1 || level > 6) return false;
            if (k < end && !IsSpaceOrTab(_src[k])) return false;

            // TRACE R-MD-0078
            int lineEnd = end;
            int t = end;
            while (t > k && IsSpaceOrTab(_src[t - 1])) t--;
            int h = t;
            while (h > k && _src[h - 1] == '#') h--;
            if (h < t && (h == k || IsSpaceOrTab(_src[h - 1])))
            {
                // a closing sequence: drop it and the spaces before it
                int e = h;
                while (e > k && IsSpaceOrTab(_src[e - 1])) e--;
                lineEnd = e;
            }
            int contentStart = k;
            while (contentStart < lineEnd && IsSpaceOrTab(_src[contentStart])) contentStart++;
            int contentEnd = lineEnd;
            while (contentEnd > contentStart && IsSpaceOrTab(_src[contentEnd - 1])) contentEnd--;

            var block = Open(BlockKind.AtxHeading, container, ref opened, firstUnmatched, first);
            ((Heading)block.Node).Level = level;
            ((Heading)block.Node).IsSetext = false;
            // TRACE R-MD-0079
            block.End = contentEnd > contentStart ? contentEnd : (contentStart > lineEnd ? lineEnd : contentStart);
            block.Lines = new List<ContentLine> { new ContentLine(contentStart, contentEnd, _line.NextLineStart, 0, contentStart, contentStart) };
            FinishNonBlankLine(block);
            Close(block, displaced: false);
            container = block.Parent!;
            return true;
        }

        // ---------------------------------------------------------------- fenced code

        private bool TryFenceOpening(int first, int indent, ref OpenBlock container, ref bool opened, OpenBlock? firstUnmatched)
        {
            int end = _line.LineEnd;
            char fence = _src[first];
            // TRACE R-MD-0080
            int k = first;
            while (k < end && _src[k] == fence) k++;
            int length = k - first;
            if (length < 3) return false;
            // TRACE R-MD-0081
            int infoStart = k;
            while (infoStart < end && IsSpaceOrTab(_src[infoStart])) infoStart++;
            int infoEnd = end;
            while (infoEnd > infoStart && IsSpaceOrTab(_src[infoEnd - 1])) infoEnd--;
            // TRACE R-MD-0082
            if (fence == '`' && _src.IndexOf('`', infoStart, infoEnd - infoStart) >= 0) return false;

            var block = Open(BlockKind.FencedCode, container, ref opened, firstUnmatched, first);
            var code = (CodeBlock)block.Node;
            code.IsFenced = true;
            code.FenceChar = fence;
            code.FenceLength = length;
            // TRACE R-MD-0083
            code.Info = CharacterReferences.Resolve(_src, infoStart, infoEnd);
            block.FenceChar = fence;
            block.FenceLength = length;
            block.FenceIndent = indent;
            block.Literal = new StringBuilder();
            block.End = _line.LineEnd;
            FinishNonBlankLine(block);
            container = block;
            return true;
        }

        // TRACE R-MD-0084
        private bool IsClosingFence(OpenBlock b, int first)
        {
            int end = _line.LineEnd;
            int k = first;
            while (k < end && _src[k] == b.FenceChar) k++;
            if (k - first < b.FenceLength) return false;
            while (k < end && IsSpaceOrTab(_src[k])) k++;
            return k == end;
        }

        // ---------------------------------------------------------------- setext headings

        // TRACE R-MD-0085
        private bool IsSetextUnderline(int first)
        {
            int end = _line.LineEnd;
            char c = _src[first];
            int k = first;
            while (k < end && _src[k] == c) k++;
            while (k < end && IsSpaceOrTab(_src[k])) k++;
            return k == end;
        }

        /// <summary>
        /// Turns the open paragraph into a setext heading. Link reference definitions at its start are taken
        /// out first; if nothing is left, the paragraph stays open (the underline is then read as text) and
        /// false is returned.
        /// </summary>
        private bool TurnIntoSetextHeading(OpenBlock paragraph, int level)
        {
            var parent = paragraph.Parent!;
            // TRACE R-MD-0086
            int used = LinkReferenceReader.Read(_src, paragraph.Lines!, _document, _definitions);
            if (used > 0) paragraph.Lines!.RemoveRange(0, used);
            if (paragraph.Lines!.Count == 0) return false;
            // TRACE R-MD-0087
            var heading = new Heading { Level = level, IsSetext = true };
            parent.Node.RemoveChild(paragraph.Node);
            parent.Node.AppendChildUnchecked(heading);
            paragraph.Node = heading;
            paragraph.Kind = BlockKind.SetextHeading;
            // TRACE R-MD-0106
            paragraph.End = _line.LineEnd;
            FinishNonBlankLine(paragraph);
            Close(paragraph, displaced: false);
            return true;
        }

        // ---------------------------------------------------------------- thematic breaks

        // TRACE R-MD-0088
        private bool IsThematicBreak(int first)
        {
            int end = _line.LineEnd;
            char c = _src[first];
            // TRACE R-MD-0243
            // a scan that failed at a foreign char fails again from any later start before it with the same char
            if (_breakFailEnd == end && _breakFailChar == c && first < _breakFailAt) return false;
            int count = 0;
            for (int k = first; k < end; k++)
            {
                char x = _src[k];
                if (x == c) count++;
                else if (!IsSpaceOrTab(x))
                {
                    _breakFailEnd = end;
                    _breakFailChar = c;
                    _breakFailAt = k;
                    return false;
                }
            }
            return count >= 3;
        }

        // the last failed thematic break scan: its line (by line end), marker char and the foreign char's index
        private int _breakFailEnd = -1;
        private char _breakFailChar;
        private int _breakFailAt;

        // ---------------------------------------------------------------- list items

        private bool TryListItem(int first, bool interruptsParagraph, ref OpenBlock container, ref bool opened, OpenBlock? firstUnmatched)
        {
            int end = _line.LineEnd;
            char c = _src[first];
            bool ordered;
            int number = 0;
            char delimiter = '.';
            int markerEnd;
            // TRACE R-MD-0089
            if (c == '-' || c == '+' || c == '*')
            {
                ordered = false;
                markerEnd = first + 1;
            }
            else if (CharUtil.IsAsciiDigit(c))
            {
                // TRACE R-MD-0090
                int k = first;
                while (k < end && CharUtil.IsAsciiDigit(_src[k]) && k - first < 10) k++;
                int digits = k - first;
                if (digits > 9 || k >= end || (_src[k] != '.' && _src[k] != ')')) return false;
                for (int d = first; d < k; d++) number = number * 10 + (_src[d] - '0');
                delimiter = _src[k];
                ordered = true;
                markerEnd = k + 1;
            }
            else return false;

            // TRACE R-MD-0091
            if (markerEnd < end && !IsSpaceOrTab(_src[markerEnd])) return false;

            // is this item continuing a list of the same type, or starting a new one?
            var listOwner = container;
            while (listOwner.Kind == BlockKind.Paragraph || listOwner.Kind == BlockKind.ThematicBreak) listOwner = listOwner.Parent!;
            bool continuesList = listOwner.Kind == BlockKind.List && SameType((ListBlock)listOwner.Node, ordered, c, delimiter);
            if (continuesList ? !HasRoom(listOwner, BlockKind.ListItem, 1) : !HasRoom(container, BlockKind.List, 2)) return false;

            // marker position and the spaces after it
            int baseColumn = _line.Column;
            var saved = _line.Save();
            _line.SkipSpaces();
            _line.Advance(markerEnd - first);
            int afterMarkerColumn = _line.Column;
            bool restBlank = _line.RestIsBlank;
            int spaces = _line.Indent;

            // TRACE R-MD-0092
            if (interruptsParagraph && !continuesList)
            {
                if (restBlank || (ordered && number != 1))
                {
                    _line.Restore(saved);
                    return false;
                }
            }

            // TRACE R-MD-0093
            int contentSpaces;
            if (restBlank || spaces >= 5) contentSpaces = 1;
            else contentSpaces = spaces;
            if (!restBlank) _line.SkipColumns(contentSpaces);

            // TRACE R-MD-0094
            if (!continuesList)
            {
                var list = Open(BlockKind.List, container, ref opened, firstUnmatched, first);
                var node = (ListBlock)list.Node;
                node.ListKind = ordered ? ListKind.Ordered : ListKind.Bullet;
                if (ordered)
                {
                    node.Start = number;
                    node.Delimiter = delimiter;
                }
                else node.BulletChar = c;
                container = list;
            }
            else container = listOwner;

            var item = Open(BlockKind.ListItem, container, ref opened, firstUnmatched, first);
            item.ItemContentIndent = afterMarkerColumn - baseColumn + contentSpaces;
            // TRACE R-MD-0103
            item.End = _line.LineEnd;
            container = item;
            return true;
        }

        // TRACE R-MD-0095
        private static bool SameType(ListBlock list, bool ordered, char bullet, char delimiter)
        {
            if (ordered) return list.ListKind == ListKind.Ordered && list.Delimiter == delimiter;
            return list.ListKind == ListKind.Bullet && list.BulletChar == bullet;
        }

        // TRACE R-MD-0260
        /// <summary>
        /// Whether the line at <paramref name="first"/> is "-" followed by a space or tab and more content: such a
        /// line starts a list item, not a delimiter row (D-232, G4: "a\n- |" is a paragraph and a list).
        /// </summary>
        private bool StartsListItemWithContent(int first)
        {
            int end = _line.LineEnd;
            if (first + 1 >= end || _src[first] != '-' || !IsSpaceOrTab(_src[first + 1])) return false;
            for (int k = first + 2; k < end; k++)
                if (!IsSpaceOrTab(_src[k])) return true;
            return false;
        }

        // ---------------------------------------------------------------- leaf content

        // TRACE R-MD-0096
        private void AddParagraphLine(OpenBlock paragraph, bool lazy = false)
        {
            int indent = _line.Indent;
            int start = _line.FirstNonSpace;
            // TRACE R-MD-0162
            int entry = lazy ? _line.Pos : start;
            // TRACE R-MD-0266
            // a lazy line that starts inside a partly used tab has its text positions and its end moved on by the
            // tab's unused columns, as the frozen output shows ("- >o\n\t[" puts "[" at 8 and ends the blocks at 9;
            // D-232, C1 as frozen)
            int shift = lazy ? _line.PartialTabRemaining : 0;
            paragraph.Lines!.Add(new ContentLine(start, _line.LineEnd, _line.NextLineStart, indent, shift > 0 ? start : entry, entry, shift));
            if (shift > 0)
            {
                // the shifted end leaves out trailing spaces and tabs ("\t:-<b>[X] " ends at the content end + 2)
                int contentEnd = _line.LineEnd;
                while (contentEnd > start && IsSpaceOrTab(_src[contentEnd - 1])) contentEnd--;
                paragraph.End = contentEnd + shift;
            }
            else paragraph.End = _line.LineEnd;
            if (!lazy && paragraph.HasLazyEnd)
            {
                // a later non-lazy line: pass the lazy lines' end on now, as before
                paragraph.HasLazyEnd = false;
            }
        }

        // TRACE R-MD-0097
        private void AddLiteralLine(OpenBlock block, bool blank)
        {
            var sb = block.Literal!;
            _line.AppendRest(sb);
            sb.Append('\n');
            if (!blank) block.LiteralKeep = sb.Length;
            block.End = _line.LineEnd;
        }

        /// <summary>Takes link reference definitions out of a closing paragraph; returns false if nothing is left.</summary>
        private bool FinishParagraph(OpenBlock b)
        {
            var lines = b.Lines!;
            // TRACE R-MD-0098
            int used = LinkReferenceReader.Read(_src, lines, _document, _definitions);
            if (used > 0) lines.RemoveRange(0, used);
            if (lines.Count == 0) return false;
            // TRACE R-MD-0256
            // a task item whose marker is all its paragraph holds keeps its state and loses the empty paragraph
            // (D-232, G6: "- [ ] " is a task item with no content)
            if (_options.TaskLists && b.FirstInItem && TakeTaskMarker(b) && lines.Count == 1 && lines[0].Start >= lines[0].End) return false;
            // TRACE R-MD-0107
            // (the paragraph keeps its start even when definitions were taken from its first lines)
            InlineBlocks.Add((b.Node, BuildContent(lines)));
            // TRACE R-MD-0267
            if (b.HasLazyEnd) TouchAncestors(b, b.End);
            return true;
        }

        // TRACE R-MD-0099
        /// <summary>
        /// Joins paragraph lines with '\n' and strips the final spaces and tabs. Lines after the first keep their
        /// indentation here; the inline parser skips it after each line break.
        /// </summary>
        private InlineContent BuildContent(List<ContentLine> lines)
        {
            var sb = new StringBuilder();
            var textStarts = new int[lines.Count];
            var sourceStarts = new int[lines.Count];
            var breakEnds = new int[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                textStarts[i] = sb.Length;
                // TRACE R-MD-0165
                // TRACE R-MD-0269
                // a lazy line that became the first line (definitions taken before it) keeps its entry point
                int from = lines[i].RawStart;
                sourceStarts[i] = from + lines[i].Shift;
                breakEnds[i] = lines[i].BreakEnd;
                sb.Append(_src, from, lines[i].End - from);
            }
            int len = sb.Length;
            while (len > 0 && IsSpaceOrTab(sb[len - 1])) len--;
            sb.Length = len;
            // TRACE R-MD-0163
            sb.Replace('\0', '\uFFFD');
            return new InlineContent(sb.ToString(), textStarts, sourceStarts, breakEnds);
        }
    
        // TRACE R-MD-0196
        /// <summary>
        /// GFM task list item: the first paragraph of a list item starting with [ ], [x] or [X] and a space or tab.
        /// The marker leaves the paragraph, which then starts after it and the whitespace that follows.
        /// </summary>
        /// <returns>Whether a task marker was taken.</returns>
        private bool TakeTaskMarker(OpenBlock paragraph)
        {
            var lines = paragraph.Lines!;
            var line = lines[0];
            int i = line.Start;
            if (line.End - i < 4 || _src[i] != '[' || _src[i + 2] != ']') return false;
            char mark = _src[i + 1];
            if (mark != ' ' && mark != 'x' && mark != 'X' && mark != '\t') return false;
            char after = _src[i + 3];
            if (after != ' ' && after != '\t') return false;
            int content = i + 3;
            while (content < line.End && IsSpaceOrTab(_src[content])) content++;
            // TRACE R-MD-0258
            // a tab inside the brackets with nothing after the marker is plain text (D-232, G6: "- [\t] ")
            if (mark == '\t' && content >= line.End && paragraph.Lines!.Count == 1) return false;
            var item = (ListItem)paragraph.Parent!.Node;
            item.TaskState = mark == 'x' || mark == 'X' ? TaskState.Checked : TaskState.Unchecked;
            lines[0] = line with { Start = content, RawStart = content, BreakEnd = content };
            paragraph.Start = content;
            return true;
        }
    }
}
