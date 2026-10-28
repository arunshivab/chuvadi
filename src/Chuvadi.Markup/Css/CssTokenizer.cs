using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Css
{
    /// <summary>The CSS Syntax Level 3 tokenizer.</summary>
    public sealed class CssTokenizer
    {
        private readonly string _s;
        private int _pos;
        private CancellationPoll _poll;

        /// <summary>Creates a tokenizer (input is preprocessed: CR/FF to LF, NUL to U+FFFD).</summary>
        public CssTokenizer(string css, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(css);
            _s = Preprocess(css);
            _poll = new CancellationPoll(cancellationToken);
        }

        private static string Preprocess(string s)
        {
            if (s.IndexOfAny(new[] { '\r', '\f', '\0' }) < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r')
                {
                    sb.Append('\n');
                    if (i + 1 < s.Length && s[i + 1] == '\n') i++;
                }
                else if (c == '\f') sb.Append('\n');
                else if (c == '\0') sb.Append('�');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Tokenizes the whole input (without the EOF token).</summary>
        public List<CssToken> TokenizeAll()
        {
            var list = new List<CssToken>();
            while (true)
            {
                var t = Next();
                if (t.Type == CssTokenType.EndOfFile) return list;
                list.Add(t);
            }
        }

        private char At(int i) => i < _s.Length ? _s[i] : '\0';

        private bool Eof(int i) => i >= _s.Length;

        private static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\n';

        private static bool IsHex(char c) => CharUtil.IsAsciiHex(c);

        private static bool IsIdentStart(char c) => CharUtil.IsAsciiAlpha(c) || c == '_' || c >= 0x80;

        private static bool IsIdent(char c) => IsIdentStart(c) || CharUtil.IsAsciiDigit(c) || c == '-';

        private static bool IsNonPrintable(char c) => c <= 0x08 || c == 0x0B || (c >= 0x0E && c <= 0x1F) || c == 0x7F;

        private bool ValidEscape(int i) => At(i) == '\\' && !Eof(i + 1) && At(i + 1) != '\n';

        private bool StartsIdentSequence(int i)
        {
            char c = At(i);
            if (Eof(i)) return false;
            if (c == '-')
            {
                char n = At(i + 1);
                if (!Eof(i + 1) && (IsIdentStart(n) || n == '-')) return true;
                return ValidEscape(i + 1);
            }
            if (IsIdentStart(c)) return true;
            return c == '\\' && ValidEscape(i);
        }

        private bool StartsNumber(int i)
        {
            char c = At(i);
            if (c == '+' || c == '-')
            {
                if (CharUtil.IsAsciiDigit(At(i + 1))) return true;
                return At(i + 1) == '.' && CharUtil.IsAsciiDigit(At(i + 2));
            }
            if (c == '.') return CharUtil.IsAsciiDigit(At(i + 1));
            return CharUtil.IsAsciiDigit(c);
        }

        /// <summary>Returns the next token (EOF repeatedly at the end).</summary>
        public CssToken Next()
        {
            _poll.Tick();
            // comments
            while (At(_pos) == '/' && At(_pos + 1) == '*')
            {
                int end = _s.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
                _pos = end < 0 ? _s.Length : end + 2;
            }
            if (Eof(_pos)) return new CssToken(CssTokenType.EndOfFile);
            char c = _s[_pos];
            if (IsWs(c))
            {
                while (!Eof(_pos) && IsWs(_s[_pos])) _pos++;
                return new CssToken(CssTokenType.Whitespace, " ");
            }
            switch (c)
            {
                case '"':
                case '\'':
                    _pos++;
                    return ConsumeString(c);
                case '#':
                    if (IsIdent(At(_pos + 1)) || ValidEscape(_pos + 1))
                    {
                        _pos++;
                        bool id = StartsIdentSequence(_pos);
                        return new CssToken(CssTokenType.Hash, ConsumeIdentSequence()) { IsIdHash = id };
                    }
                    _pos++;
                    return new CssToken(CssTokenType.Delim, "#");
                case '(': _pos++; return new CssToken(CssTokenType.OpenParen, "(");
                case ')': _pos++; return new CssToken(CssTokenType.CloseParen, ")");
                case '[': _pos++; return new CssToken(CssTokenType.OpenSquare, "[");
                case ']': _pos++; return new CssToken(CssTokenType.CloseSquare, "]");
                case '{': _pos++; return new CssToken(CssTokenType.OpenCurly, "{");
                case '}': _pos++; return new CssToken(CssTokenType.CloseCurly, "}");
                case ',': _pos++; return new CssToken(CssTokenType.Comma, ",");
                case ':': _pos++; return new CssToken(CssTokenType.Colon, ":");
                case ';': _pos++; return new CssToken(CssTokenType.Semicolon, ";");
                case '+':
                case '.':
                    if (StartsNumber(_pos)) return ConsumeNumeric();
                    _pos++;
                    return new CssToken(CssTokenType.Delim, c.ToString());
                case '-':
                    if (StartsNumber(_pos)) return ConsumeNumeric();
                    if (At(_pos + 1) == '-' && At(_pos + 2) == '>')
                    {
                        _pos += 3;
                        return new CssToken(CssTokenType.Cdc, "-->");
                    }
                    if (StartsIdentSequence(_pos)) return ConsumeIdentLike();
                    _pos++;
                    return new CssToken(CssTokenType.Delim, "-");
                case '<':
                    if (At(_pos + 1) == '!' && At(_pos + 2) == '-' && At(_pos + 3) == '-')
                    {
                        _pos += 4;
                        return new CssToken(CssTokenType.Cdo, "<!--");
                    }
                    _pos++;
                    return new CssToken(CssTokenType.Delim, "<");
                case '@':
                    if (StartsIdentSequence(_pos + 1))
                    {
                        _pos++;
                        return new CssToken(CssTokenType.AtKeyword, ConsumeIdentSequence());
                    }
                    _pos++;
                    return new CssToken(CssTokenType.Delim, "@");
                case '\\':
                    if (ValidEscape(_pos)) return ConsumeIdentLike();
                    _pos++;
                    return new CssToken(CssTokenType.Delim, "\\");
            }
            if (CharUtil.IsAsciiDigit(c)) return ConsumeNumeric();
            if (IsIdentStart(c)) return ConsumeIdentLike();
            _pos++;
            return new CssToken(CssTokenType.Delim, c.ToString());
        }

        private string ConsumeIdentSequence()
        {
            var sb = new StringBuilder();
            while (!Eof(_pos))
            {
                char c = _s[_pos];
                if (IsIdent(c))
                {
                    sb.Append(c);
                    _pos++;
                }
                else if (ValidEscape(_pos))
                {
                    _pos++;
                    ConsumeEscape(sb);
                }
                else break;
            }
            return sb.ToString();
        }

        private void ConsumeEscape(StringBuilder sb)
        {
            if (Eof(_pos))
            {
                sb.Append('�');
                return;
            }
            char c = _s[_pos];
            if (IsHex(c))
            {
                int cp = 0, n = 0;
                while (n < 6 && !Eof(_pos) && IsHex(_s[_pos]))
                {
                    cp = cp * 16 + Convert.ToInt32(_s[_pos].ToString(), 16);
                    _pos++;
                    n++;
                }
                if (!Eof(_pos) && IsWs(_s[_pos])) _pos++;
                CharUtil.AppendCodePoint(sb, cp);
                return;
            }
            sb.Append(c);
            _pos++;
        }

        private CssToken ConsumeString(char quote)
        {
            var sb = new StringBuilder();
            while (true)
            {
                if (Eof(_pos)) return new CssToken(CssTokenType.String, sb.ToString());
                char c = _s[_pos];
                if (c == quote)
                {
                    _pos++;
                    return new CssToken(CssTokenType.String, sb.ToString());
                }
                if (c == '\n') return new CssToken(CssTokenType.BadString, sb.ToString());
                if (c == '\\')
                {
                    if (Eof(_pos + 1)) { _pos++; continue; }
                    if (_s[_pos + 1] == '\n') { _pos += 2; continue; }
                    _pos++;
                    ConsumeEscape(sb);
                    continue;
                }
                sb.Append(c);
                _pos++;
            }
        }

        private CssToken ConsumeNumeric()
        {
            int start = _pos;
            bool integer = true;
            if (At(_pos) == '+' || At(_pos) == '-') _pos++;
            while (CharUtil.IsAsciiDigit(At(_pos))) _pos++;
            if (At(_pos) == '.' && CharUtil.IsAsciiDigit(At(_pos + 1)))
            {
                integer = false;
                _pos++;
                while (CharUtil.IsAsciiDigit(At(_pos))) _pos++;
            }
            char e = At(_pos);
            if ((e == 'e' || e == 'E') && (CharUtil.IsAsciiDigit(At(_pos + 1)) || ((At(_pos + 1) == '+' || At(_pos + 1) == '-') && CharUtil.IsAsciiDigit(At(_pos + 2)))))
            {
                integer = false;
                _pos += 2;
                while (CharUtil.IsAsciiDigit(At(_pos))) _pos++;
            }
            string repr = _s.Substring(start, _pos - start);
            double.TryParse(repr, NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
            if (StartsIdentSequence(_pos))
            {
                string unit = ConsumeIdentSequence();
                return new CssToken(CssTokenType.Dimension, repr) { Number = value, IsInteger = integer, Unit = unit };
            }
            if (At(_pos) == '%')
            {
                _pos++;
                return new CssToken(CssTokenType.Percentage, repr) { Number = value, IsInteger = integer };
            }
            return new CssToken(CssTokenType.Number, repr) { Number = value, IsInteger = integer };
        }

        private CssToken ConsumeIdentLike()
        {
            string name = ConsumeIdentSequence();
            if (name.Equals("url", StringComparison.OrdinalIgnoreCase) && At(_pos) == '(')
            {
                _pos++;
                while (IsWs(At(_pos)) && IsWs(At(_pos + 1))) _pos++;
                char n0 = At(_pos), n1 = At(_pos + 1);
                if (n0 == '"' || n0 == '\'' || (IsWs(n0) && (n1 == '"' || n1 == '\'')))
                    return new CssToken(CssTokenType.Function, name);
                return ConsumeUrl();
            }
            if (At(_pos) == '(')
            {
                _pos++;
                return new CssToken(CssTokenType.Function, name);
            }
            return new CssToken(CssTokenType.Ident, name);
        }

        private CssToken ConsumeUrl()
        {
            var sb = new StringBuilder();
            while (IsWs(At(_pos))) _pos++;
            while (true)
            {
                if (Eof(_pos)) return new CssToken(CssTokenType.Url, sb.ToString());
                char c = _s[_pos];
                if (c == ')')
                {
                    _pos++;
                    return new CssToken(CssTokenType.Url, sb.ToString());
                }
                if (IsWs(c))
                {
                    while (IsWs(At(_pos))) _pos++;
                    if (Eof(_pos) || At(_pos) == ')')
                    {
                        if (!Eof(_pos)) _pos++;
                        return new CssToken(CssTokenType.Url, sb.ToString());
                    }
                    ConsumeBadUrlRemnants();
                    return new CssToken(CssTokenType.BadUrl);
                }
                if (c == '"' || c == '\'' || c == '(' || IsNonPrintable(c))
                {
                    ConsumeBadUrlRemnants();
                    return new CssToken(CssTokenType.BadUrl);
                }
                if (c == '\\')
                {
                    if (ValidEscape(_pos))
                    {
                        _pos++;
                        ConsumeEscape(sb);
                        continue;
                    }
                    ConsumeBadUrlRemnants();
                    return new CssToken(CssTokenType.BadUrl);
                }
                sb.Append(c);
                _pos++;
            }
        }

        private void ConsumeBadUrlRemnants()
        {
            while (!Eof(_pos))
            {
                char c = _s[_pos];
                if (c == ')')
                {
                    _pos++;
                    return;
                }
                if (ValidEscape(_pos))
                {
                    _pos++;
                    ConsumeEscape(new StringBuilder());
                    continue;
                }
                _pos++;
            }
        }
    }
}
