using System;
using System.Collections.Generic;
using Chuvadi.Markup.Css;

namespace Chuvadi.Markup.Sanitization
{
    /// <summary>Sanitizes CSS declarations and style sheets for the HTML sanitizer.</summary>
    internal sealed class CssSanitizer
    {
        private readonly SanitizerEngine _engine;
        private readonly SanitizerPolicy _p;
        private bool _underScopeRoot;

        public CssSanitizer(SanitizerEngine engine, SanitizerPolicy policy)
        {
            _engine = engine;
            _p = policy;
        }

        public List<CssDeclaration> SanitizeDeclarations(List<CssDeclaration> decls, string element, bool fontFace = false)
        {
            var result = new List<CssDeclaration>(decls.Count);
            foreach (var d in decls)
            {
                if (SanitizeDeclaration(d, element, fontFace)) result.Add(d);
                else _engine.Record(SanitizerActionKind.RemovedCssDeclaration, element, d.Name, null);
            }
            return result;
        }

        private bool SanitizeDeclaration(CssDeclaration d, string element, bool fontFace)
        {
            string name = d.Name;
            if (d.IsCustomProperty)
            {
                if (!_p.AllowCssCustomProperties) return false;
            }
            else
            {
                if (_p.DeniedCssProperties.Contains(name)) return false;
                if (_p.AllowedCssProperties is not null && !_p.AllowedCssProperties.Contains(name)) return false;
            }
            if (!_p.AllowFixedPositioning && name == "position")
            {
                foreach (var v in d.Value)
                    if (v is CssToken { Type: CssTokenType.Ident } id && (id.Value.Equals("fixed", StringComparison.OrdinalIgnoreCase) || id.Value.Equals("sticky", StringComparison.OrdinalIgnoreCase)))
                        return false;
            }
            if (!fontFace) RenameReferences(d);
            var kind = fontFace && name == "src" ? ResourceKind.Font : ResourceKind.Image;
            if (kind == ResourceKind.Image && _p.ReplaceBlockedCssImagesWithNone && !d.IsCustomProperty && s_noneImageProperties.Contains(StripVendor(name)))
                ReplaceBlockedTopLevelImages(d.Value, element);
            return SanitizeValues(d.Value, element, kind, 0);
        }

        // Properties whose image items accept the keyword 'none' in place of an image.
        private static readonly HashSet<string> s_noneImageProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "background", "background-image", "list-style", "list-style-image", "border-image", "border-image-source",
            "mask", "mask-image", "mask-border", "mask-border-source", "content", "shape-outside",
        };

        /// <summary>Replaces top-level url(...) items whose URL would be blocked with the identifier 'none'.</summary>
        private void ReplaceBlockedTopLevelImages(List<CssComponentValue> values, string element)
        {
            for (int i = 0; i < values.Count; i++)
            {
                string? url = values[i] switch
                {
                    CssToken { Type: CssTokenType.Url } t => t.Value,
                    CssFunction f when f.Name.Equals("url", StringComparison.OrdinalIgnoreCase) => SingleString(f.Arguments),
                    _ => null,
                };
                if (url is null) continue;
                string? r = _engine.CheckResource(url, ResourceKind.Image, element, "style");
                if (r is null) values[i] = CssToken.Ident("none");
                else if (r != url) values[i] = new CssToken(CssTokenType.Url, r);
            }
        }

        private static string? SingleString(List<CssComponentValue> args)
        {
            string? s = null;
            foreach (var a in args)
            {
                if (IsWs(a)) continue;
                if (s is not null || a is not CssToken { Type: CssTokenType.String } t) return null;
                s = t.Value;
            }
            return s;
        }

        /// <summary>Checks (and rewrites URLs in) a component value list; false means drop the declaration.</summary>
        public bool SanitizeValues(List<CssComponentValue> values, string element, ResourceKind kind, int urlStringsDepth)
        {
            for (int i = 0; i < values.Count; i++)
            {
                switch (values[i])
                {
                    case CssToken t:
                        switch (t.Type)
                        {
                            case CssTokenType.Url:
                            {
                                string? r = _engine.CheckResource(t.Value, kind, element, "style");
                                if (r is null) return false;
                                if (!ReferenceEquals(r, t.Value) && r != t.Value) values[i] = new CssToken(CssTokenType.Url, r);
                                break;
                            }
                            case CssTokenType.String when urlStringsDepth > 0:
                            {
                                string? r = _engine.CheckResource(t.Value, kind, element, "style");
                                if (r is null) return false;
                                if (r != t.Value) values[i] = CssToken.StringToken(r);
                                break;
                            }
                            case CssTokenType.BadUrl:
                            case CssTokenType.BadString:
                            case CssTokenType.Cdo:
                            case CssTokenType.Cdc:
                                return false;
                            case CssTokenType.Delim when t.Value == "<":
                                return false;
                        }
                        break;
                    case CssFunction f:
                    {
                        string n = f.Name.ToLowerInvariant();
                        string bare = StripVendor(n);
                        if (_p.DeniedCssFunctions.Contains(n) || _p.DeniedCssFunctions.Contains(bare)) return false;
                        if (bare == "var" && !_p.AllowCssCustomProperties) return false;
                        if (bare is "url" or "src")
                        {
                            // url("...") / src("..."): the string argument is the URL
                            if (!SanitizeValues(f.Arguments, element, kind, urlStringsDepth + 1)) return false;
                            break;
                        }
                        bool urlStrings = bare is "image-set" or "cross-fade" or "image";
                        if (!SanitizeValues(f.Arguments, element, kind, urlStrings ? urlStringsDepth + 1 : urlStringsDepth)) return false;
                        break;
                    }
                    case CssSimpleBlock b:
                        if (!SanitizeValues(b.Values, element, kind, urlStringsDepth)) return false;
                        break;
                }
            }
            return true;
        }

        // ---------------- document-global names (security review F1, F3) ----------------
        // @font-face families, @keyframes and @counter-style names are global to the host document, so a
        // content style sheet could restyle host UI that uses the same name. Every name the content declares
        // is moved into the content's namespace (NamePrefix + name), and every reference to a declared name in
        // the same sheets, style attributes and SVG font-family attributes is rewritten to match. Names that
        // already carry the prefix are left alone, so the rewrite is idempotent (stability passes).

        private readonly HashSet<string> _fontFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keyframes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _counterStyles = new HashSet<string>(StringComparer.Ordinal);

        internal string NamePrefix =>
            !string.IsNullOrEmpty(_p.IdPrefix) ? _p.IdPrefix
            : SanitizerEngine.IsValidClass(_p.ScopeClass) ? _p.ScopeClass + "-"
            : "chuvadi-";

        private string Prefixed(string name) => name.StartsWith(NamePrefix, StringComparison.Ordinal) ? name : NamePrefix + name;

        /// <summary>Records the global names a style sheet declares (only in at-rules the policy keeps).</summary>
        public void CollectDeclaredNames(string css)
        {
            var sheet = CssParser.ParseStyleSheet(css, _p.Limits);
            var stack = new Stack<List<CssRule>>();
            stack.Push(sheet.Rules);
            while (stack.Count > 0)
            {
                foreach (var rule in stack.Pop())
                {
                    if (rule is CssStyleRule sr)
                    {
                        stack.Push(sr.Rules);
                        continue;
                    }
                    if (rule is not CssAtRule at) continue;
                    string n = at.Name.ToLowerInvariant();
                    if (!_p.AllowedAtRules.Contains(n)) continue;
                    if (n == "font-face")
                    {
                        foreach (var d in at.Declarations)
                            if (d.Name == "font-family" && FamilyName(d.Value, 0, d.Value.Count) is { } fam) _fontFamilies.Add(fam);
                    }
                    else if (n.EndsWith("keyframes", StringComparison.Ordinal) || n == "counter-style")
                    {
                        if (SingleName(at.Prelude) is { } name) (n == "counter-style" ? _counterStyles : _keyframes).Add(name);
                    }
                    else stack.Push(at.Rules);
                }
            }
        }

        /// <summary>A family name: one string, or identifiers separated by whitespace (joined by single spaces).</summary>
        private static string? FamilyName(List<CssComponentValue> v, int start, int end)
        {
            while (start < end && IsWs(v[start])) start++;
            while (end > start && IsWs(v[end - 1])) end--;
            if (start >= end) return null;
            if (end - start == 1 && v[start] is CssToken { Type: CssTokenType.String } s) return s.Value;
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < end; i++)
            {
                if (IsWs(v[i])) continue;
                if (v[i] is not CssToken { Type: CssTokenType.Ident } id) return null;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(id.Value);
            }
            return sb.ToString();
        }

        private static string? SingleName(List<CssComponentValue> prelude)
        {
            string? name = null;
            foreach (var v in prelude)
            {
                if (IsWs(v)) continue;
                if (name is not null) return null;
                if (v is CssToken { Type: CssTokenType.Ident or CssTokenType.String } t) name = t.Value;
                else return null;
            }
            return name;
        }

        private bool RenameFontFaceFamily(CssDeclaration d)
        {
            if (FamilyName(d.Value, 0, d.Value.Count) is not { } fam) return false;
            d.Value.Clear();
            d.Value.Add(CssToken.StringToken(Prefixed(fam)));
            return true;
        }

        private bool RenamePrelude(List<CssComponentValue> prelude)
        {
            for (int i = 0; i < prelude.Count; i++)
            {
                if (IsWs(prelude[i])) continue;
                if (prelude[i] is CssToken { Type: CssTokenType.Ident } id)
                    prelude[i] = CssToken.Ident(Prefixed(id.Value));
                else if (prelude[i] is CssToken { Type: CssTokenType.String } st)
                    prelude[i] = CssToken.StringToken(Prefixed(st.Value));
                else return false;
                return true;
            }
            return false;
        }

        /// <summary>Rewrites references to declared global names in one declaration's value.</summary>
        internal void RenameReferences(CssDeclaration d)
        {
            string name = d.IsCustomProperty ? d.Name : StripVendor(d.Name);
            switch (name)
            {
                case "font-family":
                    if (_fontFamilies.Count > 0) RenameFamilies(d.Value, shorthand: false);
                    break;
                case "font":
                    if (_fontFamilies.Count > 0) RenameFamilies(d.Value, shorthand: true);
                    break;
                case "animation-name":
                case "animation":
                    if (_keyframes.Count > 0) RenameIdents(d.Value, _keyframes);
                    break;
                case "list-style-type":
                case "list-style":
                case "system":
                case "fallback":
                    if (_counterStyles.Count > 0) RenameIdents(d.Value, _counterStyles);
                    break;
                case "content":
                    if (_counterStyles.Count > 0) RenameCounterFunctions(d.Value);
                    break;
            }
        }

        private void RenameIdents(List<CssComponentValue> v, HashSet<string> declared)
        {
            for (int i = 0; i < v.Count; i++)
            {
                if (v[i] is CssToken { Type: CssTokenType.Ident } id && declared.Contains(id.Value))
                    v[i] = CssToken.Ident(Prefixed(id.Value));
                else if (v[i] is CssToken { Type: CssTokenType.String } st && declared.Contains(st.Value))
                    v[i] = CssToken.StringToken(Prefixed(st.Value));
            }
        }

        private void RenameCounterFunctions(List<CssComponentValue> v)
        {
            foreach (var c in v)
            {
                if (c is not CssFunction f) continue;
                string n = f.Name.ToLowerInvariant();
                if (n is not ("counter" or "counters")) continue;
                // counter(name, style) / counters(name, string, style): the style is the last argument
                int last = -1;
                for (int i = 0; i < f.Arguments.Count; i++)
                    if (f.Arguments[i] is CssToken { Type: CssTokenType.Comma }) last = i;
                if (last < 0) continue;
                for (int i = last + 1; i < f.Arguments.Count; i++)
                    if (f.Arguments[i] is CssToken { Type: CssTokenType.Ident } id && _counterStyles.Contains(id.Value))
                        f.Arguments[i] = CssToken.Ident(Prefixed(id.Value));
            }
        }

        /// <summary>
        /// Rewrites declared families in a font-family list, or in the family list at the end of the font
        /// shorthand (where the first family is the trailing run of identifiers, or a string, after the size).
        /// </summary>
        private void RenameFamilies(List<CssComponentValue> v, bool shorthand)
        {
            var segments = new List<(int Start, int End)>();
            int segStart = 0;
            for (int i = 0; i <= v.Count; i++)
            {
                if (i == v.Count || v[i] is CssToken { Type: CssTokenType.Comma })
                {
                    segments.Add((segStart, i));
                    segStart = i + 1;
                }
            }
            for (int k = segments.Count - 1; k >= 0; k--)
            {
                var (start, end) = segments[k];
                if (shorthand && k == 0)
                {
                    // the family is the trailing run of identifiers (or the trailing string) of the first segment
                    int e = end;
                    while (e > start && IsWs(v[e - 1])) e--;
                    if (e == start) continue;
                    int b = e;
                    if (v[e - 1] is CssToken { Type: CssTokenType.String }) b = e - 1;
                    else
                        while (b > start && (IsWs(v[b - 1]) || v[b - 1] is CssToken { Type: CssTokenType.Ident })) b--;
                    // the identifiers before the family are keywords (style, weight, size keywords); try each suffix
                    for (int from = b; from < e; from++)
                    {
                        if (IsWs(v[from])) continue;
                        if (FamilyName(v, from, e) is { } fam && _fontFamilies.Contains(fam))
                        {
                            v.RemoveRange(from, e - from);
                            v.Insert(from, CssToken.StringToken(Prefixed(fam)));
                            break;
                        }
                    }
                    continue;
                }
                if (FamilyName(v, start, end) is { } family && _fontFamilies.Contains(family))
                {
                    int s0 = start, e0 = end;
                    while (s0 < e0 && IsWs(v[s0])) s0++;
                    while (e0 > s0 && IsWs(v[e0 - 1])) e0--;
                    v.RemoveRange(s0, e0 - s0);
                    v.Insert(s0, CssToken.StringToken(Prefixed(family)));
                }
            }
        }

        private static string StripVendor(string n)
        {
            foreach (var p in new[] { "-webkit-", "-moz-", "-ms-", "-o-" })
                if (n.StartsWith(p, StringComparison.Ordinal)) return n.Substring(p.Length);
            return n;
        }

        // ---------------- style sheets ----------------

        public void SanitizeSheet(CssStyleSheet sheet, string? scopeClass, bool verification)
        {
            SanitizeRuleList(sheet.Rules, scopeClass, verification, inStyleRule: false, inKeyframes: false);
        }

        private void SanitizeRuleList(List<CssRule> rules, string? scope, bool verification, bool inStyleRule, bool inKeyframes)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                bool keep = rule switch
                {
                    CssAtRule at => SanitizeAtRule(at, scope, verification, inStyleRule),
                    CssStyleRule sr => SanitizeStyleRule(sr, scope, verification, inStyleRule, inKeyframes),
                    _ => false,
                };
                if (!keep)
                {
                    _engine.Record(SanitizerActionKind.RemovedCssRule, "style", null, Truncate(CssSerializer.Serialize(rule)));
                    rules.RemoveAt(i);
                    i--;
                }
            }
        }

        private static string Truncate(string s) => s.Length <= 80 ? s : s.Substring(0, 80);

        private bool SanitizeAtRule(CssAtRule at, string? scope, bool verification, bool inStyleRule)
        {
            string name = at.Name.ToLowerInvariant();
            if (!_p.AllowedAtRules.Contains(name)) return false;
            if (!at.HasBlock) return false;
            if (!PreludeIsSafe(at.Prelude)) return false;
            bool keyframes = name.EndsWith("keyframes", StringComparison.Ordinal);
            bool fontFace = name == "font-face";
            bool counterStyle = name == "counter-style";
            var decls = SanitizeDeclarations(at.Declarations, "style", fontFace);
            at.Declarations.Clear();
            at.Declarations.AddRange(decls);
            if (fontFace)
            {
                // a font face without a usable src is useless; keep it only if src survived
                at.Rules.Clear();
                bool hasSrc = false, hasFamily = false;
                foreach (var d in at.Declarations)
                {
                    if (d.Name == "src") hasSrc = true;
                    if (d.Name == "font-family") hasFamily = RenameFontFaceFamily(d);
                }
                return hasSrc && hasFamily;
            }
            // names that live in document-global namespaces are moved into the content's namespace
            if (keyframes || counterStyle)
            {
                if (!RenamePrelude(at.Prelude)) return false;
            }
            SanitizeRuleList(at.Rules, scope, verification, inStyleRule, keyframes);
            return true;
        }

        private bool SanitizeStyleRule(CssStyleRule rule, string? scope, bool verification, bool inStyleRule, bool inKeyframes)
        {
            if (!PreludeIsSafe(rule.Prelude) || HasEmptySelector(rule.Prelude)) return false;
            // a nested rule under a rule that matches the wrapper itself must not reach the wrapper's siblings
            if (inStyleRule && _underScopeRoot && HasTopLevelSiblingCombinator(rule.Prelude)) return false;
            var decls = SanitizeDeclarations(rule.Declarations, "style");
            rule.Declarations.Clear();
            rule.Declarations.AddRange(decls);
            if (!inKeyframes)
            {
                if (!string.IsNullOrEmpty(_p.IdPrefix)) PrefixIdSelectors(rule.Prelude);
                if (scope is not null && !inStyleRule)
                {
                    var scoped = ScopePrelude(rule.Prelude, scope);
                    if (scoped.Count == 0) return false;
                    rule.Prelude.Clear();
                    rule.Prelude.AddRange(scoped);
                }
            }
            bool saved = _underScopeRoot;
            if (scope is not null && !inStyleRule && !inKeyframes) _underScopeRoot = IsScopeRoot(rule.Prelude, scope);
            try
            {
                SanitizeRuleList(rule.Rules, scope, verification, inStyleRule: true, inKeyframes: false);
            }
            finally
            {
                _underScopeRoot = saved;
            }
            return rule.Declarations.Count > 0 || rule.Rules.Count > 0;
        }

        /// <summary>
        /// True when the selector list is empty or has an empty item ("", "a,,b", "a,"): browsers drop such
        /// rules, so they are dropped here rather than scoped into something valid.
        /// </summary>
        private static bool HasEmptySelector(List<CssComponentValue> prelude)
        {
            bool itemHasContent = false;
            foreach (var v in prelude)
            {
                if (v is CssToken { Type: CssTokenType.Comma })
                {
                    if (!itemHasContent) return true;
                    itemHasContent = false;
                }
                else if (!IsWs(v)) itemHasContent = true;
            }
            return !itemHasContent;
        }

        private static bool PreludeIsSafe(List<CssComponentValue> prelude)
        {
            var stack = new Stack<List<CssComponentValue>>();
            stack.Push(prelude);
            while (stack.Count > 0)
            {
                foreach (var v in stack.Pop())
                {
                    switch (v)
                    {
                        case CssToken t when t.Type is CssTokenType.BadString or CssTokenType.BadUrl or CssTokenType.Url or CssTokenType.Cdo or CssTokenType.Cdc:
                            return false;
                        case CssToken t when t.Type == CssTokenType.Delim && t.Value == "<":
                            return false;
                        case CssFunction f:
                            if (f.Name.Equals("url", StringComparison.OrdinalIgnoreCase)) return false;
                            stack.Push(f.Arguments);
                            break;
                        case CssSimpleBlock b:
                            stack.Push(b.Values);
                            break;
                    }
                }
            }
            return true;
        }

        private void PrefixIdSelectors(List<CssComponentValue> prelude)
        {
            string prefix = _p.IdPrefix!;
            for (int i = 0; i < prelude.Count; i++)
            {
                if (prelude[i] is CssToken { Type: CssTokenType.Hash, IsIdHash: true } h && !h.Value.StartsWith(prefix, StringComparison.Ordinal))
                    prelude[i] = new CssToken(CssTokenType.Hash, prefix + h.Value) { IsIdHash = true };
                else if (prelude[i] is CssFunction f) PrefixIdSelectors(f.Arguments);
            }
        }

        /// <summary>Prefixes each selector in a selector list with the scope class (idempotent).</summary>
        private static List<CssComponentValue> ScopePrelude(List<CssComponentValue> prelude, string scope)
        {
            var result = new List<CssComponentValue>();
            var current = new List<CssComponentValue>();
            bool invalid = false;
            void Flush()
            {
                var sel = ScopeSelector(current, scope);
                if (sel is null) invalid = true;
                else if (sel.Count > 0)
                {
                    if (result.Count > 0)
                    {
                        result.Add(new CssToken(CssTokenType.Comma, ","));
                        result.Add(CssToken.Space());
                    }
                    result.AddRange(sel);
                }
                current.Clear();
            }
            foreach (var v in prelude)
            {
                if (v is CssToken { Type: CssTokenType.Comma }) Flush();
                else current.Add(v);
            }
            Flush();
            return invalid ? new List<CssComponentValue>() : result;
        }

        private static bool IsWs(CssComponentValue v) => v is CssToken { Type: CssTokenType.Whitespace };

        private static List<CssComponentValue>? ScopeSelector(List<CssComponentValue> sel, string scope)
        {
            int s = 0, e = sel.Count;
            while (s < e && IsWs(sel[s])) s++;
            while (e > s && IsWs(sel[e - 1])) e--;
            var rest = sel.GetRange(s, e - s);
            var scopeTokens = new List<CssComponentValue> { CssToken.Delim('.'), CssToken.Ident(scope) };
            if (rest.Count == 0) return rest;
            // a selector cannot start with a combinator; browsers drop it, and scoping would make it valid
            if (IsCombinator(rest[0])) return null;
            // already scoped (verification passes)
            if (rest.Count >= 2 && rest[0] is CssToken { Type: CssTokenType.Delim, Value: "." } && rest[1] is CssToken { Type: CssTokenType.Ident } id0 && id0.Value == scope)
                return ReachesOutsideScope(rest) ? null : rest;
            bool removed = false;
            while (true)
            {
                if (rest.Count > 0 && rest[0] is CssToken { Type: CssTokenType.Ident } id && (id.Value.Equals("html", StringComparison.OrdinalIgnoreCase) || id.Value.Equals("body", StringComparison.OrdinalIgnoreCase)))
                {
                    rest.RemoveAt(0);
                    removed = true;
                }
                else if (rest.Count > 1 && rest[0] is CssToken { Type: CssTokenType.Colon } && rest[1] is CssToken { Type: CssTokenType.Ident } r && r.Value.Equals("root", StringComparison.OrdinalIgnoreCase))
                {
                    rest.RemoveRange(0, 2);
                    removed = true;
                }
                else break;
                // a following compound part (body.x, html[lang]) stays attached to the scope
                if (rest.Count == 0 || !IsWs(rest[0])) break;
                int k = 0;
                while (k < rest.Count && IsWs(rest[k])) k++;
                if (k < rest.Count && rest[k] is CssToken { Type: CssTokenType.Ident } nx && (nx.Value.Equals("body", StringComparison.OrdinalIgnoreCase) || nx.Value.Equals("html", StringComparison.OrdinalIgnoreCase)))
                {
                    rest.RemoveRange(0, k);
                    continue;
                }
                break;
            }
            var output = new List<CssComponentValue>(scopeTokens);
            if (rest.Count == 0) return output;
            bool attached = removed && rest[0] is CssToken t0 && (t0.Type is CssTokenType.Hash or CssTokenType.Colon || (t0.Type == CssTokenType.Delim && t0.Value == ".")) || removed && rest[0] is CssSimpleBlock { Open: '[' };
            if (!attached) output.Add(CssToken.Space());
            output.AddRange(rest);
            return ReachesOutsideScope(output) ? null : output;
        }

        private static bool IsCombinator(CssComponentValue v) =>
            v is CssToken { Type: CssTokenType.Delim } d && d.Value is ">" or "+" or "~" or "|";

        private static bool IsSiblingCombinator(CssComponentValue v) =>
            v is CssToken { Type: CssTokenType.Delim } d && d.Value is "+" or "~";

        /// <summary>
        /// True if a scoped selector puts a sibling combinator after the scope compound itself
        /// (".scope ~ x", ".scope.a + x"): that matches elements outside the wrapper (security review F4).
        /// </summary>
        private static bool ReachesOutsideScope(List<CssComponentValue> scoped)
        {
            int i = 2; // after ". scope"
            while (i < scoped.Count && !IsWs(scoped[i]) && !IsCombinator(scoped[i])) i++;
            while (i < scoped.Count && IsWs(scoped[i])) i++;
            return i < scoped.Count && IsSiblingCombinator(scoped[i]);
        }

        /// <summary>True if any item of a scoped selector list is the scope compound itself (html, body, :root).</summary>
        private static bool IsScopeRoot(List<CssComponentValue> scoped, string scope)
        {
            bool atItemStart = true;
            int i = 0;
            while (i < scoped.Count)
            {
                if (atItemStart && i + 1 < scoped.Count && scoped[i] is CssToken { Type: CssTokenType.Delim, Value: "." } && scoped[i + 1] is CssToken { Type: CssTokenType.Ident } sc && sc.Value == scope)
                {
                    int j = i + 2;
                    while (j < scoped.Count && !IsWs(scoped[j]) && !IsCombinator(scoped[j]) && scoped[j] is not CssToken { Type: CssTokenType.Comma }) j++;
                    while (j < scoped.Count && IsWs(scoped[j])) j++;
                    if (j == scoped.Count || scoped[j] is CssToken { Type: CssTokenType.Comma }) return true;
                }
                atItemStart = scoped[i] is CssToken { Type: CssTokenType.Comma } || (atItemStart && IsWs(scoped[i]));
                i++;
            }
            return false;
        }

        /// <summary>True if a nested selector list uses a sibling combinator at its top level.</summary>
        private static bool HasTopLevelSiblingCombinator(List<CssComponentValue> prelude)
        {
            foreach (var v in prelude)
                if (IsSiblingCombinator(v)) return true;
            return false;
        }
    }
}
