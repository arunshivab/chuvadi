using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Markup.Css;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.Tests.Sanitization
{
    /// <summary>
    /// Checks sanitized output the way a browser would see it: the output is re-parsed (with scripting on and
    /// off) and every element, attribute and CSS value is checked against hard safety rules.
    /// </summary>
    public static class SafetyInvariants
    {
        private static readonly HashSet<string> s_neverElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "script", "iframe", "frame", "frameset", "object", "embed", "applet", "base", "link", "meta", "form",
            "noscript", "template", "title", "head", "html", "body", "xmp", "plaintext", "noembed", "noframes", "param",
            "dialog", "portal", "fencedframe", "isindex", "keygen", "bgsound",
        };

        private static readonly HashSet<string> s_strictNever = new HashSet<string>(StringComparer.Ordinal)
        {
            "input", "button", "select", "option", "textarea", "audio", "video", "source", "track", "svg", "math",
        };

        public static List<string> Check(SanitizeResult result, SanitizerPolicy policy) => Check(result.Html, policy);

        public static List<string> Check(string html, SanitizerPolicy policy)
        {
            var problems = new List<string>();
            foreach (bool scripting in new[] { true, false })
            {
                var frag = HtmlParser.ParseFragment(html, "body", HtmlNamespace.Html, new HtmlParserOptions { Scripting = scripting });
                CheckTree(frag, policy, problems, scripting);
            }
            return problems;
        }

        private static void CheckTree(HtmlNode root, SanitizerPolicy policy, List<string> problems, bool scripting)
        {
            bool strict = !policy.AllowFormControls;
            string scope = policy.ScopeClass ?? "";
            int styleCount = 0;
            foreach (var n in root.Descendants())
            {
                if (n is HtmlComment) problems.Add("comment survived");
                if (n is not HtmlElement e) continue;
                string name = e.LocalName;
                if (e.Namespace == HtmlNamespace.Html)
                {
                    if (name == "style")
                    {
                        styleCount++;
                        if (!(e.Parent is HtmlElement p && p.GetAttribute("class") == scope && ReferenceEquals(p.FirstChild, e)))
                            problems.Add("style element not first child of the wrapper");
                        CheckSheet(e.TextContent, policy, problems);
                        continue;
                    }
                    if (s_neverElements.Contains(name)) problems.Add("forbidden element <" + name + "> (scripting=" + scripting + ")");
                    if (strict && s_strictNever.Contains(name)) problems.Add("strict-forbidden element <" + name + ">");
                }
                else
                {
                    if (strict) problems.Add("foreign element <" + name + "> in strict policy");
                    if (name is "script" or "foreignObject" or "animate" or "set" or "animateTransform" or "animateMotion" or "a" or "annotation-xml" or "style")
                        problems.Add("dangerous foreign element <" + name + ">");
                }
                foreach (var a in e.Attributes)
                {
                    string an = a.Name.ToLowerInvariant();
                    string v = a.Value;
                    // Conservative: drop every C0 control and space anywhere (browsers only strip them at the ends,
                    // plus tab/LF/CR inside). Comparisons are ordinal: a culture-aware StartsWith ignores C1 controls
                    // such as U+0099, which browsers do not ("\u0099javascript:" is a relative URL).
                    string norm = new string(v.Where(c => c > ' ').ToArray()).ToLowerInvariant();
                    if (an.StartsWith("on", StringComparison.Ordinal)) problems.Add("event handler " + an + " on <" + name + ">");
                    if (an is "srcset" or "action" or "formaction" or "srcdoc" or "dynsrc" or "lowsrc" or "ping" or "autofocus" or "accesskey" or "contenteditable" or "popover" or "form")
                        problems.Add("forbidden attribute " + an + " on <" + name + ">");
                    if (an is "src" or "background" or "poster" || ((an is "href" or "xlink:href") && e.Namespace == HtmlNamespace.Svg))
                    {
                        if (!(norm.StartsWith("data:image/", StringComparison.Ordinal) || norm.StartsWith("#", StringComparison.Ordinal) || norm.StartsWith("data:audio/", StringComparison.Ordinal) || norm.StartsWith("data:video/", StringComparison.Ordinal)))
                            problems.Add("resource URL " + an + "=" + v + " on <" + name + ">");
                        if (norm.StartsWith("data:image/svg", StringComparison.Ordinal) && strict) problems.Add("svg data image in strict policy");
                    }
                    if (an == "href" && e.Namespace == HtmlNamespace.Html)
                    {
                        if (norm.StartsWith("javascript:", StringComparison.Ordinal) || norm.StartsWith("vbscript:", StringComparison.Ordinal) || norm.StartsWith("data:", StringComparison.Ordinal) || norm.StartsWith("file:", StringComparison.Ordinal) || norm.StartsWith("//", StringComparison.Ordinal))
                            problems.Add("dangerous href " + v);
                        if (policy.Links == LinkHandling.Inert && !norm.StartsWith("#", StringComparison.Ordinal)) problems.Add("navigable link in inert policy: " + v);
                    }
                    if (an == "target" && v != "_blank" && v != "_self") problems.Add("target " + v);
                    if (an == "style") CheckDeclarations(CssParser.ParseDeclarations(v), policy, problems);
                    if (policy.InertLinkAttribute is not null && an == policy.InertLinkAttribute && name is not ("a" or "area"))
                        problems.Add("reserved attribute on <" + name + ">");
                    if (policy.InertLinkAttribute is not null && an == policy.InertLinkAttribute && (norm.StartsWith("javascript:", StringComparison.Ordinal) || norm.StartsWith("data:", StringComparison.Ordinal)))
                        problems.Add("reserved attribute carries dangerous url " + v);
                    if (an is "id" or "name" && !string.IsNullOrEmpty(policy.IdPrefix) && e.Namespace == HtmlNamespace.Html && name is not ("input" or "select" or "textarea" or "button" or "output") && !v.StartsWith(policy.IdPrefix))
                        problems.Add("unprefixed " + an + "=" + v);
                }
            }
            if (styleCount > 1) problems.Add("more than one style element");
        }

        private static string NamePrefix(SanitizerPolicy policy) =>
            !string.IsNullOrEmpty(policy.IdPrefix) ? policy.IdPrefix : policy.ScopeClass is { Length: > 0 } sc ? sc + "-" : "chuvadi-";

        private static void CheckSheet(string css, SanitizerPolicy policy, List<string> problems)
        {
            if (css.Contains('<')) problems.Add("'<' inside style element");
            var sheet = CssParser.ParseStyleSheet(css);
            var rules = new Stack<(CssRule rule, bool keyframes, bool nested)>();
            foreach (var r in sheet.Rules) rules.Push((r, false, false));
            while (rules.Count > 0)
            {
                var (r, kf, nested) = rules.Pop();
                if (r is CssAtRule at)
                {
                    string n = at.Name.ToLowerInvariant();
                    if (n is "import" or "namespace" or "charset" or "document" or "-moz-document" or "page" or "layer" or "property" or "scope" or "font-feature-values" or "font-palette-values" or "view-transition")
                        problems.Add("at-rule @" + n);
                    string prefix = NamePrefix(policy);
                    if (n == "font-face")
                        foreach (var d in at.Declarations)
                            if (d.Name == "font-family" && !d.ValueText.Trim().Trim('"', '\'').StartsWith(prefix, StringComparison.Ordinal))
                                problems.Add("unprefixed font-face family " + d.ValueText);
                    if ((n.EndsWith("keyframes", StringComparison.Ordinal) || n == "counter-style") && !at.PreludeText.Trim().Trim('"', '\'').StartsWith(prefix, StringComparison.Ordinal))
                        problems.Add("unprefixed @" + n + " name " + at.PreludeText);
                    CheckDeclarations(at.Declarations, policy, problems, n == "font-face");
                    foreach (var c in at.Rules) rules.Push((c, n.EndsWith("keyframes", StringComparison.Ordinal), nested));
                }
                else
                {
                    if (!kf && !nested && policy.StyleSheets == StyleSheetHandling.SanitizeScoped)
                    {
                        foreach (var sel in r.PreludeText.Split(','))
                        {
                            string t = sel.Trim();
                            if (!t.StartsWith("." + policy.ScopeClass, StringComparison.Ordinal))
                                problems.Add("unscoped selector " + sel);
                            // the scope compound must not be followed by a sibling combinator (matches outside the wrapper)
                            var m = System.Text.RegularExpressions.Regex.Match(t, @"^\.[^\s>+~]+\s*([+~])");
                            if (m.Success) problems.Add("selector reaches outside the scope: " + t);
                        }
                    }
                    CheckDeclarations(r.Declarations, policy, problems);
                    foreach (var c in r.Rules) rules.Push((c, false, true));
                }
            }
        }

        private static void CheckDeclarations(List<CssDeclaration> decls, SanitizerPolicy policy, List<string> problems, bool fontFace = false)
        {
            foreach (var d in decls)
            {
                if (policy.DeniedCssProperties.Contains(d.Name)) problems.Add("denied css property " + d.Name);
                if (d.IsCustomProperty && !policy.AllowCssCustomProperties) problems.Add("custom property " + d.Name);
                if (!policy.AllowFixedPositioning && d.Name == "position" && (d.ValueText.Contains("fixed", StringComparison.OrdinalIgnoreCase) || d.ValueText.Contains("sticky", StringComparison.OrdinalIgnoreCase)))
                    problems.Add("fixed positioning");
                CheckValues(d.Value, policy, problems, fontFace && d.Name == "src", false);
            }
        }

        private static void CheckValues(List<CssComponentValue> values, SanitizerPolicy policy, List<string> problems, bool font, bool urlStrings)
        {
            foreach (var v in values)
            {
                switch (v)
                {
                    case CssToken t when t.Type == CssTokenType.Url || (urlStrings && t.Type == CssTokenType.String):
                    {
                        string u = t.Value.Trim().ToLowerInvariant();
                        if (!(u.StartsWith("data:image/", StringComparison.Ordinal) || u.StartsWith("#", StringComparison.Ordinal) || (font && (u.StartsWith("data:font/", StringComparison.Ordinal) || u.StartsWith("data:application/", StringComparison.Ordinal)))))
                            problems.Add("css url " + t.Value);
                        break;
                    }
                    case CssFunction f:
                    {
                        string n = f.Name.ToLowerInvariant();
                        if (n.Contains("expression") || n == "element" || n == "paint") problems.Add("css function " + n);
                        if (n == "var" && !policy.AllowCssCustomProperties) problems.Add("var()");
                        CheckValues(f.Arguments, policy, problems, font, urlStrings || n is "url" or "src" || n.EndsWith("image-set", StringComparison.Ordinal) || n.EndsWith("cross-fade", StringComparison.Ordinal) || n == "image");
                        break;
                    }
                    case CssSimpleBlock b:
                        CheckValues(b.Values, policy, problems, font, urlStrings);
                        break;
                }
            }
        }
    }
}
