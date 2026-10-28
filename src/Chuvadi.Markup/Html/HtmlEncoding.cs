using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Markup.Html
{
    /// <summary>
    /// WHATWG encoding labels and the HTML encoding sniffing algorithm (BOM, caller encoding, meta prescan,
    /// fallback). Legacy encodings come from the BCL's built-in code page provider; nothing is registered
    /// globally.
    /// </summary>
    public static class HtmlEncoding
    {
        private const int Replacement = -1;
        private const int UserDefined = -2;

        private static readonly Dictionary<string, int> s_labels = Build();

        private static Dictionary<string, int> Build()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            void Add(int cp, params string[] labels)
            {
                foreach (var l in labels) d[l] = cp;
            }
            Add(65001, "unicode-1-1-utf-8", "unicode11utf8", "unicode20utf8", "utf-8", "utf8", "x-unicode20utf8");
            Add(866, "866", "cp866", "csibm866", "ibm866");
            Add(28592, "csisolatin2", "iso-8859-2", "iso-ir-101", "iso8859-2", "iso88592", "iso_8859-2", "iso_8859-2:1987", "l2", "latin2");
            Add(28593, "csisolatin3", "iso-8859-3", "iso-ir-109", "iso8859-3", "iso88593", "iso_8859-3", "iso_8859-3:1988", "l3", "latin3");
            Add(28594, "csisolatin4", "iso-8859-4", "iso-ir-110", "iso8859-4", "iso88594", "iso_8859-4", "iso_8859-4:1988", "l4", "latin4");
            Add(28595, "csisolatincyrillic", "cyrillic", "iso-8859-5", "iso-ir-144", "iso8859-5", "iso88595", "iso_8859-5", "iso_8859-5:1988");
            Add(28596, "arabic", "asmo-708", "csiso88596e", "csiso88596i", "csisolatinarabic", "ecma-114", "iso-8859-6", "iso-8859-6-e", "iso-8859-6-i", "iso-ir-127", "iso8859-6", "iso88596", "iso_8859-6", "iso_8859-6:1987");
            Add(28597, "csisolatingreek", "ecma-118", "elot_928", "greek", "greek8", "iso-8859-7", "iso-ir-126", "iso8859-7", "iso88597", "iso_8859-7", "iso_8859-7:1987", "sun_eu_greek");
            Add(28598, "csiso88598e", "csisolatinhebrew", "hebrew", "iso-8859-8", "iso-8859-8-e", "iso-ir-138", "iso8859-8", "iso88598", "iso_8859-8", "iso_8859-8:1988", "visual");
            Add(38598, "csiso88598i", "iso-8859-8-i", "logical");
            Add(28603, "iso-8859-13", "iso8859-13", "iso885913");
            Add(28605, "csisolatin9", "iso-8859-15", "iso8859-15", "iso885915", "iso_8859-15", "l9");
            Add(20866, "cskoi8r", "koi", "koi8", "koi8-r", "koi8_r");
            Add(21866, "koi8-ru", "koi8-u");
            Add(10000, "csmacintosh", "mac", "macintosh", "x-mac-roman");
            Add(874, "dos-874", "iso-8859-11", "iso8859-11", "iso885911", "tis-620", "windows-874");
            Add(1250, "cp1250", "windows-1250", "x-cp1250");
            Add(1251, "cp1251", "windows-1251", "x-cp1251");
            Add(1252, "ansi_x3.4-1968", "ascii", "cp1252", "cp819", "csisolatin1", "ibm819", "iso-8859-1", "iso-ir-100", "iso8859-1", "iso88591", "iso_8859-1", "iso_8859-1:1987", "l1", "latin1", "us-ascii", "windows-1252", "x-cp1252");
            Add(1253, "cp1253", "windows-1253", "x-cp1253");
            Add(1254, "cp1254", "csisolatin5", "iso-8859-9", "iso-ir-148", "iso8859-9", "iso88599", "iso_8859-9", "iso_8859-9:1989", "l5", "latin5", "windows-1254", "x-cp1254");
            Add(1255, "cp1255", "windows-1255", "x-cp1255");
            Add(1256, "cp1256", "windows-1256", "x-cp1256");
            Add(1257, "cp1257", "windows-1257", "x-cp1257");
            Add(1258, "cp1258", "windows-1258", "x-cp1258");
            Add(10007, "x-mac-cyrillic", "x-mac-ukrainian");
            Add(936, "chinese", "csgb2312", "csiso58gb231280", "gb2312", "gb_2312", "gb_2312-80", "gbk", "iso-ir-58", "x-gbk");
            Add(54936, "gb18030");
            Add(950, "big5", "big5-hkscs", "cn-big5", "csbig5", "x-x-big5");
            Add(51932, "cseucpkdfmtjapanese", "euc-jp", "x-euc-jp");
            Add(50220, "csiso2022jp", "iso-2022-jp");
            Add(932, "csshiftjis", "ms932", "ms_kanji", "shift-jis", "shift_jis", "sjis", "windows-31j", "x-sjis");
            Add(949, "cseuckr", "csksc56011987", "euc-kr", "iso-ir-149", "korean", "ks_c_5601-1987", "ks_c_5601-1989", "ksc5601", "ksc_5601", "windows-949");
            Add(Replacement, "csiso2022kr", "hz-gb-2312", "iso-2022-cn", "iso-2022-cn-ext", "iso-2022-kr", "replacement");
            Add(1201, "unicodefffe", "utf-16be");
            Add(1200, "csunicode", "iso-10646-ucs-2", "ucs-2", "unicode", "unicodefeff", "utf-16", "utf-16le");
            Add(UserDefined, "x-user-defined");
            return d;
        }

        /// <summary>
        /// Maps a WHATWG encoding label (case-insensitive, surrounding whitespace ignored) to an encoding,
        /// or null if the label is unknown or the encoding is unavailable. Decoders replace invalid bytes with U+FFFD.
        /// </summary>
        public static Encoding? GetEncoding(string label)
        {
            ArgumentNullException.ThrowIfNull(label);
            label = label.Trim('\t', '\n', '\f', '\r', ' ');
            if (!s_labels.TryGetValue(label, out int cp)) return null;
            return FromCodePage(cp);
        }

        private static Encoding? FromCodePage(int cp)
        {
            switch (cp)
            {
                case 65001: return new UTF8Encoding(false, false);
                case 1200: return new UnicodeEncoding(false, false, false);
                case 1201: return new UnicodeEncoding(true, false, false);
                case Replacement: return ReplacementEncoding.Instance;
                case UserDefined: return UserDefinedEncoding.Instance;
            }
            try
            {
                return System.Text.CodePagesEncodingProvider.Instance.GetEncoding(cp, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback)
                    ?? Encoding.GetEncoding(cp, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>The default fallback encoding (windows-1252).</summary>
        public static Encoding Windows1252 => FromCodePage(1252) ?? Encoding.Latin1;

        internal static (Encoding encoding, HtmlEncodingSource source, int skip) Determine(ReadOnlySpan<byte> bytes, HtmlParserOptions options)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return (new UTF8Encoding(false, false), HtmlEncodingSource.ByteOrderMark, 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return (new UnicodeEncoding(true, false, false), HtmlEncodingSource.ByteOrderMark, 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return (new UnicodeEncoding(false, false, false), HtmlEncodingSource.ByteOrderMark, 2);
            if (options.Encoding is not null)
                return (options.Encoding, HtmlEncodingSource.Caller, 0);
            if (options.PrescanMeta)
            {
                var found = Prescan(bytes);
                if (found is not null) return (found, HtmlEncodingSource.MetaPrescan, 0);
            }
            return (options.FallbackEncoding ?? Windows1252, HtmlEncodingSource.Fallback, 0);
        }

        /// <summary>The WHATWG "prescan a byte stream to determine its encoding" algorithm over the first 1024 bytes.</summary>
        public static Encoding? Prescan(ReadOnlySpan<byte> input)
        {
            var b = input.Length > 1024 ? input.Slice(0, 1024) : input;
            int pos = 0;
            while (pos < b.Length)
            {
                if (StartsWith(b, pos, "<!--"))
                {
                    int end = IndexOf(b, pos + 2, "-->");
                    if (end < 0) return null;
                    pos = end + 3;
                    continue;
                }
                if (StartsWithIgnoreCase(b, pos, "<meta") && pos + 5 < b.Length && (IsWs(b[pos + 5]) || b[pos + 5] == '/'))
                {
                    pos += 6;
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    bool gotPragma = false;
                    int needPragma = 0; // 0 null, 1 true, 2 false
                    string? charset = null;
                    while (GetAttribute(b, ref pos, out var name, out var value))
                    {
                        if (!names.Add(name)) continue;
                        if (name == "http-equiv")
                        {
                            if (value.Equals("content-type", StringComparison.OrdinalIgnoreCase)) gotPragma = true;
                        }
                        else if (name == "content")
                        {
                            if (charset is null)
                            {
                                var extracted = ExtractCharsetFromContent(value);
                                if (extracted is not null)
                                {
                                    charset = extracted;
                                    needPragma = 1;
                                }
                            }
                        }
                        else if (name == "charset")
                        {
                            charset = value;
                            needPragma = 2;
                        }
                    }
                    if (needPragma == 0 || (needPragma == 1 && !gotPragma) || charset is null) continue;
                    var enc = GetLabelCodePage(charset, out int cp) ? cp : (int?)null;
                    if (enc is null) continue;
                    int code = enc.Value;
                    if (code == 1200 || code == 1201) code = 65001;
                    if (code == UserDefined) code = 1252;
                    var e = FromCodePage(code);
                    if (e is not null) return e;
                    continue;
                }
                if (b[pos] == '<' && pos + 1 < b.Length && (IsAlpha(b[pos + 1]) || (b[pos + 1] == '/' && pos + 2 < b.Length && IsAlpha(b[pos + 2]))))
                {
                    while (pos < b.Length && !IsWs(b[pos]) && b[pos] != '>') pos++;
                    while (GetAttribute(b, ref pos, out _, out _)) { }
                    continue;
                }
                if (b[pos] == '<' && pos + 1 < b.Length && (b[pos + 1] == '!' || b[pos + 1] == '/' || b[pos + 1] == '?'))
                {
                    int gt = b.Slice(pos).IndexOf((byte)'>');
                    if (gt < 0) return null;
                    pos += gt + 1;
                    continue;
                }
                pos++;
            }
            return null;
        }

        private static bool GetLabelCodePage(string label, out int cp) => s_labels.TryGetValue(label.Trim('\t', '\n', '\f', '\r', ' '), out cp);

        private static bool GetAttribute(ReadOnlySpan<byte> b, ref int pos, out string name, out string value)
        {
            name = value = string.Empty;
            while (pos < b.Length && (IsWs(b[pos]) || b[pos] == '/')) pos++;
            if (pos >= b.Length || b[pos] == '>') return false;
            var n = new StringBuilder();
            var v = new StringBuilder();
            while (true)
            {
                if (pos >= b.Length) return false;
                byte c = b[pos];
                if (c == '=' && n.Length > 0)
                {
                    pos++;
                    goto value;
                }
                if (IsWs(c)) break;
                if (c == '/' || c == '>')
                {
                    name = n.ToString();
                    return true;
                }
                n.Append((char)(c >= 'A' && c <= 'Z' ? c + 0x20 : c));
                pos++;
            }
            while (pos < b.Length && IsWs(b[pos])) pos++;
            if (pos >= b.Length) return false;
            if (b[pos] != '=')
            {
                name = n.ToString();
                return true;
            }
            pos++;
        value:
            while (pos < b.Length && IsWs(b[pos])) pos++;
            if (pos >= b.Length) return false;
            byte q = b[pos];
            if (q == '"' || q == '\'')
            {
                pos++;
                while (true)
                {
                    if (pos >= b.Length) return false;
                    byte c = b[pos++];
                    if (c == q) break;
                    v.Append((char)(c >= 'A' && c <= 'Z' ? c + 0x20 : c));
                }
                name = n.ToString();
                value = v.ToString();
                return true;
            }
            if (q == '>')
            {
                name = n.ToString();
                return true;
            }
            v.Append((char)(q >= 'A' && q <= 'Z' ? q + 0x20 : q));
            pos++;
            while (true)
            {
                if (pos >= b.Length) return false;
                byte c = b[pos];
                if (IsWs(c) || c == '>') break;
                v.Append((char)(c >= 'A' && c <= 'Z' ? c + 0x20 : c));
                pos++;
            }
            name = n.ToString();
            value = v.ToString();
            return true;
        }

        /// <summary>The WHATWG "extracting a character encoding from a meta element" algorithm (content attribute).</summary>
        public static string? ExtractCharsetFromContent(string s)
        {
            int pos = 0;
            while (true)
            {
                int i = s.IndexOf("charset", pos, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                pos = i + 7;
                while (pos < s.Length && IsWs((byte)s[pos])) pos++;
                if (pos >= s.Length || s[pos] != '=') continue;
                pos++;
                while (pos < s.Length && IsWs((byte)s[pos])) pos++;
                if (pos >= s.Length) return null;
                char q = s[pos];
                if (q == '"' || q == '\'')
                {
                    int end = s.IndexOf(q, pos + 1);
                    if (end < 0) return null;
                    return s.Substring(pos + 1, end - pos - 1);
                }
                int st = pos;
                while (pos < s.Length && !IsWs((byte)s[pos]) && s[pos] != ';') pos++;
                return s.Substring(st, pos - st);
            }
        }

        private static bool IsWs(byte c) => c == 0x09 || c == 0x0A || c == 0x0C || c == 0x0D || c == 0x20;

        private static bool IsAlpha(byte c) => (c | 0x20) >= 'a' && (c | 0x20) <= 'z';

        private static bool StartsWith(ReadOnlySpan<byte> b, int pos, string s)
        {
            if (pos + s.Length > b.Length) return false;
            for (int i = 0; i < s.Length; i++)
                if (b[pos + i] != s[i]) return false;
            return true;
        }

        private static bool StartsWithIgnoreCase(ReadOnlySpan<byte> b, int pos, string s)
        {
            if (pos + s.Length > b.Length) return false;
            for (int i = 0; i < s.Length; i++)
            {
                byte c = b[pos + i];
                if (c >= 'A' && c <= 'Z') c = (byte)(c + 0x20);
                if (c != s[i]) return false;
            }
            return true;
        }

        private static int IndexOf(ReadOnlySpan<byte> b, int from, string s)
        {
            for (int i = from; i + s.Length <= b.Length; i++)
                if (StartsWith(b, i, s)) return i;
            return -1;
        }

        /// <summary>The WHATWG "replacement" encoding: any input decodes to a single U+FFFD.</summary>
        private sealed class ReplacementEncoding : Encoding
        {
            public static readonly ReplacementEncoding Instance = new ReplacementEncoding();
            public override string WebName => "replacement";
            public override int GetByteCount(char[] chars, int index, int count) => 0;
            public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => 0;
            public override int GetCharCount(byte[] bytes, int index, int count) => count > 0 ? 1 : 0;
            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
            {
                if (byteCount == 0) return 0;
                chars[charIndex] = '�';
                return 1;
            }
            public override int GetMaxByteCount(int charCount) => 0;
            public override int GetMaxCharCount(int byteCount) => 1;
        }

        /// <summary>The WHATWG x-user-defined encoding: 0x80-0xFF map to U+F780-U+F7FF.</summary>
        private sealed class UserDefinedEncoding : Encoding
        {
            public static readonly UserDefinedEncoding Instance = new UserDefinedEncoding();
            public override string WebName => "x-user-defined";
            public override int GetByteCount(char[] chars, int index, int count) => count;
            public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
            {
                for (int i = 0; i < charCount; i++)
                {
                    char c = chars[charIndex + i];
                    bytes[byteIndex + i] = c < 0x80 ? (byte)c : c >= 0xF780 && c <= 0xF7FF ? (byte)(c - 0xF780 + 0x80) : (byte)'?';
                }
                return charCount;
            }
            public override int GetCharCount(byte[] bytes, int index, int count) => count;
            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
            {
                for (int i = 0; i < byteCount; i++)
                {
                    byte b = bytes[byteIndex + i];
                    chars[charIndex + i] = b < 0x80 ? (char)b : (char)(0xF780 + b - 0x80);
                }
                return byteCount;
            }
            public override int GetMaxByteCount(int charCount) => charCount;
            public override int GetMaxCharCount(int byteCount) => byteCount;
        }
    }
}
