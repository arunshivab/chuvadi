using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Css
{
    /// <summary>CSS Syntax Level 3 parser (with nesting-aware block contents). Never throws on malformed CSS.</summary>
    public sealed class CssParser
    {
        private readonly List<CssToken> _t;
        private int _i;
        private readonly int _maxDepth;
        private readonly NodeBudget _budget;
        private CancellationPoll _poll;
        private int _depth;

        private CssParser(string css, MarkupLimits limits, CancellationToken ct)
        {
            limits.Validate();
            Guard.InputLength(css.Length, limits);
            _t = new CssTokenizer(css, ct).TokenizeAll();
            _maxDepth = limits.MaxNestingDepth;
            _budget = new NodeBudget(limits.MaxNodes);
            _poll = new CancellationPoll(ct);
        }

        /// <summary>Parses a style sheet (the contents of a <c>&lt;style&gt;</c> element or a .css file).</summary>
        public static CssStyleSheet ParseStyleSheet(string css, MarkupLimits? limits = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(css);
            var p = new CssParser(css, limits ?? new MarkupLimits(), cancellationToken);
            var sheet = new CssStyleSheet();
            p.ConsumeRuleList(sheet.Rules, topLevel: true);
            return sheet;
        }

        /// <summary>Parses a declaration list (the value of a <c>style</c> attribute). Nested rules are discarded.</summary>
        public static List<CssDeclaration> ParseDeclarations(string css, MarkupLimits? limits = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(css);
            var p = new CssParser(css, limits ?? new MarkupLimits(), cancellationToken);
            var decls = new List<CssDeclaration>();
            p.ConsumeBlockContents(decls, new List<CssRule>(), insideBlock: false);
            return decls;
        }

        private CssToken Peek() => _i < _t.Count ? _t[_i] : s_eof;

        private CssToken Next() => _i < _t.Count ? _t[_i++] : s_eof;

        private static readonly CssToken s_eof = new CssToken(CssTokenType.EndOfFile);

        private void ConsumeRuleList(List<CssRule> rules, bool topLevel)
        {
            while (true)
            {
                _poll.Tick();
                var t = Peek();
                switch (t.Type)
                {
                    case CssTokenType.Whitespace:
                        _i++;
                        continue;
                    case CssTokenType.EndOfFile:
                        return;
                    case CssTokenType.Cdo:
                    case CssTokenType.Cdc:
                        _i++;
                        if (topLevel) continue;
                        break;
                    case CssTokenType.AtKeyword:
                    {
                        var r = ConsumeAtRule(nested: false);
                        if (r is not null) rules.Add(r);
                        continue;
                    }
                }
                var q = ConsumeQualifiedRule(nested: false, stop: CssTokenType.EndOfFile);
                if (q is not null) rules.Add(q);
            }
        }

        private CssAtRule? ConsumeAtRule(bool nested)
        {
            var at = Next();
            _budget.Take();
            var rule = new CssAtRule(at.Value);
            while (true)
            {
                _poll.Tick();
                var t = Peek();
                switch (t.Type)
                {
                    case CssTokenType.Semicolon:
                        _i++;
                        return rule;
                    case CssTokenType.EndOfFile:
                        return rule;
                    case CssTokenType.CloseCurly:
                        if (nested) return rule;
                        _i++;
                        rule.Prelude.Add(t);
                        continue;
                    case CssTokenType.OpenCurly:
                        _i++;
                        rule.HasBlock = true;
                        ConsumeBlockContents(rule.Declarations, rule.Rules, insideBlock: true);
                        return rule;
                    default:
                        rule.Prelude.Add(ConsumeComponentValue());
                        continue;
                }
            }
        }

        private CssStyleRule? ConsumeQualifiedRule(bool nested, CssTokenType stop)
        {
            _budget.Take();
            var rule = new CssStyleRule();
            while (true)
            {
                _poll.Tick();
                var t = Peek();
                if (t.Type == CssTokenType.EndOfFile) return null;
                if (t.Type == stop && stop != CssTokenType.EndOfFile)
                {
                    _i++;
                    return null;
                }
                if (t.Type == CssTokenType.CloseCurly)
                {
                    if (nested) return null;
                    _i++;
                    rule.Prelude.Add(t);
                    continue;
                }
                if (t.Type == CssTokenType.OpenCurly)
                {
                    _i++;
                    ConsumeBlockContents(rule.Declarations, rule.Rules, insideBlock: true);
                    return rule;
                }
                rule.Prelude.Add(ConsumeComponentValue());
            }
        }

        /// <summary>Consumes declarations and nested rules up to the closing '}' (or EOF).</summary>
        private void ConsumeBlockContents(List<CssDeclaration> decls, List<CssRule> rules, bool insideBlock)
        {
            if (++_depth > _maxDepth)
            {
                SkipBalanced('}');
                _depth--;
                return;
            }
            try
            {
                while (true)
                {
                    _poll.Tick();
                    var t = Peek();
                    switch (t.Type)
                    {
                        case CssTokenType.Whitespace:
                        case CssTokenType.Semicolon:
                            _i++;
                            continue;
                        case CssTokenType.EndOfFile:
                            return;
                        case CssTokenType.CloseCurly:
                            _i++;
                            if (insideBlock) return;
                            continue;
                        case CssTokenType.AtKeyword:
                        {
                            var r = ConsumeAtRule(nested: true);
                            if (r is not null) rules.Add(r);
                            continue;
                        }
                    }
                    int mark = _i;
                    var decl = ConsumeDeclaration();
                    if (decl is not null)
                    {
                        decls.Add(decl);
                        continue;
                    }
                    _i = mark;
                    var q = ConsumeQualifiedRule(nested: true, stop: CssTokenType.Semicolon);
                    if (q is not null) rules.Add(q);
                }
            }
            finally
            {
                _depth--;
            }
        }

        private CssDeclaration? ConsumeDeclaration()
        {
            var t = Peek();
            if (t.Type != CssTokenType.Ident)
            {
                return null;
            }
            _i++;
            while (Peek().Type == CssTokenType.Whitespace) _i++;
            if (Peek().Type != CssTokenType.Colon)
            {
                return null;
            }
            _i++;
            string name = t.Value.StartsWith("--", StringComparison.Ordinal) ? t.Value : t.Value.ToLowerInvariant();
            _budget.Take();
            var decl = new CssDeclaration(name);
            while (Peek().Type == CssTokenType.Whitespace) _i++;
            while (true)
            {
                _poll.Tick();
                var n = Peek();
                if (n.Type is CssTokenType.EndOfFile or CssTokenType.Semicolon or CssTokenType.CloseCurly) break;
                if (n.Type == CssTokenType.OpenCurly && !decl.IsCustomProperty)
                {
                    // a {} block in a non-custom declaration means this was really a nested rule
                    return null;
                }
                decl.Value.Add(ConsumeComponentValue());
            }
            // trailing whitespace and !important
            TrimTrailingWhitespace(decl.Value);
            int c = decl.Value.Count;
            if (c >= 2 && decl.Value[c - 1] is CssToken { Type: CssTokenType.Ident } imp && imp.Value.Equals("important", StringComparison.OrdinalIgnoreCase))
            {
                int j = c - 2;
                while (j >= 0 && decl.Value[j] is CssToken { Type: CssTokenType.Whitespace }) j--;
                if (j >= 0 && decl.Value[j] is CssToken { Type: CssTokenType.Delim, Value: "!" })
                {
                    decl.Important = true;
                    decl.Value.RemoveRange(j, c - j);
                    TrimTrailingWhitespace(decl.Value);
                }
            }
            return decl;
        }

        private static void TrimTrailingWhitespace(List<CssComponentValue> v)
        {
            while (v.Count > 0 && v[v.Count - 1] is CssToken { Type: CssTokenType.Whitespace }) v.RemoveAt(v.Count - 1);
        }

        private CssComponentValue ConsumeComponentValue()
        {
            var t = Next();
            switch (t.Type)
            {
                case CssTokenType.OpenCurly:
                case CssTokenType.OpenSquare:
                case CssTokenType.OpenParen:
                    return ConsumeSimpleBlock(t.Value[0]);
                case CssTokenType.Function:
                    return ConsumeFunction(t.Value);
                default:
                    return t;
            }
        }

        private CssComponentValue ConsumeSimpleBlock(char open)
        {
            _budget.Take();
            var block = new CssSimpleBlock(open);
            if (++_depth > _maxDepth)
            {
                SkipBalanced(block.Close);
                _depth--;
                return block;
            }
            try
            {
                var close = open == '{' ? CssTokenType.CloseCurly : open == '[' ? CssTokenType.CloseSquare : CssTokenType.CloseParen;
                while (true)
                {
                    _poll.Tick();
                    var t = Peek();
                    if (t.Type == CssTokenType.EndOfFile) return block;
                    if (t.Type == close)
                    {
                        _i++;
                        return block;
                    }
                    block.Values.Add(ConsumeComponentValue());
                }
            }
            finally
            {
                _depth--;
            }
        }

        private CssComponentValue ConsumeFunction(string name)
        {
            _budget.Take();
            var fn = new CssFunction(name);
            if (++_depth > _maxDepth)
            {
                SkipBalanced(')');
                _depth--;
                return fn;
            }
            try
            {
                while (true)
                {
                    _poll.Tick();
                    var t = Peek();
                    if (t.Type == CssTokenType.EndOfFile) return fn;
                    if (t.Type == CssTokenType.CloseParen)
                    {
                        _i++;
                        return fn;
                    }
                    fn.Arguments.Add(ConsumeComponentValue());
                }
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Skips tokens up to and including the matching close (iteratively), used past the depth limit.</summary>
        private void SkipBalanced(char close)
        {
            var stack = new Stack<CssTokenType>();
            stack.Push(close == '}' ? CssTokenType.CloseCurly : close == ']' ? CssTokenType.CloseSquare : CssTokenType.CloseParen);
            while (stack.Count > 0)
            {
                var t = Next();
                switch (t.Type)
                {
                    case CssTokenType.EndOfFile:
                        return;
                    case CssTokenType.OpenCurly: stack.Push(CssTokenType.CloseCurly); break;
                    case CssTokenType.OpenSquare: stack.Push(CssTokenType.CloseSquare); break;
                    case CssTokenType.OpenParen:
                    case CssTokenType.Function: stack.Push(CssTokenType.CloseParen); break;
                    default:
                        if (t.Type == stack.Peek()) stack.Pop();
                        break;
                }
            }
        }
    }
}
