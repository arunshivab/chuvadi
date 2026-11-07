using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    internal sealed partial class InlineParser
    {
        // ---------------------------------------------------------------- brackets

        private int OpenBracket(int i, bool image)
        {
            int len = image ? 2 : 1;
            FlushText();
            var text = new TextInline(image ? "![" : "[") { Span = SpanOf(i, i + len) };
            var slot = Append(text);
            // TRACE R-MD-0131
            _work.Step();
            _brackets = new BracketOpener
            {
                Image = image,
                TextIndex = i,
                Order = _order++,
                Slot = slot,
                RunsBelow = _runsTail,
                Prev = _brackets,
            };
            return i + len;
        }

        private int CloseBracket(int i)
        {
            _work.Step();
            var opener = _brackets;
            if (opener is null)
            {
                AddText(']', i, i + 1);
                return i + 1;
            }
            // TRACE R-MD-0132
            if (!opener.Image && opener.Order < _linkBarrier)
            {
                _brackets = opener.Prev;
                AddText(']', i, i + 1);
                return i + 1;
            }

            int labelStart = opener.TextIndex + (opener.Image ? 2 : 1);
            if (!TryLinkTail(i, labelStart, out int end, out string destination, out string? title, out LinkStyle style, out string? label))
            {
                // TRACE R-MD-0133
                _brackets = opener.Prev;
                AddText(']', i, i + 1);
                return i + 1;
            }

            FlushText();
            LinkBase node = opener.Image ? new Image() : new Link();
            node.Destination = destination;
            node.Title = title;
            node.Style = style;
            // TRACE R-MD-0268
            // a full reference's Label is its second bracket's raw text trimmed of spaces, tabs and line endings
            // ("[\ta][\ta]" gives Label "a", df-0106); collapsed and shortcut references keep the raw text
            // ("[\ta]" gives "\ta", lab-0027)
            node.Label = style == LinkStyle.ReferenceFull ? label?.Trim(' ', '\t', '\n', '\r') : label;
            node.Span = SpanOf(opener.TextIndex, end);

            // TRACE R-MD-0134
            ProcessEmphasis(opener.RunsBelow);
            var inner = opener.Slot.Next;
            while (inner is not null)
            {
                var nextInner = inner.Next;
                RemoveSlot(inner);
                node.AppendChildUnchecked(inner.Node);
                inner = nextInner;
            }
            RemoveSlot(opener.Slot);
            Append(node);
            _brackets = opener.Prev;
            // TRACE R-MD-0135
            if (!opener.Image) _linkBarrier = _order;
            return end;
        }

        /// <summary>
        /// After the ']' at <paramref name="close"/>: an inline link tail, or a full, collapsed or shortcut
        /// reference that matches a definition.
        /// </summary>
        private bool TryLinkTail(int close, int textStart, out int end, out string destination, out string? title, out LinkStyle style, out string? label)
        {
            end = close + 1;
            destination = string.Empty;
            title = null;
            label = null;
            style = LinkStyle.Inline;

            // TRACE R-MD-0136
            if (close + 1 < _n && _t[close + 1] == '(' && TryInlineTail(close + 2, out end, out destination, out title))
            {
                style = LinkStyle.Inline;
                return true;
            }

            // TRACE R-MD-0137
            int after = close + 1;
            if (after < _n && _t[after] == '[')
            {
                int labelEnd = LinkSyntax.Label(_t, after, _n);
                if (labelEnd > 0)
                {
                    // TRACE R-MD-0138
                    label = _t.Substring(after + 1, labelEnd - 1 - (after + 1));
                    if (!Lookup(label, out destination, out title)) return false;
                    style = LinkStyle.ReferenceFull;
                    end = labelEnd;
                    return true;
                }
                if (after + 1 < _n && _t[after + 1] == ']')
                {
                    // TRACE R-MD-0139
                    if (!TextIsLabel(textStart, close)) return false;
                    label = _t.Substring(textStart, close - textStart);
                    if (!Lookup(label, out destination, out title)) return false;
                    style = LinkStyle.ReferenceCollapsed;
                    end = after + 2;
                    return true;
                }
            }

            // TRACE R-MD-0140
            if (!TextIsLabel(textStart, close)) return false;
            label = _t.Substring(textStart, close - textStart);
            if (!Lookup(label, out destination, out title)) return false;
            style = LinkStyle.ReferenceShortcut;
            end = close + 1;
            return true;
        }

        /// <summary>Whether the link text [textStart, close) is also a valid link label.</summary>
        private bool TextIsLabel(int textStart, int close)
            => close - textStart <= LinkSyntax.MaxLabelChars && LinkSyntax.Label(_t, textStart - 1, close + 1) == close + 1;

        // TRACE R-MD-0141
        private bool Lookup(string label, out string destination, out string? title)
        {
            _work.Step();
            if (_document.LinkDefinitions.TryGetValue(LinkReferenceDefinition.NormalizeLabel(label), out var def))
            {
                destination = def.Destination;
                title = def.Title;
                return true;
            }
            destination = string.Empty;
            title = null;
            return false;
        }

        // TRACE R-MD-0142
        /// <summary>Skips spaces, tabs and at most one line ending.</summary>
        private int SkipLinkSpace(int i)
        {
            while (i < _n && (_t[i] == ' ' || _t[i] == '\t')) i++;
            if (i < _n && _t[i] == '\n')
            {
                i++;
                while (i < _n && (_t[i] == ' ' || _t[i] == '\t')) i++;
            }
            return i;
        }

        /// <summary>The part of an inline link after "(": optional destination, optional title, ")".</summary>
        private bool TryInlineTail(int p, out int end, out string destination, out string? title)
        {
            end = p;
            destination = string.Empty;
            title = null;
            int k = SkipLinkSpace(p);
            if (k >= _n) return false;
            int destEnd = k;
            if (_t[k] != ')')
            {
                // TRACE R-MD-0143
                int ds, de;
                if (_t[k] == '<') destEnd = LinkSyntax.Destination(_t, k, _n, out ds, out de);
                else destEnd = RawDestination(k, out ds, out de);
                if (destEnd < 0) return false;
                destination = CharacterReferences.Resolve(_t, ds, de);
            }
            int t = SkipLinkSpace(destEnd);
            // TRACE R-MD-0144
            if (t > destEnd && t < _n && (_t[t] == '"' || _t[t] == '\'' || _t[t] == '('))
            {
                int titleEnd = TitleAt(t, out int ts, out int te);
                if (titleEnd > 0)
                {
                    title = CharacterReferences.Resolve(_t, ts, te);
                    t = SkipLinkSpace(titleEnd);
                }
            }
            if (t >= _n || _t[t] != ')') return false;
            end = t + 1;
            return true;
        }

        // ---------------------------------------------------------------- autolinks and raw HTML

        private int AngleBracket(int i)
        {
            // TRACE R-MD-0145
            int uriEnd = UriAutolink(i);
            if (uriEnd > 0)
            {
                string uri = CharacterReferences.Safe(CharacterReferences.DecodeReferences(_t.Substring(i + 1, uriEnd - 1 - (i + 1))));
                AddAutolink(i, uriEnd, uri, uri, isEmail: false);
                return uriEnd;
            }
            // TRACE R-MD-0146
            int mailEnd = EmailAutolink(i);
            if (mailEnd > 0)
            {
                string address = _t.Substring(i + 1, mailEnd - 1 - (i + 1));
                AddAutolink(i, mailEnd, "mailto:" + address, address, isEmail: true);
                return mailEnd;
            }
            // TRACE R-MD-0147
            int htmlEnd = RawHtml(i);
            if (htmlEnd > 0)
            {
                var node = new HtmlInline(CharacterReferences.Safe(_t.Substring(i, htmlEnd - i))) { Span = SpanOf(i, htmlEnd) };
                AddNode(node);
                return htmlEnd;
            }
            AddText('<', i, i + 1);
            return i + 1;
        }

        private void AddAutolink(int start, int end, string destination, string text, bool isEmail)
        {
            var link = new Link { Destination = destination, Style = LinkStyle.Autolink, IsEmail = isEmail, Span = SpanOf(start, end) };
            link.AppendChildUnchecked(new TextInline(text) { Span = SpanOf(start + 1, end - 1) });
            AddNode(link);
        }

        // TRACE R-MD-0148
        /// <summary>End (after '&gt;') of a URI autolink at <paramref name="i"/>, or -1.</summary>
        private int UriAutolink(int i)
        {
            int k = i + 1;
            if (k >= _n || !CharUtil.IsAsciiAlpha(_t[k])) return -1;
            int schemeStart = k;
            k++;
            while (k < _n && (CharUtil.IsAsciiAlnum(_t[k]) || _t[k] == '+' || _t[k] == '.' || _t[k] == '-')) k++;
            int schemeLength = k - schemeStart;
            if (schemeLength < 2 || schemeLength > 32 || k >= _n || _t[k] != ':') return -1;
            k++;
            while (k < _n)
            {
                char c = _t[k];
                if (c == '>') return k + 1;
                if (c == '<' || c <= ' ' || c == '\u007f') return -1;
                k++;
            }
            return -1;
        }

        // TRACE R-MD-0149
        /// <summary>End (after '&gt;') of an email autolink at <paramref name="i"/>, or -1.</summary>
        private int EmailAutolink(int i)
        {
            int k = i + 1;
            int localStart = k;
            while (k < _n && (CharUtil.IsAsciiAlnum(_t[k]) || ".!#$%&'*+/=?^_`{|}~-".IndexOf(_t[k]) >= 0)) k++;
            if (k == localStart || k >= _n || _t[k] != '@') return -1;
            k++;
            while (true)
            {
                int labelStart = k;
                while (k < _n && (CharUtil.IsAsciiAlnum(_t[k]) || _t[k] == '-')) k++;
                int len = k - labelStart;
                if (len < 1 || len > 63 || _t[labelStart] == '-' || _t[k - 1] == '-') return -1;
                if (k < _n && _t[k] == '.')
                {
                    k++;
                    continue;
                }
                break;
            }
            return k < _n && _t[k] == '>' ? k + 1 : -1;
        }

        /// <summary>End of a raw HTML tag at <paramref name="i"/> (CommonMark "Raw HTML"), or -1.</summary>
        private int RawHtml(int i)
        {
            if (i + 1 >= _n) return -1;
            char c = _t[i + 1];
            if (CharUtil.IsAsciiAlpha(c)) return HtmlSyntax.OpenTag(_t, i, _n, allowLineEnding: true, _quotes);
            if (c == '/') return HtmlSyntax.ClosingTag(_t, i, _n, allowLineEnding: true);
            if (c == '?')
            {
                // TRACE R-MD-0150
                int close = _finder.Find("?>", i + 2);
                return close < 0 ? -1 : close + 2;
            }
            if (c != '!') return -1;
            if (i + 3 < _n && _t[i + 2] == '-' && _t[i + 3] == '-')
            {
                // TRACE R-MD-0151
                int bodyStart = i + 4;
                if (bodyStart < _n && _t[bodyStart] == '>') return bodyStart + 1;
                if (bodyStart + 1 < _n && _t[bodyStart] == '-' && _t[bodyStart + 1] == '>') return bodyStart + 2;
                int close = _finder.Find("-->", bodyStart);
                return close < 0 ? -1 : close + 3;
            }
            if (string.CompareOrdinal(_t, i + 2, "[CDATA[", 0, 7) == 0)
            {
                // TRACE R-MD-0152
                int close = _finder.Find("]]>", i + 9);
                return close < 0 ? -1 : close + 3;
            }
            if (i + 2 < _n && CharUtil.IsAsciiAlpha(_t[i + 2]))
            {
                // TRACE R-MD-0153
                int close = _finder.Find(">", i + 3);
                return close < 0 ? -1 : close + 1;
            }
            return -1;
        }
    }
}
