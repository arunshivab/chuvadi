using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Css;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Sanitization
{
    /// <summary>One sanitize pass over a DOM.</summary>
    internal sealed class SanitizerEngine
    {
        private readonly SanitizerPolicy _p;
        private readonly bool _verification;
        private readonly HashSet<string> _trustedUrls;
        private CancellationPoll _poll;
        private readonly CssSanitizer _css;
        private readonly List<SanitizerAction> _actions;
        private readonly List<BlockedResource> _blocked;
        private readonly HashSet<(string, ResourceKind)> _blockedSeen = new HashSet<(string, ResourceKind)>();
        private int _actionCount;
        private readonly CancellationToken _ct;

        public SanitizerEngine(SanitizerPolicy policy, bool verification, HashSet<string> trustedUrls, CancellationToken ct,
            List<SanitizerAction> actions, List<BlockedResource> blocked)
        {
            _p = policy;
            _verification = verification;
            _trustedUrls = trustedUrls;
            _ct = ct;
            _poll = new CancellationPoll(ct);
            _css = new CssSanitizer(this, policy);
            _actions = actions;
            _blocked = blocked;
        }

        public int ActionCount => _actionCount;

        public void Record(SanitizerActionKind kind, string element, string? attribute, string? detail)
        {
            if (_verification) return;
            _actionCount++;
            if (_actions.Count < _p.Limits.MaxRecordedErrors) _actions.Add(new SanitizerAction(kind, element, attribute, detail));
        }

        private string? ScopeClass => _p.StyleSheets == StyleSheetHandling.SanitizeScoped && IsValidClass(_p.ScopeClass) ? _p.ScopeClass : null;

        internal static bool IsValidClass(string? c)
        {
            if (string.IsNullOrEmpty(c) || char.IsAsciiDigit(c[0])) return false;
            foreach (char ch in c)
                if (!(char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_')) return false;
            return true;
        }

        // ---------------- entry ----------------

        /// <summary>Sanitizes a parsed document into a fragment (wrapper, scoped styles, body content).</summary>
        public HtmlDocumentFragment RunDocument(HtmlDocument doc)
        {
            var output = new HtmlDocumentFragment();
            var sheets = new List<string>();
            CollectStyleSheets(doc, sheets);
            if (doc.Head is { } head)
            {
                foreach (var hn in head.ChildNodes)
                    if (hn is HtmlElement he && !he.IsHtml("title"))
                        Record(SanitizerActionKind.RemovedElement, he.LocalName, null, "head");
            }
            var body = doc.Body;
            HtmlElement? bodyEl = body is not null && body.IsHtml("body") ? body : null;
            if (bodyEl is not null)
            {
                while (bodyEl.FirstChild is { } c)
                {
                    bodyEl.RemoveChild(c);
                    output.AppendChild(c);
                }
            }
            SanitizeChildren(output);
            return Wrap(output, sheets, bodyEl);
        }

        /// <summary>Sanitizes a fragment in place. In verification passes the existing wrapper and style elements are kept.</summary>
        public HtmlDocumentFragment RunFragment(HtmlDocumentFragment fragment)
        {
            if (_verification)
            {
                foreach (var n in fragment.Descendants())
                    if (n is HtmlElement st && st.IsHtml("style")) _css.CollectDeclaredNames(st.TextContent);
                SanitizeChildren(fragment);
                return fragment;
            }
            var sheets = new List<string>();
            CollectStyleSheets(fragment, sheets);
            SanitizeChildren(fragment);
            return Wrap(fragment, sheets, null);
        }

        private void CollectStyleSheets(HtmlNode root, List<string> sheets)
        {
            var styles = new List<HtmlElement>();
            foreach (var n in root.Descendants())
                if (n is HtmlElement e && e.IsHtml("style")) styles.Add(e);
            foreach (var s in styles)
            {
                if (_p.StyleSheets != StyleSheetHandling.Drop)
                {
                    sheets.Add(s.TextContent);
                    _css.CollectDeclaredNames(s.TextContent);
                }
                s.Remove();
            }
        }

        private HtmlDocumentFragment Wrap(HtmlDocumentFragment content, List<string> sheets, HtmlElement? body)
        {
            string? css = null;
            if (sheets.Count > 0 && _p.StyleSheets != StyleSheetHandling.Drop)
            {
                string? scope = ScopeClass;
                if (_p.StyleSheets == StyleSheetHandling.SanitizeScoped && scope is null)
                {
                    Record(SanitizerActionKind.RemovedElement, "style", null, "scoped style sheets need a valid ScopeClass");
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var text in sheets)
                    {
                        _poll.Check();
                        var sheet = CssParser.ParseStyleSheet(text, _p.Limits, _ct);
                        _css.SanitizeSheet(sheet, scope, verification: false);
                        string s = CssSerializer.Serialize(sheet);
                        if (s.Length > 0)
                        {
                            if (sb.Length > 0) sb.Append('\n');
                            sb.Append(s);
                        }
                    }
                    if (sb.Length > 0) css = sb.ToString();
                }
            }

            bool wrapper = IsValidClass(_p.ScopeClass) && !string.IsNullOrEmpty(_p.WrapperElement) && IsValidClass(_p.WrapperElement);
            HtmlNode target = content;
            HtmlElement? wrap = null;
            if (wrapper)
            {
                wrap = new HtmlElement(_p.WrapperElement.ToLowerInvariant());
                wrap.SetAttribute("class", _p.ScopeClass!);
                if (body is not null && _p.TransferBodyStyle)
                {
                    string style = BodyStyle(body);
                    if (style.Length > 0) wrap.SetAttribute("style", style);
                }
                while (content.FirstChild is { } c)
                {
                    content.RemoveChild(c);
                    wrap.AppendChild(c);
                }
                content.AppendChild(wrap);
                target = wrap;
            }
            if (css is not null)
            {
                var style = new HtmlElement("style");
                style.AppendChild(new HtmlText(css));
                target.InsertBefore(style, target.FirstChild);
            }
            return content;
        }

        private string BodyStyle(HtmlElement body)
        {
            var parts = new List<CssDeclaration>();
            void AddFrom(string property, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var decls = CssParser.ParseDeclarations(property + ": " + value, _p.Limits, _ct);
                if (decls.Count > 0 && decls[0].Name == property) parts.Add(decls[0]);
            }
            AddFrom("background-color", body.GetAttribute("bgcolor"));
            AddFrom("color", body.GetAttribute("text"));
            var bg = body.GetAttribute("background");
            if (!string.IsNullOrWhiteSpace(bg))
            {
                var d = new CssDeclaration("background-image");
                d.Value.Add(new CssToken(CssTokenType.Url, bg));
                parts.Add(d);
            }
            var styleAttr = body.GetAttribute("style");
            if (_p.AllowStyleAttribute && !string.IsNullOrWhiteSpace(styleAttr))
                parts.AddRange(CssParser.ParseDeclarations(styleAttr, _p.Limits, _ct));
            var clean = _css.SanitizeDeclarations(parts, "body");
            return CssSerializer.Serialize(clean);
        }

        // ---------------- tree walk ----------------

        private void SanitizeChildren(HtmlNode root)
        {
            var stack = new Stack<HtmlNode>();
            PushChildren(stack, root);
            while (stack.Count > 0)
            {
                _poll.Tick();
                var n = stack.Pop();
                if (n.Parent is null) continue;
                switch (n)
                {
                    case HtmlText:
                        break;
                    case HtmlComment c:
                    {
                        bool keep = _p.AllowComments && c.Data.IndexOfAny(new[] { '[', '<', '>' }) < 0 && !c.Data.Contains("--", StringComparison.Ordinal);
                        if (!keep)
                        {
                            Record(SanitizerActionKind.RemovedComment, "#comment", null, null);
                            c.Remove();
                        }
                        break;
                    }
                    case HtmlDocumentType:
                        Record(SanitizerActionKind.RemovedComment, "#doctype", null, null);
                        n.Remove();
                        break;
                    case HtmlElement e:
                    {
                        var action = Decide(e);
                        if (action == ElementAction.Drop)
                        {
                            Record(SanitizerActionKind.RemovedElement, e.LocalName, null, null);
                            e.Remove();
                        }
                        else if (action == ElementAction.Unwrap)
                        {
                            Record(SanitizerActionKind.UnwrappedElement, e.LocalName, null, null);
                            var kids = new List<HtmlNode>();
                            var container = e.Content ?? (HtmlNode)e;
                            for (var c = container.FirstChild; c is not null; c = c.NextSibling) kids.Add(c);
                            var parent = e.Parent!;
                            foreach (var k in kids)
                            {
                                container.RemoveChild(k);
                                parent.InsertBefore(k, e);
                            }
                            e.Remove();
                            for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
                        }
                        else
                        {
                            SanitizeAttributes(e);
                            PushChildren(stack, e);
                        }
                        break;
                    }
                }
            }
        }

        private static void PushChildren(Stack<HtmlNode> stack, HtmlNode node)
        {
            for (var c = node.LastChild; c is not null; c = c.PreviousSibling) stack.Push(c);
        }

        private ElementAction Decide(HtmlElement e)
        {
            switch (e.Namespace)
            {
                case HtmlNamespace.Svg:
                    if (!_p.AllowSvg) return ElementAction.Drop;
                    return _p.AllowedSvgElements.Contains(e.LocalName) ? ElementAction.Keep : ElementAction.Drop;
                case HtmlNamespace.MathML:
                    if (!_p.AllowMathML) return ElementAction.Drop;
                    return _p.AllowedMathMLElements.Contains(e.LocalName) ? ElementAction.Keep : ElementAction.Drop;
            }
            string name = e.LocalName;
            if (name == "form") return ElementAction.Unwrap; // forms can never submit
            if (_verification && name == "style" && _p.StyleSheets != StyleSheetHandling.Drop) return ElementAction.Keep;
            if (_verification && IsValidClass(_p.ScopeClass) && name == _p.WrapperElement && HasClass(e, _p.ScopeClass!)) return ElementAction.Keep;
            if (_p.AllowedElements.Contains(name))
            {
                if (!_p.AllowFormControls && name is "input" or "select" or "option" or "optgroup" or "textarea" or "button" or "datalist" or "output")
                    return ElementAction.Drop;
                return ElementAction.Keep;
            }
            if (_p.DroppedElements.Contains(name)) return ElementAction.Drop;
            return _p.DefaultElementAction == ElementAction.Keep ? ElementAction.Unwrap : _p.DefaultElementAction;
        }

        private static bool HasClass(HtmlElement e, string cls)
        {
            foreach (var c in e.ClassList)
                if (c == cls) return true;
            return false;
        }

        // ---------------- attributes ----------------

        private static readonly HashSet<string> s_idRefAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "for", "headers", "aria-labelledby", "aria-describedby", "aria-controls", "aria-owns", "aria-activedescendant",
            "aria-flowto", "aria-details", "aria-errormessage", "list",
        };

        private static readonly HashSet<string> s_svgUrlAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "fill", "stroke", "filter", "clip-path", "mask", "marker-start", "marker-mid", "marker-end", "cursor",
        };

        private void SanitizeAttributes(HtmlElement e)
        {
            if (e.Namespace != HtmlNamespace.Html)
            {
                SanitizeForeignAttributes(e);
                return;
            }
            string el = e.LocalName;
            _p.ElementAttributes.TryGetValue(el, out var specific);
            var attrs = e.Attributes;
            // the reserved attribute is only ever written by the sanitizer itself (never accepted from input,
            // except on links in verification passes where it is this sanitizer's own output)
            string? reservedName = _p.InertLinkAttribute;
            if (reservedName is not null && !(_verification && el is "a" or "area") && attrs.Remove(reservedName))
                Record(SanitizerActionKind.RemovedAttribute, el, reservedName, null);
            int originalCount = attrs.Count;
            for (int i = 0; i < attrs.Count && i < originalCount; i++)
            {
                var a = attrs[i];
                string name = a.Name;
                if (reservedName is not null && name == reservedName) continue;
                bool allowed = a.Namespace == HtmlAttributeNamespace.None && !name.StartsWith("on", StringComparison.Ordinal)
                    && (_p.GlobalAttributes.Contains(name) || (specific is not null && specific.Contains(name))
                        || (_p.AllowDataAttributes && name.StartsWith("data-", StringComparison.Ordinal) && name.Length > 5)
                        || (_p.AllowAriaAttributes && name.StartsWith("aria-", StringComparison.Ordinal) && name.Length > 5));
                if (allowed) allowed = SanitizeAttributeValue(e, a);
                if (!allowed)
                {
                    Record(SanitizerActionKind.RemovedAttribute, el, name, null);
                    attrs.RemoveAt(i);
                    i--;
                    originalCount--;
                }
            }
            PostProcessElement(e);
        }

        /// <summary>Validates or rewrites an allowed attribute's value; false removes the attribute.</summary>
        private bool SanitizeAttributeValue(HtmlElement e, HtmlAttribute a)
        {
            string el = e.LocalName;
            switch (a.Name)
            {
                case "style":
                {
                    if (!_p.AllowStyleAttribute) return false;
                    var decls = CssParser.ParseDeclarations(a.Value, _p.Limits, _ct);
                    var clean = _css.SanitizeDeclarations(decls, el);
                    if (clean.Count == 0) return false;
                    a.Value = CssSerializer.Serialize(clean);
                    return true;
                }
                case "id":
                    a.Value = PrefixId(a.Value);
                    return a.Value.Length > 0;
                case "name" when el is "a" or "map" or "img":
                    a.Value = PrefixId(a.Value);
                    return true;
                case "usemap":
                    if (!a.Value.StartsWith('#')) return false;
                    a.Value = "#" + PrefixId(a.Value.Substring(1));
                    return true;
                case "href" when el is "a" or "area":
                    return SanitizeLink(e, a);
                case "src" when el is "img":
                case "background":
                    return ReplaceResource(a, ResourceKind.Image, el);
                case "src" when el is "audio" or "video" or "source" or "track":
                case "poster":
                    return ReplaceResource(a, ResourceKind.Media, el);
                case "src" when el is "input":
                    return ReplaceResource(a, ResourceKind.Image, el);
                case "src":
                    return false;
                case "cite":
                {
                    string u = UrlUtil.Normalize(a.Value);
                    string? scheme = UrlUtil.Scheme(u);
                    return scheme is "http" or "https";
                }
                case "target":
                case "rel":
                    return true; // normalized in PostProcessElement
                case "type" when el is "button":
                    return true;
                case "loading":
                    return a.Value.Equals("lazy", StringComparison.OrdinalIgnoreCase) || a.Value.Equals("eager", StringComparison.OrdinalIgnoreCase);
            }
            if (s_idRefAttributes.Contains(a.Name))
            {
                a.Value = PrefixIdList(a.Value);
                return true;
            }
            return true;
        }

        private void PostProcessElement(HtmlElement e)
        {
            string el = e.LocalName;
            if (el is "a" or "area")
            {
                bool hasHref = e.HasAttribute("href");
                bool navigable = hasHref && !(e.GetAttribute("href") ?? string.Empty).StartsWith('#');
                if (navigable && _p.LinksOpenInNewWindow && _p.Links != LinkHandling.Rewrite)
                {
                    e.SetAttribute("target", "_blank");
                    e.SetAttribute("rel", "noopener noreferrer");
                }
                else
                {
                    if (e.HasAttribute("target") && !navigable) e.RemoveAttribute("target");
                    if (navigable && e.GetAttribute("target") is { } t && !t.Equals("_blank", StringComparison.OrdinalIgnoreCase) && !t.Equals("_self", StringComparison.OrdinalIgnoreCase))
                        e.RemoveAttribute("target");
                    if (e.GetAttribute("target") is not null) e.SetAttribute("rel", "noopener noreferrer");
                }
            }
            else if (el == "img" && e.GetAttribute("srcset") is not null)
            {
                e.RemoveAttribute("srcset");
            }
        }

        private bool SanitizeLink(HtmlElement e, HtmlAttribute a)
        {
            string u = UrlUtil.Normalize(a.Value);
            if (u.StartsWith('#'))
            {
                a.Value = "#" + PrefixId(u.Substring(1));
                return true;
            }
            if (_verification && _p.Links == LinkHandling.Rewrite && _p.InertLinkAttribute is not null && e.HasAttribute(_p.InertLinkAttribute))
                return true;
            string? scheme = UrlUtil.Scheme(u);
            bool ok;
            if (scheme is null)
            {
                ok = _p.AllowRelativeLinks && u.Length > 0 && !u.StartsWith("//", StringComparison.Ordinal) && !u.StartsWith("\\\\", StringComparison.Ordinal) && !u.StartsWith("/\\", StringComparison.Ordinal)
                    && !UrlUtil.LenientNetworkPath(u)
                    && (UrlUtil.LenientScheme(u) is not { } hidden || _p.AllowedLinkSchemes.Contains(hidden));
                if (ok && _p.ResourceResolver is not null && !_trustedUrls.Contains(u))
                {
                    var r = _p.ResourceResolver.Resolve(new ResourceRequest(u, ResourceKind.Link, e.LocalName, "href"));
                    if (r is not null && r != u)
                    {
                        _trustedUrls.Add(r);
                        u = r;
                    }
                }
            }
            else
            {
                ok = _p.AllowedLinkSchemes.Contains(scheme);
            }
            if (!ok)
            {
                Record(SanitizerActionKind.RemovedAttribute, e.LocalName, "href", Short(u));
                return false;
            }
            switch (_p.Links)
            {
                case LinkHandling.Inert:
                    if (_p.InertLinkAttribute is not null) e.SetAttribute(_p.InertLinkAttribute, u);
                    if (!e.HasAttribute("title")) e.SetAttribute("title", u);
                    Record(SanitizerActionKind.NeutralizedLink, e.LocalName, "href", Short(u));
                    return false;
                case LinkHandling.Rewrite:
                    a.Value = _p.LinkRewriteTemplate.Replace("{url}", Uri.EscapeDataString(u), StringComparison.Ordinal);
                    if (_p.InertLinkAttribute is not null) e.SetAttribute(_p.InertLinkAttribute, u);
                    Record(SanitizerActionKind.NeutralizedLink, e.LocalName, "href", Short(u));
                    return true;
                default:
                    a.Value = u;
                    return true;
            }
        }

        private static string Short(string s) => s.Length <= 200 ? s : s.Substring(0, 200);

        private bool ReplaceResource(HtmlAttribute a, ResourceKind kind, string element)
        {
            string? r = CheckResource(a.Value, kind, element, a.Name);
            if (r is null) return false;
            a.Value = r;
            return true;
        }

        /// <summary>Returns the URL to emit for a resource, or null when it is blocked (recorded).</summary>
        public string? CheckResource(string url, ResourceKind kind, string element, string? attribute)
        {
            string u = UrlUtil.Normalize(url);
            if (u.Length == 0) return null;
            if (_trustedUrls.Contains(u) || _trustedUrls.Contains(url)) return url;
            if (u.StartsWith('#'))
            {
                // local fragment reference (SVG paint servers, filters)
                return element is "style" or "svg-attr" ? "#" + PrefixId(u.Substring(1)) : null;
            }
            if (UrlUtil.TryParseData(u, out string mime, out long size))
            {
                var allowed = kind switch
                {
                    ResourceKind.Image => _p.AllowedDataImageTypes,
                    ResourceKind.Font => _p.AllowedDataFontTypes,
                    ResourceKind.Media => _p.AllowedDataMediaTypes,
                    _ => null,
                };
                if (allowed is not null && allowed.Contains(mime) && size <= _p.Limits.MaxDataUriBytes) return u;
                Block(u.Length > 64 ? u.Substring(0, 64) + "..." : u, kind, element, attribute);
                return null;
            }
            if (_p.ResourceResolver is not null)
            {
                var r = _p.ResourceResolver.Resolve(new ResourceRequest(u, kind, element, attribute));
                if (r is not null && !IsScriptCapable(r, kind))
                {
                    _trustedUrls.Add(r);
                    Record(SanitizerActionKind.ResolvedResource, element, attribute, Short(u));
                    return r;
                }
            }
            Block(u, kind, element, attribute);
            return null;
        }

        // Resolver output is trusted (it is host code mapping cid:/relative URLs to host-served URLs),
        // but never as a script-capable URL or a data: URL the policy would not accept.
        private bool IsScriptCapable(string resolved, ResourceKind kind)
        {
            string n = UrlUtil.Normalize(resolved);
            string? scheme = UrlUtil.Scheme(n);
            if (scheme is null) return n.Length == 0;
            if (scheme is "javascript" or "vbscript" or "livescript" or "mocha" or "jscript" or "file" or "blob") return true;
            if (scheme == "data")
            {
                if (!UrlUtil.TryParseData(n, out string mime, out long size)) return true;
                var allowed = kind switch
                {
                    ResourceKind.Image => _p.AllowedDataImageTypes,
                    ResourceKind.Font => _p.AllowedDataFontTypes,
                    ResourceKind.Media => _p.AllowedDataMediaTypes,
                    _ => null,
                };
                return allowed is null || !allowed.Contains(mime) || size > _p.Limits.MaxDataUriBytes;
            }
            return false;
        }

        private void Block(string url, ResourceKind kind, string element, string? attribute)
        {
            if (_verification) return;
            Record(SanitizerActionKind.BlockedResource, element, attribute, Short(url));
            if (_blockedSeen.Add((url, kind)) && _blocked.Count < Math.Max(1, _p.Limits.MaxRecordedErrors))
                _blocked.Add(new BlockedResource(Short(url), kind, element, attribute));
        }

        private string PrefixId(string id)
        {
            string? prefix = _p.IdPrefix;
            if (string.IsNullOrEmpty(prefix) || id.Length == 0 || id.StartsWith(prefix, StringComparison.Ordinal)) return id;
            return prefix + id;
        }

        private string PrefixIdList(string list)
        {
            if (string.IsNullOrEmpty(_p.IdPrefix)) return list;
            var parts = list.Split(new[] { ' ', '\t', '\n', '\f', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = PrefixId(parts[i]);
            return string.Join(" ", parts);
        }

        // ---------------- SVG / MathML ----------------

        private void SanitizeForeignAttributes(HtmlElement e)
        {
            bool svg = e.Namespace == HtmlNamespace.Svg;
            var allowedSet = svg ? _p.AllowedSvgAttributes : _p.AllowedMathMLAttributes;
            var attrs = e.Attributes;
            for (int i = 0; i < attrs.Count; i++)
            {
                var a = attrs[i];
                string name = a.Name;
                bool keep;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    keep = false;
                }
                else if (svg && (name == "href" || name == "xlink:href"))
                {
                    string u = UrlUtil.Normalize(a.Value);
                    if (u.StartsWith('#'))
                    {
                        a.Value = "#" + PrefixId(u.Substring(1));
                        keep = e.LocalName is "use" or "textPath" or "linearGradient" or "radialGradient" or "pattern" or "filter" or "feImage";
                    }
                    else if (e.LocalName == "image")
                    {
                        keep = ReplaceResource(a, ResourceKind.Image, "image");
                    }
                    else
                    {
                        if (u.Length > 0) Block(u, ResourceKind.Other, e.LocalName, name);
                        keep = false;
                    }
                }
                else if (!allowedSet.Contains(name) && !(a.Namespace == HtmlAttributeNamespace.Xml && allowedSet.Contains("xml:" + a.LocalName)))
                {
                    keep = false;
                }
                else if (name == "style")
                {
                    var clean = _css.SanitizeDeclarations(CssParser.ParseDeclarations(a.Value, _p.Limits, _ct), e.LocalName);
                    keep = clean.Count > 0;
                    if (keep) a.Value = CssSerializer.Serialize(clean);
                }
                else if (name == "id")
                {
                    a.Value = PrefixId(a.Value);
                    keep = a.Value.Length > 0;
                }
                else if (svg && name == "font-family")
                {
                    var decls = CssParser.ParseDeclarations("font-family: " + a.Value, _p.Limits, _ct);
                    keep = decls.Count == 1;
                    if (keep)
                    {
                        _css.RenameReferences(decls[0]);
                        a.Value = decls[0].ValueText;
                    }
                }
                else if (svg && s_svgUrlAttributes.Contains(name) && a.Value.Contains('('))
                {
                    var decls = CssParser.ParseDeclarations("x: " + a.Value, _p.Limits, _ct);
                    keep = decls.Count == 1 && _css.SanitizeValues(decls[0].Value, "svg-attr", ResourceKind.Image, 0) && OnlyFragmentUrls(decls[0].Value);
                    if (keep) a.Value = decls[0].ValueText;
                }
                else
                {
                    keep = true;
                }
                if (!keep)
                {
                    Record(SanitizerActionKind.RemovedAttribute, e.LocalName, name, null);
                    attrs.RemoveAt(i);
                    i--;
                }
            }
        }

        private static bool OnlyFragmentUrls(List<CssComponentValue> values)
        {
            foreach (var v in values)
            {
                if (v is CssToken { Type: CssTokenType.Url } u && !u.Value.StartsWith('#')) return false;
                if (v is CssFunction f && (f.Name.Equals("url", StringComparison.OrdinalIgnoreCase) || !OnlyFragmentUrls(f.Arguments))) return false;
                if (v is CssSimpleBlock b && !OnlyFragmentUrls(b.Values)) return false;
            }
            return true;
        }

        // ---------------- verification: in-place style elements ----------------

        public void SanitizeStyleElementsInPlace(HtmlNode root)
        {
            foreach (var n in new List<HtmlNode>(root.Descendants()))
            {
                if (n is not HtmlElement { LocalName: "style", Namespace: HtmlNamespace.Html } s || s.Parent is null) continue;
                var sheet = CssParser.ParseStyleSheet(s.TextContent, _p.Limits, _ct);
                _css.SanitizeSheet(sheet, ScopeClass, verification: true);
                s.RemoveAllChildren();
                s.Attributes.Clear();
                string css = CssSerializer.Serialize(sheet);
                if (css.Length > 0) s.AppendChild(new HtmlText(css));
                else s.Remove();
            }
        }
    }
}
