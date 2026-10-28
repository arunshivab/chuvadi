using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Html
{
    /// <summary>Tokenizer states a caller (or the tree builder) may switch to.</summary>
    public enum HtmlTokenizerState
    {
        /// <summary>Normal content.</summary>
        Data,
        /// <summary>Escapable raw text (<c>title</c>, <c>textarea</c>).</summary>
        Rcdata,
        /// <summary>Raw text (<c>style</c>, <c>xmp</c>, <c>iframe</c>, <c>noembed</c>, <c>noframes</c>).</summary>
        Rawtext,
        /// <summary>Script data (<c>script</c>).</summary>
        ScriptData,
        /// <summary>Everything to the end is text (<c>plaintext</c>).</summary>
        Plaintext,
        /// <summary>Inside a CDATA section (foreign content only).</summary>
        CdataSection,
    }

    /// <summary>
    /// The WHATWG HTML tokenizer: a full implementation of the tokenization state machine, including
    /// all character reference rules. Input is newline-normalized first (CR LF and CR become LF).
    /// Produces <see cref="HtmlToken"/>s one at a time; adjacent characters arrive as one token.
    /// </summary>
    public sealed class HtmlTokenizer
    {
        private enum S
        {
            Data, Rcdata, Rawtext, ScriptData, Plaintext,
            TagOpen, EndTagOpen, TagName,
            RcdataLessThan, RcdataEndTagOpen, RcdataEndTagName,
            RawtextLessThan, RawtextEndTagOpen, RawtextEndTagName,
            ScriptDataLessThan, ScriptDataEndTagOpen, ScriptDataEndTagName,
            ScriptDataEscapeStart, ScriptDataEscapeStartDash, ScriptDataEscaped, ScriptDataEscapedDash, ScriptDataEscapedDashDash,
            ScriptDataEscapedLessThan, ScriptDataEscapedEndTagOpen, ScriptDataEscapedEndTagName,
            ScriptDataDoubleEscapeStart, ScriptDataDoubleEscaped, ScriptDataDoubleEscapedDash, ScriptDataDoubleEscapedDashDash,
            ScriptDataDoubleEscapedLessThan, ScriptDataDoubleEscapeEnd,
            BeforeAttributeName, AttributeName, AfterAttributeName, BeforeAttributeValue,
            AttributeValueDoubleQuoted, AttributeValueSingleQuoted, AttributeValueUnquoted, AfterAttributeValueQuoted,
            SelfClosingStartTag, BogusComment, MarkupDeclarationOpen,
            CommentStart, CommentStartDash, Comment, CommentLessThan, CommentLessThanBang, CommentLessThanBangDash,
            CommentLessThanBangDashDash, CommentEndDash, CommentEnd, CommentEndBang,
            Doctype, BeforeDoctypeName, DoctypeName, AfterDoctypeName, AfterDoctypePublicKeyword,
            BeforeDoctypePublicIdentifier, DoctypePublicIdentifierDoubleQuoted, DoctypePublicIdentifierSingleQuoted,
            AfterDoctypePublicIdentifier, BetweenDoctypePublicAndSystemIdentifiers, AfterDoctypeSystemKeyword,
            BeforeDoctypeSystemIdentifier, DoctypeSystemIdentifierDoubleQuoted, DoctypeSystemIdentifierSingleQuoted,
            AfterDoctypeSystemIdentifier, BogusDoctype,
            CdataSection, CdataSectionBracket, CdataSectionEnd,
            CharacterReference, NamedCharacterReference, AmbiguousAmpersand, NumericCharacterReference,
            HexadecimalCharacterReferenceStart, DecimalCharacterReferenceStart, HexadecimalCharacterReference,
            DecimalCharacterReference, NumericCharacterReferenceEnd,
        }

        private const int Eof = -1;

        private readonly string _in;
        private readonly int _len;
        private readonly MarkupLimits _limits;
        private CancellationPoll _poll;
        private int _pos;
        private S _state = S.Data;
        private S _returnState;
        private bool _eofEmitted;

        private readonly Queue<HtmlToken> _queue = new Queue<HtmlToken>();
        private readonly StringBuilder _chars = new StringBuilder();
        private int _charsStart = -1;

        // current tag
        private HtmlToken? _tag;
        private readonly StringBuilder _tagName = new StringBuilder();
        private readonly StringBuilder _attrName = new StringBuilder();
        private readonly StringBuilder _attrValue = new StringBuilder();
        private bool _inAttr;
        private readonly HashSet<string> _attrNames = new HashSet<string>(StringComparer.Ordinal);
        private bool _attrValueTruncated;

        // comment / doctype
        private HtmlToken? _comment;
        private readonly StringBuilder _commentData = new StringBuilder();
        private HtmlToken? _doctype;
        private StringBuilder? _doctypeName, _publicId, _systemId;

        private readonly StringBuilder _temp = new StringBuilder();
        private int _charRefCode;
        private string? _lastStartTag;

        private readonly List<HtmlParseError> _errors = new List<HtmlParseError>();
        private int _errorCount;

        /// <summary>Creates a tokenizer over a string.</summary>
        public HtmlTokenizer(string input, MarkupLimits? limits = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            _limits = limits ?? new MarkupLimits();
            _limits.Validate();
            Guard.InputLength(input.Length, _limits);
            _in = NormalizeNewlines(input);
            _len = _in.Length;
            _poll = new CancellationPoll(cancellationToken);
        }

        /// <summary>The newline-normalized input.</summary>
        public string Input => _in;

        /// <summary>Parse errors recorded so far (up to <see cref="MarkupLimits.MaxRecordedErrors"/>).</summary>
        public IReadOnlyList<HtmlParseError> Errors => _errors;

        /// <summary>Total parse errors, including those beyond the recording limit.</summary>
        public int ErrorCount => _errorCount;

        /// <summary>
        /// The name of the last start tag emitted (used to recognise appropriate end tags in RCDATA,
        /// RAWTEXT and script data). Settable for fragment parsing and tests.
        /// </summary>
        public string? LastStartTagName
        {
            get => _lastStartTag;
            set => _lastStartTag = value;
        }

        /// <summary>
        /// Called at <c>&lt;![CDATA[</c>: returns true when the adjusted current node is not in the HTML
        /// namespace (set by the tree builder; null means CDATA is never allowed).
        /// </summary>
        public Func<bool>? AllowCdata { get; set; }

        /// <summary>Switches the tokenizer state (the tree builder does this for raw text elements).</summary>
        public void SetState(HtmlTokenizerState state)
        {
            _state = state switch
            {
                HtmlTokenizerState.Rcdata => S.Rcdata,
                HtmlTokenizerState.Rawtext => S.Rawtext,
                HtmlTokenizerState.ScriptData => S.ScriptData,
                HtmlTokenizerState.Plaintext => S.Plaintext,
                HtmlTokenizerState.CdataSection => S.CdataSection,
                _ => S.Data,
            };
        }

        internal static string NormalizeNewlines(string s)
        {
            int i = s.IndexOf('\r');
            if (i < 0) return s;
            var sb = new StringBuilder(s.Length);
            sb.Append(s, 0, i);
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r')
                {
                    sb.Append('\n');
                    if (i + 1 < s.Length && s[i + 1] == '\n') i++;
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Returns the next token; returns end-of-file tokens forever once input is exhausted.</summary>
        public HtmlToken NextToken()
        {
            while (_queue.Count == 0)
            {
                if (_eofEmitted)
                    return new HtmlToken(HtmlTokenKind.EndOfFile) { Offset = _len };
                _poll.Tick();
                Step();
            }
            return _queue.Dequeue();
        }

        /// <summary>Tokenizes all input (convenience for tooling and tests).</summary>
        public IEnumerable<HtmlToken> Tokens()
        {
            while (true)
            {
                var t = NextToken();
                yield return t;
                if (t.Kind == HtmlTokenKind.EndOfFile) yield break;
            }
        }

        // ---------------- helpers ----------------

        private int _checkedUpTo;

        private int Consume()
        {
            if (_pos >= _len)
            {
                _pos++;
                return Eof;
            }
            char c = _in[_pos];
            if (_pos >= _checkedUpTo) CheckInput(_pos, c);
            _pos++;
            return c;
        }

        /// <summary>Input stream preprocessing errors (reported once per character).</summary>
        private void CheckInput(int i, char c)
        {
            _checkedUpTo = i + 1;
            if (c < 0x20)
            {
                if (c != '\t' && c != '\n' && c != '\f' && c != '\r' && c != 0) Error("control-character-in-input-stream", i);
            }
            else if (c >= 0x7F && c <= 0x9F)
            {
                Error("control-character-in-input-stream", i);
            }
            else if (c >= 0xD800 && c <= 0xDFFF)
            {
                if (char.IsHighSurrogate(c) && i + 1 < _len && char.IsLowSurrogate(_in[i + 1]))
                {
                    int cp = char.ConvertToUtf32(c, _in[i + 1]);
                    _checkedUpTo = i + 2;
                    if ((cp & 0xFFFE) == 0xFFFE) Error("noncharacter-in-input-stream", i);
                }
                else
                {
                    Error("surrogate-in-input-stream", i);
                }
            }
            else if (c >= 0xFDD0 && (c <= 0xFDEF || c >= 0xFFFE))
            {
                Error("noncharacter-in-input-stream", i);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void CheckFast(int i)
        {
            char c = _in[i];
            if (i >= _checkedUpTo && (c < 0x20 || c >= 0x7F && c <= 0x9F || c >= 0xD800 && c <= 0xDFFF || c >= 0xFDD0))
                CheckInput(i, c);
        }

        private void Reconsume() => _pos--;

        private int Peek(int ahead = 0) => _pos + ahead < _len ? _in[_pos + ahead] : Eof;

        internal void Error(string code, int offset = -1)
        {
            _errorCount++;
            if (_errors.Count < _limits.MaxRecordedErrors)
                _errors.Add(new HtmlParseError(code, offset < 0 ? Math.Min(Math.Max(0, _pos - 1), _len) : offset));
        }

        private static bool IsWs(int c) => c == '\t' || c == '\n' || c == '\f' || c == ' ';

        private static bool IsAlpha(int c) => c >= 0 && CharUtil.IsAsciiAlpha((char)c);

        private static bool IsAlnum(int c) => c >= 0 && CharUtil.IsAsciiAlnum((char)c);

        private static char Lower(int c) => (char)(c >= 'A' && c <= 'Z' ? c + 0x20 : c);

        private void EmitChar(int c)
        {
            if (_charsStart < 0) _charsStart = Math.Max(0, Math.Min(_pos - 1, _len));
            _chars.Append((char)c);
        }

        private void EmitChars(string s)
        {
            if (_charsStart < 0) _charsStart = Math.Max(0, Math.Min(_pos - 1, _len));
            _chars.Append(s);
        }

        private void FlushChars()
        {
            if (_chars.Length == 0) return;
            _queue.Enqueue(new HtmlToken(HtmlTokenKind.Character) { Data = _chars.ToString(), Offset = _charsStart });
            _chars.Clear();
            _charsStart = -1;
        }

        private void Emit(HtmlToken t)
        {
            FlushChars();
            _queue.Enqueue(t);
        }

        private void EmitEof()
        {
            FlushChars();
            _queue.Enqueue(new HtmlToken(HtmlTokenKind.EndOfFile) { Offset = _len });
            _eofEmitted = true;
        }

        private void NewTag(HtmlTokenKind kind)
        {
            _tag = new HtmlToken(kind) { Offset = Math.Max(0, _pos - 2) };
            _tagName.Clear();
            _attrNames.Clear();
            _inAttr = false;
        }

        private void StartAttribute()
        {
            FinishAttribute();
            _attrName.Clear();
            _attrValue.Clear();
            _attrValueTruncated = false;
            _inAttr = true;
        }

        private void AppendAttrValue(char c)
        {
            if (_attrValue.Length >= _limits.MaxAttributeValueChars)
            {
                if (!_attrValueTruncated) Error("chuvadi-attribute-value-truncated");
                _attrValueTruncated = true;
                return;
            }
            _attrValue.Append(c);
        }

        private void AppendAttrValue(string s)
        {
            foreach (char c in s) AppendAttrValue(c);
        }

        private void FinishAttribute()
        {
            if (!_inAttr) return;
            _inAttr = false;
            string name = _attrName.ToString();
            if (!_attrNames.Add(name))
            {
                Error("duplicate-attribute");
                return;
            }
            var list = _tag!.AttributeList ??= new List<HtmlTokenAttribute>();
            if (list.Count >= _limits.MaxAttributesPerElement)
            {
                Error("chuvadi-too-many-attributes");
                return;
            }
            list.Add(new HtmlTokenAttribute(name, _attrValue.ToString()));
        }

        private void EmitTag()
        {
            FinishAttribute();
            var t = _tag!;
            t.Name = _tagName.ToString();
            if (t.Kind == HtmlTokenKind.StartTag)
            {
                _lastStartTag = t.Name;
            }
            else
            {
                if (t.AttributeList is { Count: > 0 }) Error("end-tag-with-attributes");
                if (t.SelfClosing) Error("end-tag-with-trailing-solidus");
            }
            _tag = null;
            Emit(t);
        }

        private bool IsAppropriateEndTag() => _lastStartTag is not null && _tagName.ToString() == _lastStartTag;

        private void NewComment(int offset)
        {
            _comment = new HtmlToken(HtmlTokenKind.Comment) { Offset = offset };
            _commentData.Clear();
        }

        private void EmitComment()
        {
            _comment!.Data = _commentData.ToString();
            Emit(_comment);
            _comment = null;
        }

        private void NewDoctype()
        {
            _doctype = new HtmlToken(HtmlTokenKind.Doctype) { Offset = Math.Max(0, _pos - 9) };
            _doctypeName = null;
            _publicId = null;
            _systemId = null;
        }

        private void EmitDoctype()
        {
            var d = _doctype!;
            d.Name = _doctypeName?.ToString();
            d.PublicId = _publicId?.ToString();
            d.SystemId = _systemId?.ToString();
            Emit(d);
            _doctype = null;
        }

        private bool ConsumedAsPartOfAttribute =>
            _returnState is S.AttributeValueDoubleQuoted or S.AttributeValueSingleQuoted or S.AttributeValueUnquoted;

        private void FlushCharRef()
        {
            if (ConsumedAsPartOfAttribute) AppendAttrValue(_temp.ToString());
            else EmitChars(_temp.ToString());
        }

        private bool MatchAhead(string s, bool ignoreCase)
        {
            if (_pos + s.Length > _len) return false;
            return ignoreCase
                ? string.Compare(_in, _pos, s, 0, s.Length, StringComparison.OrdinalIgnoreCase) == 0
                : string.CompareOrdinal(_in, _pos, s, 0, s.Length) == 0;
        }

        // ---------------- state machine ----------------

        private void Step()
        {
            int c;
            switch (_state)
            {
                case S.Data:
                {
                    // fast path for plain text
                    int start = _pos;
                    while (_pos < _len)
                    {
                        char ch = _in[_pos];
                        if (ch == '&' || ch == '<' || ch == '\0') break;
                        CheckFast(_pos);
                        _pos++;
                    }
                    if (_pos > start)
                    {
                        if (_charsStart < 0) _charsStart = start;
                        _chars.Append(_in, start, _pos - start);
                        return;
                    }
                    c = Consume();
                    switch (c)
                    {
                        case '&': _returnState = S.Data; _state = S.CharacterReference; break;
                        case '<': _state = S.TagOpen; break;
                        case 0: Error("unexpected-null-character"); EmitChar(0); break;
                        case Eof: EmitEof(); break;
                        default: EmitChar(c); break;
                    }
                    break;
                }
                case S.Rcdata:
                    c = Consume();
                    switch (c)
                    {
                        case '&': _returnState = S.Rcdata; _state = S.CharacterReference; break;
                        case '<': _state = S.RcdataLessThan; break;
                        case 0: Error("unexpected-null-character"); EmitChar(0xFFFD); break;
                        case Eof: EmitEof(); break;
                        default: EmitChar(c); break;
                    }
                    break;
                case S.Rawtext:
                    c = Consume();
                    switch (c)
                    {
                        case '<': _state = S.RawtextLessThan; break;
                        case 0: Error("unexpected-null-character"); EmitChar(0xFFFD); break;
                        case Eof: EmitEof(); break;
                        default: EmitChar(c); break;
                    }
                    break;
                case S.ScriptData:
                    c = Consume();
                    switch (c)
                    {
                        case '<': _state = S.ScriptDataLessThan; break;
                        case 0: Error("unexpected-null-character"); EmitChar(0xFFFD); break;
                        case Eof: EmitEof(); break;
                        default: EmitChar(c); break;
                    }
                    break;
                case S.Plaintext:
                    c = Consume();
                    switch (c)
                    {
                        case 0: Error("unexpected-null-character"); EmitChar(0xFFFD); break;
                        case Eof: EmitEof(); break;
                        default: EmitChar(c); break;
                    }
                    break;
                case S.TagOpen:
                    c = Consume();
                    if (c == '!') _state = S.MarkupDeclarationOpen;
                    else if (c == '/') _state = S.EndTagOpen;
                    else if (IsAlpha(c)) { NewTag(HtmlTokenKind.StartTag); Reconsume(); _state = S.TagName; }
                    else if (c == '?') { Error("unexpected-question-mark-instead-of-tag-name"); NewComment(_pos - 2); Reconsume(); _state = S.BogusComment; }
                    else if (c == Eof) { Error("eof-before-tag-name"); EmitChar('<'); EmitEof(); }
                    else { Error("invalid-first-character-of-tag-name"); EmitChar('<'); Reconsume(); _state = S.Data; }
                    break;
                case S.EndTagOpen:
                    c = Consume();
                    if (IsAlpha(c)) { NewTag(HtmlTokenKind.EndTag); _tag!.Offset = Math.Max(0, _pos - 3); Reconsume(); _state = S.TagName; }
                    else if (c == '>') { Error("missing-end-tag-name"); _state = S.Data; }
                    else if (c == Eof) { Error("eof-before-tag-name"); EmitChars("</"); EmitEof(); }
                    else { Error("invalid-first-character-of-tag-name"); NewComment(_pos - 3); Reconsume(); _state = S.BogusComment; }
                    break;
                case S.TagName:
                    c = Consume();
                    if (IsWs(c)) _state = S.BeforeAttributeName;
                    else if (c == '/') _state = S.SelfClosingStartTag;
                    else if (c == '>') { _state = S.Data; EmitTag(); }
                    else if (c == 0) { Error("unexpected-null-character"); _tagName.Append('�'); }
                    else if (c == Eof) { Error("eof-in-tag"); EmitEof(); }
                    else _tagName.Append(Lower(c));
                    break;
                case S.RcdataLessThan:
                    c = Consume();
                    if (c == '/') { _temp.Clear(); _state = S.RcdataEndTagOpen; }
                    else { EmitChar('<'); Reconsume(); _state = S.Rcdata; }
                    break;
                case S.RcdataEndTagOpen:
                    c = Consume();
                    if (IsAlpha(c)) { NewTag(HtmlTokenKind.EndTag); _tag!.Offset = Math.Max(0, _pos - 3); Reconsume(); _state = S.RcdataEndTagName; }
                    else { EmitChars("</"); Reconsume(); _state = S.Rcdata; }
                    break;
                case S.RcdataEndTagName:
                    EndTagNameInRawState(S.Rcdata);
                    break;
                case S.RawtextLessThan:
                    c = Consume();
                    if (c == '/') { _temp.Clear(); _state = S.RawtextEndTagOpen; }
                    else { EmitChar('<'); Reconsume(); _state = S.Rawtext; }
                    break;
                case S.RawtextEndTagOpen:
                    c = Consume();
                    if (IsAlpha(c)) { NewTag(HtmlTokenKind.EndTag); _tag!.Offset = Math.Max(0, _pos - 3); Reconsume(); _state = S.RawtextEndTagName; }
                    else { EmitChars("</"); Reconsume(); _state = S.Rawtext; }
                    break;
                case S.RawtextEndTagName:
                    EndTagNameInRawState(S.Rawtext);
                    break;
                case S.ScriptDataLessThan:
                    c = Consume();
                    if (c == '/') { _temp.Clear(); _state = S.ScriptDataEndTagOpen; }
                    else if (c == '!') { _state = S.ScriptDataEscapeStart; EmitChars("<!"); }
                    else { EmitChar('<'); Reconsume(); _state = S.ScriptData; }
                    break;
                case S.ScriptDataEndTagOpen:
                    c = Consume();
                    if (IsAlpha(c)) { NewTag(HtmlTokenKind.EndTag); _tag!.Offset = Math.Max(0, _pos - 3); Reconsume(); _state = S.ScriptDataEndTagName; }
                    else { EmitChars("</"); Reconsume(); _state = S.ScriptData; }
                    break;
                case S.ScriptDataEndTagName:
                    EndTagNameInRawState(S.ScriptData);
                    break;
                case S.ScriptDataEscapeStart:
                    c = Consume();
                    if (c == '-') { _state = S.ScriptDataEscapeStartDash; EmitChar('-'); }
                    else { Reconsume(); _state = S.ScriptData; }
                    break;
                case S.ScriptDataEscapeStartDash:
                    c = Consume();
                    if (c == '-') { _state = S.ScriptDataEscapedDashDash; EmitChar('-'); }
                    else { Reconsume(); _state = S.ScriptData; }
                    break;
                case S.ScriptDataEscaped:
                    c = Consume();
                    switch (c)
                    {
                        case '-': _state = S.ScriptDataEscapedDash; EmitChar('-'); break;
                        case '<': _state = S.ScriptDataEscapedLessThan; break;
                        case 0: Error("unexpected-null-character"); EmitChar(0xFFFD); break;
                        case Eof: Error("eof-in-script-html-comment-like-text"); EmitEof(); break;
                        default: EmitChar(c); break;
                    }
                    break;
                case S.ScriptDataEscapedDash:
                    c = Consume();
                    switch (c)
                    {
                        case '-': _state = S.ScriptDataEscapedDashDash; EmitChar('-'); break;
                        case '<': _state = S.ScriptDataEscapedLessThan; break;
                        case 0: Error("unexpected-null-character"); _state = S.ScriptDataEscaped; EmitChar(0xFFFD); break;
                        case Eof: Error("eof-in-script-html-comment-like-text"); EmitEof(); break;
                        default: _state = S.ScriptDataEscaped; EmitChar(c); break;
                    }
                    break;
                case S.ScriptDataEscapedDashDash:
                    c = Consume();
                    switch (c)
                    {
                        case '-': EmitChar('-'); break;
                        case '<': _state = S.ScriptDataEscapedLessThan; break;
                        case '>': _state = S.ScriptData; EmitChar('>'); break;
                        case 0: Error("unexpected-null-character"); _state = S.ScriptDataEscaped; EmitChar(0xFFFD); break;
                        case Eof: Error("eof-in-script-html-comment-like-text"); EmitEof(); break;
                        default: _state = S.ScriptDataEscaped; EmitChar(c); break;
                    }
                    break;
                case S.ScriptDataEscapedLessThan:
                    c = Consume();
                    if (c == '/') { _temp.Clear(); _state = S.ScriptDataEscapedEndTagOpen; }
                    else if (IsAlpha(c)) { _temp.Clear(); EmitChar('<'); Reconsume(); _state = S.ScriptDataDoubleEscapeStart; }
                    else { EmitChar('<'); Reconsume(); _state = S.ScriptDataEscaped; }
                    break;
                case S.ScriptDataEscapedEndTagOpen:
                    c = Consume();
                    if (IsAlpha(c)) { NewTag(HtmlTokenKind.EndTag); _tag!.Offset = Math.Max(0, _pos - 3); Reconsume(); _state = S.ScriptDataEscapedEndTagName; }
                    else { EmitChars("</"); Reconsume(); _state = S.ScriptDataEscaped; }
                    break;
                case S.ScriptDataEscapedEndTagName:
                    EndTagNameInRawState(S.ScriptDataEscaped);
                    break;
                case S.ScriptDataDoubleEscapeStart:
                    c = Consume();
                    if (IsWs(c) || c == '/' || c == '>')
                    {
                        _state = _temp.ToString() == "script" ? S.ScriptDataDoubleEscaped : S.ScriptDataEscaped;
                        EmitChar(c);
                    }
                    else if (IsAlpha(c)) { _temp.Append(Lower(c)); EmitChar(c); }
                    else { Reconsume(); _state = S.ScriptDataEscaped; }
                    break;
                case S.ScriptDataDoubleEscaped:
                    c = Consume();
                    switch (c)
                    {
                        case '-': _state = S.ScriptDataDoubleEscapedDash; EmitChar('-'); break;
                        case '<': _state = S.ScriptDataDoubleEscapedLessThan; EmitChar('<'); break;
                        case 0: Error("unexpected-null-character"); EmitChar(0xFFFD); break;
                        case Eof: Error("eof-in-script-html-comment-like-text"); EmitEof(); break;
                        default: EmitChar(c); break;
                    }
                    break;
                case S.ScriptDataDoubleEscapedDash:
                    c = Consume();
                    switch (c)
                    {
                        case '-': _state = S.ScriptDataDoubleEscapedDashDash; EmitChar('-'); break;
                        case '<': _state = S.ScriptDataDoubleEscapedLessThan; EmitChar('<'); break;
                        case 0: Error("unexpected-null-character"); _state = S.ScriptDataDoubleEscaped; EmitChar(0xFFFD); break;
                        case Eof: Error("eof-in-script-html-comment-like-text"); EmitEof(); break;
                        default: _state = S.ScriptDataDoubleEscaped; EmitChar(c); break;
                    }
                    break;
                case S.ScriptDataDoubleEscapedDashDash:
                    c = Consume();
                    switch (c)
                    {
                        case '-': EmitChar('-'); break;
                        case '<': _state = S.ScriptDataDoubleEscapedLessThan; EmitChar('<'); break;
                        case '>': _state = S.ScriptData; EmitChar('>'); break;
                        case 0: Error("unexpected-null-character"); _state = S.ScriptDataDoubleEscaped; EmitChar(0xFFFD); break;
                        case Eof: Error("eof-in-script-html-comment-like-text"); EmitEof(); break;
                        default: _state = S.ScriptDataDoubleEscaped; EmitChar(c); break;
                    }
                    break;
                case S.ScriptDataDoubleEscapedLessThan:
                    c = Consume();
                    if (c == '/') { _temp.Clear(); _state = S.ScriptDataDoubleEscapeEnd; EmitChar('/'); }
                    else { Reconsume(); _state = S.ScriptDataDoubleEscaped; }
                    break;
                case S.ScriptDataDoubleEscapeEnd:
                    c = Consume();
                    if (IsWs(c) || c == '/' || c == '>')
                    {
                        _state = _temp.ToString() == "script" ? S.ScriptDataEscaped : S.ScriptDataDoubleEscaped;
                        EmitChar(c);
                    }
                    else if (IsAlpha(c)) { _temp.Append(Lower(c)); EmitChar(c); }
                    else { Reconsume(); _state = S.ScriptDataDoubleEscaped; }
                    break;
                case S.BeforeAttributeName:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '/' || c == '>' || c == Eof) { Reconsume(); _state = S.AfterAttributeName; }
                    else if (c == '=')
                    {
                        Error("unexpected-equals-sign-before-attribute-name");
                        StartAttribute();
                        _attrName.Append('=');
                        _state = S.AttributeName;
                    }
                    else { StartAttribute(); Reconsume(); _state = S.AttributeName; }
                    break;
                case S.AttributeName:
                    c = Consume();
                    if (IsWs(c) || c == '/' || c == '>' || c == Eof) { Reconsume(); _state = S.AfterAttributeName; }
                    else if (c == '=') _state = S.BeforeAttributeValue;
                    else if (c == 0) { Error("unexpected-null-character"); _attrName.Append('�'); }
                    else
                    {
                        if (c == '"' || c == '\'' || c == '<') Error("unexpected-character-in-attribute-name");
                        _attrName.Append(Lower(c));
                    }
                    break;
                case S.AfterAttributeName:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '/') _state = S.SelfClosingStartTag;
                    else if (c == '=') _state = S.BeforeAttributeValue;
                    else if (c == '>') { _state = S.Data; EmitTag(); }
                    else if (c == Eof) { Error("eof-in-tag"); EmitEof(); }
                    else { StartAttribute(); Reconsume(); _state = S.AttributeName; }
                    break;
                case S.BeforeAttributeValue:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '"') _state = S.AttributeValueDoubleQuoted;
                    else if (c == '\'') _state = S.AttributeValueSingleQuoted;
                    else if (c == '>') { Error("missing-attribute-value"); _state = S.Data; EmitTag(); }
                    else { Reconsume(); _state = S.AttributeValueUnquoted; }
                    break;
                case S.AttributeValueDoubleQuoted:
                case S.AttributeValueSingleQuoted:
                {
                    char quote = _state == S.AttributeValueDoubleQuoted ? '"' : '\'';
                    c = Consume();
                    if (c == quote) _state = S.AfterAttributeValueQuoted;
                    else if (c == '&') { _returnState = _state; _state = S.CharacterReference; }
                    else if (c == 0) { Error("unexpected-null-character"); AppendAttrValue('�'); }
                    else if (c == Eof) { Error("eof-in-tag"); EmitEof(); }
                    else
                    {
                        AppendAttrValue((char)c);
                        // fast path for the rest of a plain value
                        while (_pos < _len)
                        {
                            char ch = _in[_pos];
                            if (ch == quote || ch == '&' || ch == '\0') break;
                            CheckFast(_pos);
                            AppendAttrValue(ch);
                            _pos++;
                        }
                    }
                    break;
                }
                case S.AttributeValueUnquoted:
                    c = Consume();
                    if (IsWs(c)) _state = S.BeforeAttributeName;
                    else if (c == '&') { _returnState = S.AttributeValueUnquoted; _state = S.CharacterReference; }
                    else if (c == '>') { _state = S.Data; EmitTag(); }
                    else if (c == 0) { Error("unexpected-null-character"); AppendAttrValue('�'); }
                    else if (c == Eof) { Error("eof-in-tag"); EmitEof(); }
                    else
                    {
                        if (c == '"' || c == '\'' || c == '<' || c == '=' || c == '`') Error("unexpected-character-in-unquoted-attribute-value");
                        AppendAttrValue((char)c);
                    }
                    break;
                case S.AfterAttributeValueQuoted:
                    c = Consume();
                    if (IsWs(c)) _state = S.BeforeAttributeName;
                    else if (c == '/') _state = S.SelfClosingStartTag;
                    else if (c == '>') { _state = S.Data; EmitTag(); }
                    else if (c == Eof) { Error("eof-in-tag"); EmitEof(); }
                    else { Error("missing-whitespace-between-attributes"); Reconsume(); _state = S.BeforeAttributeName; }
                    break;
                case S.SelfClosingStartTag:
                    c = Consume();
                    if (c == '>') { _tag!.SelfClosing = true; _state = S.Data; EmitTag(); }
                    else if (c == Eof) { Error("eof-in-tag"); EmitEof(); }
                    else { Error("unexpected-solidus-in-tag"); Reconsume(); _state = S.BeforeAttributeName; }
                    break;
                case S.BogusComment:
                    c = Consume();
                    if (c == '>') { _state = S.Data; EmitComment(); }
                    else if (c == Eof) { EmitComment(); EmitEof(); }
                    else if (c == 0) { Error("unexpected-null-character"); _commentData.Append('�'); }
                    else _commentData.Append((char)c);
                    break;
                case S.MarkupDeclarationOpen:
                    if (MatchAhead("--", false))
                    {
                        _pos += 2;
                        NewComment(_pos - 4);
                        _state = S.CommentStart;
                    }
                    else if (MatchAhead("DOCTYPE", true))
                    {
                        _pos += 7;
                        _state = S.Doctype;
                    }
                    else if (MatchAhead("[CDATA[", false))
                    {
                        _pos += 7;
                        if (AllowCdata is not null && AllowCdata())
                        {
                            _state = S.CdataSection;
                        }
                        else
                        {
                            Error("cdata-in-html-content");
                            NewComment(_pos - 9);
                            _commentData.Append("[CDATA[");
                            _state = S.BogusComment;
                        }
                    }
                    else
                    {
                        Error("incorrectly-opened-comment");
                        NewComment(_pos - 2);
                        _state = S.BogusComment;
                    }
                    break;
                case S.CommentStart:
                    c = Consume();
                    if (c == '-') _state = S.CommentStartDash;
                    else if (c == '>') { Error("abrupt-closing-of-empty-comment"); _state = S.Data; EmitComment(); }
                    else { Reconsume(); _state = S.Comment; }
                    break;
                case S.CommentStartDash:
                    c = Consume();
                    if (c == '-') _state = S.CommentEnd;
                    else if (c == '>') { Error("abrupt-closing-of-empty-comment"); _state = S.Data; EmitComment(); }
                    else if (c == Eof) { Error("eof-in-comment"); EmitComment(); EmitEof(); }
                    else { _commentData.Append('-'); Reconsume(); _state = S.Comment; }
                    break;
                case S.Comment:
                    c = Consume();
                    switch (c)
                    {
                        case '<': _commentData.Append('<'); _state = S.CommentLessThan; break;
                        case '-': _state = S.CommentEndDash; break;
                        case 0: Error("unexpected-null-character"); _commentData.Append('�'); break;
                        case Eof: Error("eof-in-comment"); EmitComment(); EmitEof(); break;
                        default:
                            _commentData.Append((char)c);
                            while (_pos < _len)
                            {
                                char ch = _in[_pos];
                                if (ch == '<' || ch == '-' || ch == '\0') break;
                                CheckFast(_pos);
                                _commentData.Append(ch);
                                _pos++;
                            }
                            break;
                    }
                    break;
                case S.CommentLessThan:
                    c = Consume();
                    if (c == '!') { _commentData.Append('!'); _state = S.CommentLessThanBang; }
                    else if (c == '<') _commentData.Append('<');
                    else { Reconsume(); _state = S.Comment; }
                    break;
                case S.CommentLessThanBang:
                    c = Consume();
                    if (c == '-') _state = S.CommentLessThanBangDash;
                    else { Reconsume(); _state = S.Comment; }
                    break;
                case S.CommentLessThanBangDash:
                    c = Consume();
                    if (c == '-') _state = S.CommentLessThanBangDashDash;
                    else { Reconsume(); _state = S.CommentEndDash; }
                    break;
                case S.CommentLessThanBangDashDash:
                    c = Consume();
                    if (c == '>' || c == Eof) { Reconsume(); _state = S.CommentEnd; }
                    else { Error("nested-comment"); Reconsume(); _state = S.CommentEnd; }
                    break;
                case S.CommentEndDash:
                    c = Consume();
                    if (c == '-') _state = S.CommentEnd;
                    else if (c == Eof) { Error("eof-in-comment"); EmitComment(); EmitEof(); }
                    else { _commentData.Append('-'); Reconsume(); _state = S.Comment; }
                    break;
                case S.CommentEnd:
                    c = Consume();
                    if (c == '>') { _state = S.Data; EmitComment(); }
                    else if (c == '!') _state = S.CommentEndBang;
                    else if (c == '-') _commentData.Append('-');
                    else if (c == Eof) { Error("eof-in-comment"); EmitComment(); EmitEof(); }
                    else { _commentData.Append("--"); Reconsume(); _state = S.Comment; }
                    break;
                case S.CommentEndBang:
                    c = Consume();
                    if (c == '-') { _commentData.Append("--!"); _state = S.CommentEndDash; }
                    else if (c == '>') { Error("incorrectly-closed-comment"); _state = S.Data; EmitComment(); }
                    else if (c == Eof) { Error("eof-in-comment"); EmitComment(); EmitEof(); }
                    else { _commentData.Append("--!"); Reconsume(); _state = S.Comment; }
                    break;
                case S.Doctype:
                    c = Consume();
                    if (IsWs(c)) _state = S.BeforeDoctypeName;
                    else if (c == '>') { Reconsume(); _state = S.BeforeDoctypeName; }
                    else if (c == Eof) { Error("eof-in-doctype"); NewDoctype(); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("missing-whitespace-before-doctype-name"); Reconsume(); _state = S.BeforeDoctypeName; }
                    break;
                case S.BeforeDoctypeName:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == 0) { Error("unexpected-null-character"); NewDoctype(); _doctypeName = new StringBuilder("�"); _state = S.DoctypeName; }
                    else if (c == '>') { Error("missing-doctype-name"); NewDoctype(); _doctype!.ForceQuirks = true; _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); NewDoctype(); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { NewDoctype(); _doctypeName = new StringBuilder().Append(Lower(c)); _state = S.DoctypeName; }
                    break;
                case S.DoctypeName:
                    c = Consume();
                    if (IsWs(c)) _state = S.AfterDoctypeName;
                    else if (c == '>') { _state = S.Data; EmitDoctype(); }
                    else if (c == 0) { Error("unexpected-null-character"); _doctypeName!.Append('�'); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else _doctypeName!.Append(Lower(c));
                    break;
                case S.AfterDoctypeName:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '>') { _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else
                    {
                        Reconsume();
                        if (MatchAhead("PUBLIC", true)) { _pos += 6; _state = S.AfterDoctypePublicKeyword; }
                        else if (MatchAhead("SYSTEM", true)) { _pos += 6; _state = S.AfterDoctypeSystemKeyword; }
                        else
                        {
                            _pos++;
                            Error("invalid-character-sequence-after-doctype-name");
                            _doctype!.ForceQuirks = true;
                            Reconsume();
                            _state = S.BogusDoctype;
                        }
                    }
                    break;
                case S.AfterDoctypePublicKeyword:
                    c = Consume();
                    if (IsWs(c)) _state = S.BeforeDoctypePublicIdentifier;
                    else if (c == '"') { Error("missing-whitespace-after-doctype-public-keyword"); _publicId = new StringBuilder(); _state = S.DoctypePublicIdentifierDoubleQuoted; }
                    else if (c == '\'') { Error("missing-whitespace-after-doctype-public-keyword"); _publicId = new StringBuilder(); _state = S.DoctypePublicIdentifierSingleQuoted; }
                    else if (c == '>') { Error("missing-doctype-public-identifier"); _doctype!.ForceQuirks = true; _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("missing-quote-before-doctype-public-identifier"); _doctype!.ForceQuirks = true; Reconsume(); _state = S.BogusDoctype; }
                    break;
                case S.BeforeDoctypePublicIdentifier:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '"') { _publicId = new StringBuilder(); _state = S.DoctypePublicIdentifierDoubleQuoted; }
                    else if (c == '\'') { _publicId = new StringBuilder(); _state = S.DoctypePublicIdentifierSingleQuoted; }
                    else if (c == '>') { Error("missing-doctype-public-identifier"); _doctype!.ForceQuirks = true; _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("missing-quote-before-doctype-public-identifier"); _doctype!.ForceQuirks = true; Reconsume(); _state = S.BogusDoctype; }
                    break;
                case S.DoctypePublicIdentifierDoubleQuoted:
                case S.DoctypePublicIdentifierSingleQuoted:
                {
                    char q = _state == S.DoctypePublicIdentifierDoubleQuoted ? '"' : '\'';
                    c = Consume();
                    if (c == q) _state = S.AfterDoctypePublicIdentifier;
                    else if (c == 0) { Error("unexpected-null-character"); _publicId!.Append('�'); }
                    else if (c == '>') { Error("abrupt-doctype-public-identifier"); _doctype!.ForceQuirks = true; _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else _publicId!.Append((char)c);
                    break;
                }
                case S.AfterDoctypePublicIdentifier:
                    c = Consume();
                    if (IsWs(c)) _state = S.BetweenDoctypePublicAndSystemIdentifiers;
                    else if (c == '>') { _state = S.Data; EmitDoctype(); }
                    else if (c == '"') { Error("missing-whitespace-between-doctype-public-and-system-identifiers"); _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierDoubleQuoted; }
                    else if (c == '\'') { Error("missing-whitespace-between-doctype-public-and-system-identifiers"); _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierSingleQuoted; }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("missing-quote-before-doctype-system-identifier"); _doctype!.ForceQuirks = true; Reconsume(); _state = S.BogusDoctype; }
                    break;
                case S.BetweenDoctypePublicAndSystemIdentifiers:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '>') { _state = S.Data; EmitDoctype(); }
                    else if (c == '"') { _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierDoubleQuoted; }
                    else if (c == '\'') { _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierSingleQuoted; }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("missing-quote-before-doctype-system-identifier"); _doctype!.ForceQuirks = true; Reconsume(); _state = S.BogusDoctype; }
                    break;
                case S.AfterDoctypeSystemKeyword:
                    c = Consume();
                    if (IsWs(c)) _state = S.BeforeDoctypeSystemIdentifier;
                    else if (c == '"') { Error("missing-whitespace-after-doctype-system-keyword"); _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierDoubleQuoted; }
                    else if (c == '\'') { Error("missing-whitespace-after-doctype-system-keyword"); _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierSingleQuoted; }
                    else if (c == '>') { Error("missing-doctype-system-identifier"); _doctype!.ForceQuirks = true; _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("missing-quote-before-doctype-system-identifier"); _doctype!.ForceQuirks = true; Reconsume(); _state = S.BogusDoctype; }
                    break;
                case S.BeforeDoctypeSystemIdentifier:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '"') { _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierDoubleQuoted; }
                    else if (c == '\'') { _systemId = new StringBuilder(); _state = S.DoctypeSystemIdentifierSingleQuoted; }
                    else if (c == '>') { Error("missing-doctype-system-identifier"); _doctype!.ForceQuirks = true; _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("missing-quote-before-doctype-system-identifier"); _doctype!.ForceQuirks = true; Reconsume(); _state = S.BogusDoctype; }
                    break;
                case S.DoctypeSystemIdentifierDoubleQuoted:
                case S.DoctypeSystemIdentifierSingleQuoted:
                {
                    char q = _state == S.DoctypeSystemIdentifierDoubleQuoted ? '"' : '\'';
                    c = Consume();
                    if (c == q) _state = S.AfterDoctypeSystemIdentifier;
                    else if (c == 0) { Error("unexpected-null-character"); _systemId!.Append('�'); }
                    else if (c == '>') { Error("abrupt-doctype-system-identifier"); _doctype!.ForceQuirks = true; _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else _systemId!.Append((char)c);
                    break;
                }
                case S.AfterDoctypeSystemIdentifier:
                    c = Consume();
                    if (IsWs(c)) { }
                    else if (c == '>') { _state = S.Data; EmitDoctype(); }
                    else if (c == Eof) { Error("eof-in-doctype"); _doctype!.ForceQuirks = true; EmitDoctype(); EmitEof(); }
                    else { Error("unexpected-character-after-doctype-system-identifier"); Reconsume(); _state = S.BogusDoctype; }
                    break;
                case S.BogusDoctype:
                    c = Consume();
                    if (c == '>') { _state = S.Data; EmitDoctype(); }
                    else if (c == 0) Error("unexpected-null-character");
                    else if (c == Eof) { EmitDoctype(); EmitEof(); }
                    break;
                case S.CdataSection:
                    c = Consume();
                    if (c == ']') _state = S.CdataSectionBracket;
                    else if (c == Eof) { Error("eof-in-cdata"); EmitEof(); }
                    else EmitChar(c);
                    break;
                case S.CdataSectionBracket:
                    c = Consume();
                    if (c == ']') _state = S.CdataSectionEnd;
                    else { EmitChar(']'); Reconsume(); _state = S.CdataSection; }
                    break;
                case S.CdataSectionEnd:
                    c = Consume();
                    if (c == ']') EmitChar(']');
                    else if (c == '>') _state = S.Data;
                    else { EmitChars("]]"); Reconsume(); _state = S.CdataSection; }
                    break;
                case S.CharacterReference:
                    _temp.Clear();
                    _temp.Append('&');
                    c = Consume();
                    if (IsAlnum(c)) { Reconsume(); _state = S.NamedCharacterReference; }
                    else if (c == '#') { _temp.Append('#'); _state = S.NumericCharacterReference; }
                    else { FlushCharRef(); Reconsume(); _state = _returnState; }
                    break;
                case S.NamedCharacterReference:
                    NamedCharacterReference();
                    break;
                case S.AmbiguousAmpersand:
                    c = Consume();
                    if (IsAlnum(c))
                    {
                        if (ConsumedAsPartOfAttribute) AppendAttrValue((char)c);
                        else EmitChar(c);
                    }
                    else if (c == ';') { Error("unknown-named-character-reference"); Reconsume(); _state = _returnState; }
                    else { Reconsume(); _state = _returnState; }
                    break;
                case S.NumericCharacterReference:
                    _charRefCode = 0;
                    c = Consume();
                    if (c == 'x' || c == 'X') { _temp.Append((char)c); _state = S.HexadecimalCharacterReferenceStart; }
                    else { Reconsume(); _state = S.DecimalCharacterReferenceStart; }
                    break;
                case S.HexadecimalCharacterReferenceStart:
                    c = Consume();
                    if (c >= 0 && CharUtil.IsAsciiHex((char)c)) { Reconsume(); _state = S.HexadecimalCharacterReference; }
                    else { Error("absence-of-digits-in-numeric-character-reference"); FlushCharRef(); Reconsume(); _state = _returnState; }
                    break;
                case S.DecimalCharacterReferenceStart:
                    c = Consume();
                    if (c >= '0' && c <= '9') { Reconsume(); _state = S.DecimalCharacterReference; }
                    else { Error("absence-of-digits-in-numeric-character-reference"); FlushCharRef(); Reconsume(); _state = _returnState; }
                    break;
                case S.HexadecimalCharacterReference:
                    c = Consume();
                    if (c >= '0' && c <= '9') AddCode(16, c - '0');
                    else if (c >= 'A' && c <= 'F') AddCode(16, c - 'A' + 10);
                    else if (c >= 'a' && c <= 'f') AddCode(16, c - 'a' + 10);
                    else if (c == ';') _state = S.NumericCharacterReferenceEnd;
                    else { Error("missing-semicolon-after-character-reference"); Reconsume(); _state = S.NumericCharacterReferenceEnd; }
                    break;
                case S.DecimalCharacterReference:
                    c = Consume();
                    if (c >= '0' && c <= '9') AddCode(10, c - '0');
                    else if (c == ';') _state = S.NumericCharacterReferenceEnd;
                    else { Error("missing-semicolon-after-character-reference"); Reconsume(); _state = S.NumericCharacterReferenceEnd; }
                    break;
                case S.NumericCharacterReferenceEnd:
                    NumericCharacterReferenceEnd();
                    break;
            }
        }

        private void AddCode(int radix, int digit)
        {
            if (_charRefCode <= 0x10FFFF) _charRefCode = _charRefCode * radix + digit; // saturates above the Unicode range
            if (_charRefCode > 0x10FFFF) _charRefCode = 0x110000;
        }

        private void EndTagNameInRawState(S rawState)
        {
            int c = Consume();
            if (IsWs(c) && IsAppropriateEndTag()) { _state = S.BeforeAttributeName; return; }
            if (c == '/' && IsAppropriateEndTag()) { _state = S.SelfClosingStartTag; return; }
            if (c == '>' && IsAppropriateEndTag()) { _state = S.Data; EmitTag(); return; }
            if (IsAlpha(c))
            {
                _tagName.Append(Lower(c));
                _temp.Append((char)c);
                return;
            }
            EmitChars("</");
            EmitChars(_temp.ToString());
            _tag = null;
            Reconsume();
            _state = rawState;
        }

        private void NamedCharacterReference()
        {
            // longest prefix of the input that is an entity name
            int start = _pos;
            int i = start;
            int matchEnd = -1;
            string? value = null;
            while (i < _len && i - start < HtmlEntities.MaxNameLength)
            {
                var span = _in.AsSpan(start, i - start + 1);
                if (!HtmlEntities.AnyStartsWith(span)) break;
                i++;
                if (HtmlEntities.TryGet(_in.AsSpan(start, i - start), out var v))
                {
                    matchEnd = i;
                    value = v;
                }
            }
            if (matchEnd >= 0)
            {
                _temp.Append(_in, start, matchEnd - start);
                _pos = matchEnd;
                char last = _in[matchEnd - 1];
                int next = Peek();
                if (ConsumedAsPartOfAttribute && last != ';' && (next == '=' || IsAlnum(next)))
                {
                    FlushCharRef();
                    _state = _returnState;
                    return;
                }
                if (last != ';') Error("missing-semicolon-after-character-reference");
                _temp.Clear();
                _temp.Append(value);
                FlushCharRef();
                _state = _returnState;
                return;
            }
            FlushCharRef();
            _state = S.AmbiguousAmpersand;
        }

        private static readonly int[] s_c1Replacements =
        {
            0x20AC, 0x81, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021, 0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x8D, 0x017D, 0x8F,
            0x90, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014, 0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x9D, 0x017E, 0x0178,
        };

        private void NumericCharacterReferenceEnd()
        {
            int code = _charRefCode;
            if (code == 0) { Error("null-character-reference"); code = 0xFFFD; }
            else if (code > 0x10FFFF) { Error("character-reference-outside-unicode-range"); code = 0xFFFD; }
            else if (code >= 0xD800 && code <= 0xDFFF) { Error("surrogate-character-reference"); code = 0xFFFD; }
            else if (IsNoncharacter(code)) Error("noncharacter-character-reference");
            else if (code == 0x0D || (IsControl(code) && !IsWs(code)))
            {
                Error("control-character-reference");
                if (code >= 0x80 && code <= 0x9F) code = s_c1Replacements[code - 0x80];
            }
            _temp.Clear();
            if (code < 0x10000) _temp.Append((char)code);
            else _temp.Append(char.ConvertFromUtf32(code));
            FlushCharRef();
            _state = _returnState;
        }

        private static bool IsNoncharacter(int c) => (c >= 0xFDD0 && c <= 0xFDEF) || (c & 0xFFFE) == 0xFFFE;

        private static bool IsControl(int c) => (c >= 0 && c <= 0x1F) || (c >= 0x7F && c <= 0x9F);
    }
}
