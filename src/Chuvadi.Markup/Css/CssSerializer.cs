using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Markup.Css
{
    /// <summary>
    /// Serializes the CSS model. Strings and URLs are always written quoted with escapes; '&lt;' and '&gt;'
    /// are escaped everywhere they can be (so serialized CSS is safe inside a <c>&lt;style&gt;</c> element),
    /// and a comment is inserted wherever two adjacent tokens would otherwise merge.
    /// </summary>
    public static class CssSerializer
    {
        /// <summary>Serializes a style sheet.</summary>
        public static string Serialize(CssStyleSheet sheet)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            var sb = new StringBuilder();
            foreach (var r in sheet.Rules)
            {
                if (sb.Length > 0) sb.Append('\n');
                WriteRule(sb, r);
            }
            return sb.ToString();
        }

        /// <summary>Serializes one rule.</summary>
        public static string Serialize(CssRule rule)
        {
            var sb = new StringBuilder();
            WriteRule(sb, rule);
            return sb.ToString();
        }

        /// <summary>Serializes declarations as a declaration list (<c>a: b; c: d</c>).</summary>
        public static string Serialize(IEnumerable<CssDeclaration> declarations)
        {
            var sb = new StringBuilder();
            foreach (var d in declarations)
            {
                if (sb.Length > 0) sb.Append("; ");
                WriteDeclaration(sb, d);
            }
            return sb.ToString();
        }

        /// <summary>Serializes component values.</summary>
        public static string Serialize(IEnumerable<CssComponentValue> values)
        {
            var w = new Writer();
            foreach (var v in values) w.Write(v);
            return w.ToString();
        }

        private static void WriteRule(StringBuilder sb, CssRule rule)
        {
            if (rule is CssAtRule at)
            {
                sb.Append('@').Append(Ident(at.Name));
                string prelude = Serialize(at.Prelude).Trim();
                if (prelude.Length > 0) sb.Append(' ').Append(prelude);
                if (!at.HasBlock)
                {
                    sb.Append(';');
                    return;
                }
            }
            else
            {
                sb.Append(Serialize(rule.Prelude).Trim());
            }
            sb.Append(" {");
            bool first = true;
            foreach (var d in rule.Declarations)
            {
                sb.Append(first ? " " : " ");
                WriteDeclaration(sb, d);
                sb.Append(';');
                first = false;
            }
            foreach (var r in rule.Rules)
            {
                sb.Append(' ');
                WriteRule(sb, r);
            }
            sb.Append(" }");
        }

        private static void WriteDeclaration(StringBuilder sb, CssDeclaration d)
        {
            sb.Append(d.IsCustomProperty ? Ident(d.Name) : Ident(d.Name)).Append(": ").Append(Serialize(d.Value).Trim());
            if (d.Important) sb.Append(" !important");
        }

        /// <summary>CSSOM "serialize an identifier", additionally escaping '&lt;' and '&gt;'.</summary>
        public static string Ident(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\0') { sb.Append('�'); continue; }
                if ((c >= 1 && c <= 0x1F) || c == 0x7F || (i == 0 && c >= '0' && c <= '9') || (i == 1 && c >= '0' && c <= '9' && s[0] == '-'))
                {
                    sb.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
                    continue;
                }
                if (i == 0 && c == '-' && s.Length == 1) { sb.Append("\\-"); continue; }
                if (c >= 0x80 || c == '-' || c == '_' || char.IsAsciiLetterOrDigit(c)) { sb.Append(c); continue; }
                if (c == '<' || c == '>') { sb.Append(c == '<' ? "\\3c " : "\\3e "); continue; }
                sb.Append('\\').Append(c);
            }
            return sb.ToString();
        }

        /// <summary>CSSOM "serialize a string" (double-quoted), additionally escaping '&lt;' and '&gt;'.</summary>
        public static string String(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '\0') sb.Append('�');
                else if ((c >= 1 && c <= 0x1F) || c == 0x7F || c == '<' || c == '>')
                    sb.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
                else if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        private sealed class Writer
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private CssTokenType? _last;
            private string _lastValue = string.Empty;

            public override string ToString() => _sb.ToString();

            private void Separate(CssTokenType next, string nextValue)
            {
                if (_last is null) return;
                if (NeedsComment(_last.Value, _lastValue, next, nextValue)) _sb.Append("/**/");
            }

            private static bool Wordish(CssTokenType t) =>
                t is CssTokenType.Ident or CssTokenType.Function or CssTokenType.Url or CssTokenType.BadUrl or CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension;

            private static bool NeedsComment(CssTokenType a, string av, CssTokenType b, string bv)
            {
                bool bMinus = b == CssTokenType.Delim && bv == "-";
                bool bNum = b is CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension;
                bool bIdentish = b is CssTokenType.Ident or CssTokenType.Function or CssTokenType.Url or CssTokenType.BadUrl;
                switch (a)
                {
                    case CssTokenType.Ident:
                        return bIdentish || bMinus || bNum || b == CssTokenType.Cdc || b == CssTokenType.OpenParen;
                    case CssTokenType.AtKeyword:
                    case CssTokenType.Hash:
                    case CssTokenType.Dimension:
                        return bIdentish || bMinus || bNum || b == CssTokenType.Cdc;
                    case CssTokenType.Number:
                        return bIdentish || bNum || (b == CssTokenType.Delim && bv == "%");
                    case CssTokenType.Delim:
                        switch (av)
                        {
                            case "#": return bIdentish || bMinus || bNum;
                            case "-": return bIdentish || bNum;
                            case "@": return bIdentish || bMinus;
                            case ".": case "+": return bNum;
                            case "/": return b == CssTokenType.Delim && bv == "*";
                        }
                        return false;
                }
                return false;
            }

            private void Emit(CssTokenType type, string value, string text)
            {
                Separate(type, value);
                _sb.Append(text);
                _last = type;
                _lastValue = value;
            }

            public void Write(CssComponentValue v)
            {
                switch (v)
                {
                    case CssFunction f:
                        Emit(CssTokenType.Function, f.Name, Ident(f.Name) + "(");
                        foreach (var a in f.Arguments) Write(a);
                        _sb.Append(')');
                        _last = CssTokenType.CloseParen;
                        _lastValue = ")";
                        break;
                    case CssSimpleBlock b:
                        Emit(b.Open == '(' ? CssTokenType.OpenParen : b.Open == '[' ? CssTokenType.OpenSquare : CssTokenType.OpenCurly, b.Open.ToString(), b.Open.ToString());
                        foreach (var a in b.Values) Write(a);
                        _sb.Append(b.Close);
                        _last = b.Open == '(' ? CssTokenType.CloseParen : b.Open == '[' ? CssTokenType.CloseSquare : CssTokenType.CloseCurly;
                        _lastValue = b.Close.ToString();
                        break;
                    case CssToken t:
                        WriteToken(t);
                        break;
                }
            }

            private void WriteToken(CssToken t)
            {
                switch (t.Type)
                {
                    case CssTokenType.Ident: Emit(t.Type, t.Value, Ident(t.Value)); break;
                    case CssTokenType.AtKeyword: Emit(t.Type, t.Value, "@" + Ident(t.Value)); break;
                    case CssTokenType.Hash: Emit(t.Type, t.Value, "#" + (t.IsIdHash ? Ident(t.Value) : HashName(t.Value))); break;
                    case CssTokenType.String: Emit(t.Type, t.Value, String(t.Value)); break;
                    case CssTokenType.BadString: Emit(CssTokenType.String, t.Value, String(t.Value)); break;
                    case CssTokenType.Url: Emit(t.Type, t.Value, "url(" + String(t.Value) + ")"); break;
                    case CssTokenType.BadUrl: Emit(t.Type, t.Value, "url()"); break;
                    case CssTokenType.Number: Emit(t.Type, t.Value, t.Value); break;
                    case CssTokenType.Percentage: Emit(t.Type, t.Value, t.Value + "%"); break;
                    case CssTokenType.Dimension:
                    {
                        string unit = Ident(t.Unit);
                        // a unit starting with e/E followed by digits would re-read as an exponent
                        if (unit.Length > 1 && (unit[0] == 'e' || unit[0] == 'E')
                            && (char.IsAsciiDigit(unit[1]) || ((unit[1] == '+' || unit[1] == '-') && unit.Length > 2 && char.IsAsciiDigit(unit[2]))))
                            unit = "\\" + ((int)unit[0]).ToString("x", CultureInfo.InvariantCulture) + " " + unit.Substring(1);
                        Emit(t.Type, t.Value, t.Value + unit);
                        break;
                    }
                    case CssTokenType.Whitespace:
                        _sb.Append(' ');
                        _last = null;
                        break;
                    case CssTokenType.Delim:
                        // '<' cannot be escaped as a delim: write an escape (a rule using it is invalid anyway);
                        // a lone backslash is followed by a newline so it cannot start an escape
                        string d = t.Value switch { "<" => "\\3c ", "\\" => "\\\n", _ => t.Value };
                        Emit(t.Type, t.Value, d);
                        break;
                    case CssTokenType.Cdo: Emit(t.Type, t.Value, "\\3c !--"); break;
                    case CssTokenType.Cdc: Emit(t.Type, t.Value, "--\\3e "); break;
                    case CssTokenType.EndOfFile: break;
                    default: Emit(t.Type, t.Value, t.Value); break;
                }
            }

            private static string HashName(string s)
            {
                var sb = new StringBuilder();
                foreach (char c in s)
                {
                    if (c >= 0x80 || c == '-' || c == '_' || char.IsAsciiLetterOrDigit(c)) sb.Append(c);
                    else if ((c >= 1 && c <= 0x1F) || c == 0x7F || c == '<' || c == '>') sb.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
                    else sb.Append('\\').Append(c);
                }
                return sb.ToString();
            }
        }
    }
}
