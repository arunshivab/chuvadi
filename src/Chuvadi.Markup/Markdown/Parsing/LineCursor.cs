using System.Text;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// A position inside the current input line, tracked both as a char index and as a visual column.
    /// Tabs are never rewritten in the source; when block structure needs fewer columns than a tab
    /// covers, the tab is left partly consumed and its leftover columns read as spaces.
    /// </summary>
    internal sealed class LineCursor
    {
        private string _text = string.Empty;

        /// <summary>Offset of the first char of the line.</summary>
        public int LineStart { get; private set; }

        /// <summary>Offset just past the last char of the line (before its line ending).</summary>
        public int LineEnd { get; private set; }

        /// <summary>Offset of the next line (after the line ending), or the text length.</summary>
        public int NextLineStart { get; private set; }

        /// <summary>Zero-based line number.</summary>
        public int LineNumber { get; private set; }

        /// <summary>Current char index.</summary>
        public int Pos { get; private set; }

        /// <summary>Visual column reached so far.</summary>
        public int Column { get; private set; }

        // When the char at Pos is a tab that has been partly consumed, the column where that tab began.
        private int _partialTabStart = -1;

        public string Text => _text;

        // TRACE R-MD-0001
        /// <summary>Loads the line that starts at <paramref name="start"/>; a line ends at LF, CR or CR LF.</summary>
        public void Load(string text, int start, int lineNumber)
        {
            _text = text;
            LineStart = start;
            LineNumber = lineNumber;
            int i = start;
            while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
            LineEnd = i;
            if (i < text.Length)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i += 2;
                else i++;
            }
            NextLineStart = i;
            Pos = start;
            Column = 0;
            _partialTabStart = -1;
            _wsFrom = -1;
        }

        // TRACE R-MD-0002
        private static int TabStopAfter(int column) => column + 4 - (column % 4);

        /// <summary>Columns still available in the char at Pos when it is a partly consumed tab.</summary>
        private int ColumnsLeftInCurrent()
        {
            if (Pos >= LineEnd) return 0;
            char c = _text[Pos];
            if (c == '\t')
            {
                int begin = _partialTabStart >= 0 ? _partialTabStart : Column;
                return TabStopAfter(begin) - Column;
            }
            return c == ' ' ? 1 : 0;
        }

        // TRACE R-MD-0244
        // The run of spaces and tabs last measured: from _wsFrom up to its end _wsEnd (the first other char or the
        // line end), with _wsEndColumn the visual column at _wsEnd. Columns are counted from the line start, so
        // the answer holds for every position inside the run, and each run is measured once however many
        // open blocks ask about it.
        private int _wsFrom = -1;
        private int _wsEnd;
        private int _wsEndColumn;

        private void MeasureSpaces()
        {
            if (_wsFrom >= 0 && Pos >= _wsFrom && Pos <= _wsEnd) return;
            int col = Column;
            int i = Pos;
            if (i < LineEnd && _partialTabStart >= 0)
            {
                col = TabStopAfter(_partialTabStart);
                i++;
            }
            while (i < LineEnd)
            {
                char c = _text[i];
                if (c == ' ') col++;
                else if (c == '\t') col = TabStopAfter(col);
                else break;
                i++;
            }
            _wsFrom = Pos;
            _wsEnd = i;
            _wsEndColumn = col;
        }

        /// <summary>Index of the first char at or after Pos that is not a space or tab (LineEnd if none).</summary>
        public int FirstNonSpace
        {
            get
            {
                MeasureSpaces();
                return _wsEnd;
            }
        }

        /// <summary>Number of columns of spaces and tabs from the current position to the first other char.</summary>
        public int Indent
        {
            get
            {
                MeasureSpaces();
                return _wsEndColumn - Column;
            }
        }

        /// <summary>Columns of a partly used tab at the current position that are not used yet (0 when none).</summary>
        public int PartialTabRemaining => _partialTabStart >= 0 ? TabStopAfter(_partialTabStart) - Column : 0;

        // TRACE R-MD-0003
        /// <summary>True when the rest of the line has only spaces and tabs (a blank line from here on).</summary>
        public bool RestIsBlank => FirstNonSpace >= LineEnd;

        /// <summary>The char at <paramref name="index"/> or '\0' past the line end.</summary>
        public char At(int index) => index < LineEnd ? _text[index] : '\0';

        /// <summary>Consumes up to <paramref name="columns"/> columns of spaces and tabs, splitting a tab if needed.</summary>
        public void SkipColumns(int columns)
        {
            int target = Column + columns;
            while (Column < target && Pos < LineEnd)
            {
                char c = _text[Pos];
                if (c == ' ')
                {
                    Column++;
                    Pos++;
                    _partialTabStart = -1;
                }
                else if (c == '\t')
                {
                    int begin = _partialTabStart >= 0 ? _partialTabStart : Column;
                    int stop = TabStopAfter(begin);
                    if (stop <= target)
                    {
                        Column = stop;
                        Pos++;
                        _partialTabStart = -1;
                    }
                    else
                    {
                        _partialTabStart = begin;
                        Column = target;
                    }
                }
                else break;
            }
        }

        /// <summary>The position state, to return to it later.</summary>
        public (int Pos, int Column, int PartialTab) Save() => (Pos, Column, _partialTabStart);

        /// <summary>Returns to a saved position.</summary>
        public void Restore((int Pos, int Column, int PartialTab) state)
        {
            Pos = state.Pos;
            Column = state.Column;
            _partialTabStart = state.PartialTab;
        }

        /// <summary>Consumes all spaces and tabs.</summary>
        public void SkipSpaces() => SkipColumns(int.MaxValue / 2);

        /// <summary>Consumes <paramref name="count"/> chars that are not spaces or tabs (markers).</summary>
        public void Advance(int count)
        {
            if (_partialTabStart >= 0) SkipColumns(ColumnsLeftInCurrent());
            for (int k = 0; k < count && Pos < LineEnd; k++)
            {
                Column++;
                Pos++;
            }
            _partialTabStart = -1;
        }

        /// <summary>Moves to the first non-space char (consuming the whitespace), then consumes <paramref name="count"/> chars.</summary>
        public void SkipSpacesThenAdvance(int count)
        {
            SkipSpaces();
            Advance(count);
        }

        // TRACE R-MD-0004
        /// <summary>
        /// The rest of the line as content: the unconsumed columns of a partly consumed tab become spaces,
        /// everything else is copied unchanged.
        /// </summary>
        public void AppendRest(StringBuilder sb)
        {
            int i = Pos;
            if (i < LineEnd && _partialTabStart >= 0)
            {
                sb.Append(' ', TabStopAfter(_partialTabStart) - Column);
                i++;
            }
            sb.Append(_text, i, LineEnd - i);
        }
    }
}
