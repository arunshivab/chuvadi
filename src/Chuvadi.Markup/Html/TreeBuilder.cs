using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Html
{
    /// <summary>
    /// WHATWG HTML tree construction (all insertion modes, foreign content, adoption agency, foster
    /// parenting, templates, the 2025 relaxed &lt;select&gt; rules). Scripting is never executed.
    /// </summary>
    internal sealed partial class TreeBuilder
    {
        private enum Mode
        {
            Initial, BeforeHtml, BeforeHead, InHead, InHeadNoscript, AfterHead, InBody, Text, InTable, InTableText,
            InCaption, InColumnGroup, InTableBody, InRow, InCell, InTemplate, AfterBody, InFrameset, AfterFrameset,
            AfterAfterBody, AfterAfterFrameset,
        }

        private sealed class Formatting
        {
            public Formatting(HtmlElement element, HtmlToken token)
            {
                Element = element;
                Token = token;
            }

            public HtmlElement Element;
            public readonly HtmlToken Token;
        }

        private readonly HtmlTokenizer _tk;
        private readonly HtmlDocument _doc;
        private readonly HtmlParserOptions _opt;
        private readonly MarkupLimits _limits;
        private readonly NodeBudget _budget;
        private CancellationPoll _poll;
        private readonly long _maxWork;
        private long _work;

        private Mode _mode = Mode.Initial;
        private Mode _originalMode;
        private readonly List<Mode> _templateModes = new List<Mode>();
        private readonly List<HtmlElement> _open = new List<HtmlElement>();
        private readonly List<Formatting?> _active = new List<Formatting?>(); // null = marker
        private HtmlElement? _head;
        private HtmlElement? _form;
        private bool _framesetOk = true;
        private bool _fosterParenting;
        private readonly bool _scripting;
        private bool _skipNextNewline;
        private HtmlElement? _context;
        private readonly StringBuilder _pendingTableText = new StringBuilder();
        private bool _pendingTableTextNonWs;
        private bool _stopped;
        private readonly HtmlToken _seg = new HtmlToken(HtmlTokenKind.Character);
        private readonly int _maxDepth;

        public TreeBuilder(HtmlTokenizer tokenizer, HtmlDocument doc, HtmlParserOptions options, CancellationToken ct)
        {
            _tk = tokenizer;
            _doc = doc;
            _opt = options;
            _limits = options.Limits;
            _budget = new NodeBudget(_limits.MaxNodes);
            _poll = new CancellationPoll(ct);
            _scripting = options.Scripting;
            _maxDepth = _limits.MaxNestingDepth;
            _maxWork = (long)_limits.MaxParserWorkPerChar * (tokenizer.Input.Length + 1024) + 1_000_000;
            _tk.AllowCdata = () => AdjustedCurrentNode is { } n && n.Namespace != HtmlNamespace.Html;
        }

        // ---------------- running ----------------

        public void Run()
        {
            while (!_stopped)
            {
                _poll.Tick();
                var t = _tk.NextToken();
                if (t.Kind == HtmlTokenKind.Character)
                {
                    string data = t.Data;
                    if (_skipNextNewline)
                    {
                        _skipNextNewline = false;
                        if (data.Length > 0 && data[0] == '\n') data = data.Substring(1);
                    }
                    ProcessCharacters(data, t.Offset);
                }
                else
                {
                    _skipNextNewline = false;
                    Dispatch(t);
                    if (t.Kind == HtmlTokenKind.StartTag && t.SelfClosing && !t.SelfClosingAcknowledged)
                        Error("non-void-html-element-start-tag-with-trailing-solidus", t.Offset);
                    if (t.Kind == HtmlTokenKind.EndOfFile) break;
                }
            }
            PopAllAtEnd();
        }

        private void ProcessCharacters(string data, int offset)
        {
            int i = 0;
            while (i < data.Length)
            {
                int start = i;
                int kind = CharClass(data[i]);
                while (i < data.Length && CharClass(data[i]) == kind) i++;
                _seg.Data = start == 0 && i == data.Length ? data : data.Substring(start, i - start);
                _seg.Offset = offset + start;
                Dispatch(_seg);
                if (_stopped) return;
            }
        }

        private static int CharClass(char c) => c switch
        {
            '\t' or '\n' or '\f' or '\r' or ' ' => 0,
            '\0' => 1,
            _ => 2,
        };

        private static bool IsWhitespaceSeg(HtmlToken t) => t.Data.Length > 0 && CharClass(t.Data[0]) == 0;

        private static bool IsNullSeg(HtmlToken t) => t.Data.Length > 0 && t.Data[0] == '\0';

        private void Work(int n = 1)
        {
            _work += n;
            if (_work > _maxWork)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxParserWorkPerChar), _maxWork, _work);
        }

        private void Error(string code, int offset = -1) => _tk.Error(code, offset);

        // ---------------- dispatcher ----------------

        private HtmlElement? CurrentNode => _open.Count == 0 ? null : _open[_open.Count - 1];

        private HtmlElement? AdjustedCurrentNode => _context is not null && _open.Count == 1 ? _context : CurrentNode;

        private void Dispatch(HtmlToken t)
        {
            var acn = AdjustedCurrentNode;
            bool html;
            if (acn is null || acn.Namespace == HtmlNamespace.Html) html = true;
            else if (IsMathMLTextIntegrationPoint(acn) && ((t.Kind == HtmlTokenKind.StartTag && t.Name != "mglyph" && t.Name != "malignmark") || t.Kind == HtmlTokenKind.Character)) html = true;
            else if (acn.Namespace == HtmlNamespace.MathML && acn.LocalName == "annotation-xml" && t.Kind == HtmlTokenKind.StartTag && t.Name == "svg") html = true;
            else if (IsHtmlIntegrationPoint(acn) && (t.Kind == HtmlTokenKind.StartTag || t.Kind == HtmlTokenKind.Character)) html = true;
            else html = t.Kind == HtmlTokenKind.EndOfFile;

            if (html) ProcessHtml(t);
            else ForeignContent(t);
        }

        private void ProcessHtml(HtmlToken t)
        {
            // A token may legitimately be reprocessed once per open element or template mode
            // (end of file inside nested templates pops one template per pass).
            int guard = 0, maxPasses = 64 + 4 * (_open.Count + _templateModes.Count);
            while (Handle(_mode, t))
            {
                if (++guard > maxPasses) throw new InvalidOperationException("Tree builder reprocessing loop.");
            }
        }

        /// <summary>Processes the token in the given mode. Returns true if it must be reprocessed in the (new) current mode.</summary>
        private bool Handle(Mode mode, HtmlToken t) => mode switch
        {
            Mode.Initial => Initial(t),
            Mode.BeforeHtml => BeforeHtml(t),
            Mode.BeforeHead => BeforeHead(t),
            Mode.InHead => InHead(t),
            Mode.InHeadNoscript => InHeadNoscript(t),
            Mode.AfterHead => AfterHead(t),
            Mode.InBody => InBody(t),
            Mode.Text => TextMode(t),
            Mode.InTable => InTable(t),
            Mode.InTableText => InTableText(t),
            Mode.InCaption => InCaption(t),
            Mode.InColumnGroup => InColumnGroup(t),
            Mode.InTableBody => InTableBody(t),
            Mode.InRow => InRow(t),
            Mode.InCell => InCell(t),
            Mode.InTemplate => InTemplate(t),
            Mode.AfterBody => AfterBody(t),
            Mode.InFrameset => InFrameset(t),
            Mode.AfterFrameset => AfterFrameset(t),
            Mode.AfterAfterBody => AfterAfterBody(t),
            Mode.AfterAfterFrameset => AfterAfterFrameset(t),
            _ => false,
        };

        private static bool IsStart(HtmlToken t, string name) => t.Kind == HtmlTokenKind.StartTag && t.Name == name;

        private static bool IsEnd(HtmlToken t, string name) => t.Kind == HtmlTokenKind.EndTag && t.Name == name;

        private static bool IsStartOf(HtmlToken t, HashSet<string> set) => t.Kind == HtmlTokenKind.StartTag && set.Contains(t.Name!);

        private static bool IsEndOf(HtmlToken t, HashSet<string> set) => t.Kind == HtmlTokenKind.EndTag && set.Contains(t.Name!);

        private static HashSet<string> Set(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);

        // ---------------- element categories ----------------

        private static readonly HashSet<string> s_specialHtml = Set(
            "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote", "body", "br",
            "button", "caption", "center", "col", "colgroup", "dd", "details", "dir", "div", "dl", "dt", "embed",
            "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6",
            "head", "header", "hgroup", "hr", "html", "iframe", "img", "input", "keygen", "li", "link", "listing", "main",
            "marquee", "menu", "meta", "nav", "noembed", "noframes", "noscript", "object", "ol", "p", "param", "plaintext",
            "pre", "script", "search", "section", "select", "source", "style", "summary", "table", "tbody", "td", "template",
            "textarea", "tfoot", "th", "thead", "title", "tr", "track", "ul", "wbr", "xmp");

        private static readonly HashSet<string> s_formatting = Set("a", "b", "big", "code", "em", "font", "i", "nobr", "s", "small", "strike", "strong", "tt", "u");

        private static bool IsSpecial(HtmlElement e)
        {
            switch (e.Namespace)
            {
                case HtmlNamespace.Html: return s_specialHtml.Contains(e.LocalName);
                case HtmlNamespace.MathML: return e.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext" or "annotation-xml";
                default: return e.LocalName is "foreignObject" or "desc" or "title";
            }
        }

        private static bool IsMathMLTextIntegrationPoint(HtmlElement e) =>
            e.Namespace == HtmlNamespace.MathML && e.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext";

        private bool IsHtmlIntegrationPoint(HtmlElement e)
        {
            if (e.Namespace == HtmlNamespace.MathML && e.LocalName == "annotation-xml")
            {
                string? enc = e.GetAttribute("encoding");
                return enc is not null && (enc.Equals("text/html", StringComparison.OrdinalIgnoreCase) || enc.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));
            }
            return e.Namespace == HtmlNamespace.Svg && e.LocalName is "foreignObject" or "desc" or "title";
        }

        // ---------------- scopes ----------------

        private enum Scope { Default, ListItem, Button, Table }

        private static bool IsScopeBoundary(HtmlElement e, Scope scope)
        {
            if (scope == Scope.Table)
                return e.Namespace == HtmlNamespace.Html && e.LocalName is "html" or "table" or "template";
            switch (e.Namespace)
            {
                case HtmlNamespace.Html:
                    switch (e.LocalName)
                    {
                        case "applet": case "caption": case "html": case "table": case "td": case "th":
                        case "marquee": case "object": case "select": case "template":
                            return true;
                        case "ol": case "ul":
                            return scope == Scope.ListItem;
                        case "button":
                            return scope == Scope.Button;
                    }
                    return false;
                case HtmlNamespace.MathML:
                    return e.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext" or "annotation-xml";
                default:
                    return e.LocalName is "foreignObject" or "desc" or "title";
            }
        }

        private bool InScope(string name, Scope scope = Scope.Default)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Work();
                var e = _open[i];
                if (e.Namespace == HtmlNamespace.Html && e.LocalName == name) return true;
                if (IsScopeBoundary(e, scope)) return false;
            }
            return false;
        }

        private bool InScope(HtmlElement target, Scope scope = Scope.Default)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Work();
                var e = _open[i];
                if (ReferenceEquals(e, target)) return true;
                if (IsScopeBoundary(e, scope)) return false;
            }
            return false;
        }

        private bool InScopeAny(HashSet<string> names, Scope scope = Scope.Default)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Work();
                var e = _open[i];
                if (e.Namespace == HtmlNamespace.Html && names.Contains(e.LocalName)) return true;
                if (IsScopeBoundary(e, scope)) return false;
            }
            return false;
        }

        private bool StackHas(string name)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Work();
                if (_open[i].IsHtml(name)) return true;
            }
            return false;
        }

        private bool CurrentIs(string name) => CurrentNode is { } c && c.IsHtml(name);

        private void PopUntil(string name)
        {
            while (_open.Count > 0)
            {
                Work();
                var e = Pop();
                if (e.IsHtml(name)) return;
            }
        }

        private void PopUntilAny(HashSet<string> names)
        {
            while (_open.Count > 0)
            {
                Work();
                var e = Pop();
                if (e.Namespace == HtmlNamespace.Html && names.Contains(e.LocalName)) return;
            }
        }

        private void PopUntilElement(HtmlElement el)
        {
            while (_open.Count > 0)
            {
                Work();
                if (ReferenceEquals(Pop(), el)) return;
            }
        }

        private HtmlElement Pop()
        {
            var e = _open[_open.Count - 1];
            _open.RemoveAt(_open.Count - 1);
            if (_selectedContents is not null && e.Namespace == HtmlNamespace.Html && e.LocalName == "option")
                MaybeCloneOptionIntoSelectedContent(e);
            return e;
        }

        // ---------------- customizable <select>: <selectedcontent> ----------------

        // select -> its <selectedcontent> (first one inserted), and select -> its currently selected option.
        private Dictionary<HtmlElement, HtmlElement>? _selectedContents;
        private Dictionary<HtmlElement, HtmlElement>? _selectedOptions;

        private void OnElementInserted(HtmlElement el)
        {
            if (el.Namespace != HtmlNamespace.Html || el.LocalName != "selectedcontent") return;
            var select = NearestAncestorSelect(el);
            if (select is null) return;
            _selectedContents ??= new Dictionary<HtmlElement, HtmlElement>(ReferenceEqualityComparer.Instance);
            _selectedContents.TryAdd(select, el);
        }

        private static HtmlElement? NearestAncestorSelect(HtmlNode node)
        {
            for (var p = node.Parent; p is not null; p = p.Parent)
            {
                if (p is not HtmlElement pe || pe.Namespace != HtmlNamespace.Html) continue;
                if (pe.LocalName == "select") return pe;
                if (pe.LocalName is "datalist" or "option") return null;
            }
            return null;
        }

        /// <summary>
        /// WHATWG "maybe clone an option into selectedcontent", run when an option is popped: if the option is
        /// its select's selected option (the last one with a selected attribute, else the first), its
        /// children are cloned into the select's selectedcontent, replacing what was there.
        /// </summary>
        private void MaybeCloneOptionIntoSelectedContent(HtmlElement option)
        {
            var select = NearestAncestorSelect(option);
            if (select is null || select.HasAttribute("multiple")) return;
            if (!_selectedContents!.TryGetValue(select, out var target)) return;
            _selectedOptions ??= new Dictionary<HtmlElement, HtmlElement>(ReferenceEqualityComparer.Instance);
            if (!option.HasAttribute("selected"))
            {
                if (_selectedOptions.TryGetValue(select, out var current) && !ReferenceEquals(current, option)) return;
            }
            _selectedOptions[select] = option;
            for (HtmlNode? p = target; p is not null; p = p.Parent)
                if (ReferenceEquals(p, option)) return;
            for (HtmlNode? p = option; p is not null; p = p.Parent)
                if (ReferenceEquals(p, target)) return;
            while (target.FirstChild is { } c) c.Remove();
            for (var c = option.FirstChild; c is not null; c = c.NextSibling)
            {
                Work();
                _budget.Take();
                foreach (var _ in c.Descendants())
                {
                    Work();
                    _budget.Take();
                }
                target.InsertBeforeCore(c.Clone(true), null);
            }
        }

        /// <summary>The end of parsing pops every open element (option pops can clone into selectedcontent).</summary>
        private void PopAllAtEnd()
        {
            if (_selectedContents is null) return;
            while (_open.Count > 0) Pop();
        }

        private static readonly HashSet<string> s_impliedEnd = Set("dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc");
        private static readonly HashSet<string> s_impliedEndThorough = Set("caption", "colgroup", "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc", "tbody", "td", "tfoot", "th", "thead", "tr");

        private void GenerateImpliedEndTags(string? except = null)
        {
            while (CurrentNode is { } c && c.Namespace == HtmlNamespace.Html && s_impliedEnd.Contains(c.LocalName) && c.LocalName != except)
            {
                Work();
                Pop();
            }
        }

        private void GenerateImpliedEndTagsThoroughly()
        {
            while (CurrentNode is { } c && c.Namespace == HtmlNamespace.Html && s_impliedEndThorough.Contains(c.LocalName))
            {
                Work();
                Pop();
            }
        }

        // ---------------- node creation and insertion ----------------

        private (HtmlNode parent, HtmlNode? before) AppropriatePlace(HtmlNode? overrideTarget = null)
        {
            HtmlNode target = overrideTarget ?? (HtmlNode?)CurrentNode ?? _doc;
            if (overrideTarget is null && _open.Count > _maxDepth)
                target = _open[_maxDepth - 1]; // depth limit: attach to the deepest allowed ancestor (Chromium behaviour)
            HtmlNode? before = null;
            if (_fosterParenting && target is HtmlElement te && te.Namespace == HtmlNamespace.Html && te.LocalName is "table" or "tbody" or "tfoot" or "thead" or "tr")
            {
                HtmlElement? lastTemplate = null, lastTable = null;
                int templateIdx = -1, tableIdx = -1;
                for (int i = _open.Count - 1; i >= 0; i--)
                {
                    Work();
                    var e = _open[i];
                    if (lastTemplate is null && e.IsHtml("template")) { lastTemplate = e; templateIdx = i; }
                    if (lastTable is null && e.IsHtml("table")) { lastTable = e; tableIdx = i; }
                    if (lastTemplate is not null && lastTable is not null) break;
                }
                if (lastTemplate is not null && (lastTable is null || templateIdx > tableIdx))
                {
                    target = lastTemplate;
                }
                else if (lastTable is null)
                {
                    return (_open[0], null);
                }
                else if (lastTable.Parent is not null)
                {
                    target = lastTable.Parent;
                    before = lastTable;
                }
                else
                {
                    target = _open[tableIdx - 1];
                }
            }
            if (target is HtmlElement tpl && tpl.IsHtml("template"))
                return (tpl.Content!, before is not null && before.Parent == tpl.Content ? before : null);
            return (target, before);
        }

        private static readonly Dictionary<string, string> s_svgAttributes = BuildSvgAttributes();

        private static Dictionary<string, string> BuildSvgAttributes()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var n in new[]
            {
                "attributeName", "attributeType", "baseFrequency", "baseProfile", "calcMode", "clipPathUnits", "diffuseConstant",
                "edgeMode", "filterUnits", "glyphRef", "gradientTransform", "gradientUnits", "kernelMatrix", "kernelUnitLength",
                "keyPoints", "keySplines", "keyTimes", "lengthAdjust", "limitingConeAngle", "markerHeight", "markerUnits",
                "markerWidth", "maskContentUnits", "maskUnits", "numOctaves", "pathLength", "patternContentUnits",
                "patternTransform", "patternUnits", "pointsAtX", "pointsAtY", "pointsAtZ", "preserveAlpha",
                "preserveAspectRatio", "primitiveUnits", "refX", "refY", "repeatCount", "repeatDur", "requiredExtensions",
                "requiredFeatures", "specularConstant", "specularExponent", "spreadMethod", "startOffset", "stdDeviation",
                "stitchTiles", "surfaceScale", "systemLanguage", "tableValues", "targetX", "targetY", "textLength", "viewBox",
                "viewTarget", "xChannelSelector", "yChannelSelector", "zoomAndPan",
            })
                d[n.ToLowerInvariant()] = n;
            return d;
        }

        private static readonly Dictionary<string, string> s_svgTags = BuildSvgTags();

        private static Dictionary<string, string> BuildSvgTags()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var n in new[]
            {
                "altGlyph", "altGlyphDef", "altGlyphItem", "animateColor", "animateMotion", "animateTransform", "clipPath",
                "feBlend", "feColorMatrix", "feComponentTransfer", "feComposite", "feConvolveMatrix", "feDiffuseLighting",
                "feDisplacementMap", "feDistantLight", "feDropShadow", "feFlood", "feFuncA", "feFuncB", "feFuncG", "feFuncR",
                "feGaussianBlur", "feImage", "feMerge", "feMergeNode", "feMorphology", "feOffset", "fePointLight",
                "feSpecularLighting", "feSpotLight", "feTile", "feTurbulence", "foreignObject", "glyphRef", "linearGradient",
                "radialGradient", "textPath",
            })
                d[n.ToLowerInvariant()] = n;
            return d;
        }

        private static HtmlAttribute AdjustForeignAttribute(string name, string value, HtmlNamespace ns)
        {
            switch (name)
            {
                case "xlink:actuate": case "xlink:arcrole": case "xlink:href": case "xlink:role":
                case "xlink:show": case "xlink:title": case "xlink:type":
                    return new HtmlAttribute(name.Substring(6), value, HtmlAttributeNamespace.XLink, "xlink");
                case "xml:lang": case "xml:space":
                    return new HtmlAttribute(name.Substring(4), value, HtmlAttributeNamespace.Xml, "xml");
                case "xmlns":
                    return new HtmlAttribute("xmlns", value, HtmlAttributeNamespace.Xmlns, null);
                case "xmlns:xlink":
                    return new HtmlAttribute("xlink", value, HtmlAttributeNamespace.Xmlns, "xmlns");
            }
            if (ns == HtmlNamespace.Svg && s_svgAttributes.TryGetValue(name, out var adjusted))
                return new HtmlAttribute(adjusted, value);
            if (ns == HtmlNamespace.MathML && name == "definitionurl")
                return new HtmlAttribute("definitionURL", value);
            return new HtmlAttribute(name, value);
        }

        private HtmlElement CreateElement(HtmlToken t, HtmlNamespace ns, string? localName = null)
        {
            _budget.Take();
            var el = new HtmlElement(localName ?? t.Name!, ns) { SourceOffset = t.Offset };
            if (t.AttributeList is { } attrs)
            {
                foreach (var a in attrs)
                {
                    _budget.Take();
                    el.Attributes.AddUnchecked(ns == HtmlNamespace.Html ? new HtmlAttribute(a.Name, a.Value) : AdjustForeignAttribute(a.Name, a.Value, ns));
                }
            }
            return el;
        }

        private static HtmlToken SyntheticTag(string name) => new HtmlToken(HtmlTokenKind.StartTag) { Name = name, Offset = -1 };

        private void InsertAt(HtmlNode node, (HtmlNode parent, HtmlNode? before) place)
        {
            if (place.parent is HtmlDocument d && node is HtmlElement && d.DocumentElement is not null) return;
            if (node.Parent is not null) node.Remove();
            place.parent.InsertBeforeCore(node, place.before);
        }

        private HtmlElement InsertForeignElement(HtmlToken t, HtmlNamespace ns, string? localName = null)
        {
            var place = AppropriatePlace();
            var el = CreateElement(t, ns, localName);
            InsertAt(el, place);
            _open.Add(el);
            OnElementInserted(el);
            return el;
        }

        private HtmlElement InsertHtmlElement(HtmlToken t) => InsertForeignElement(t, HtmlNamespace.Html);

        private void InsertCharacters(string data, int offset = -1)
        {
            var (parent, before) = AppropriatePlace();
            if (parent is HtmlDocument) return;
            var prev = before is not null ? before.PreviousSibling : parent.LastChild;
            if (prev is HtmlText text)
            {
                text.AppendInternal(data);
                return;
            }
            _budget.Take();
            var node = new HtmlText(new StringBuilder(data)) { SourceOffset = offset };
            parent.InsertBeforeCore(node, before);
        }

        private void InsertComment(HtmlToken t, (HtmlNode parent, HtmlNode? before)? place = null)
        {
            var p = place ?? AppropriatePlace();
            _budget.Take();
            p.parent.InsertBeforeCore(new HtmlComment(t.Data) { SourceOffset = t.Offset }, p.before);
        }

        private void GenericRawText(HtmlToken t, HtmlTokenizerState state)
        {
            InsertHtmlElement(t);
            _tk.SetState(state);
            _originalMode = _mode;
            _mode = Mode.Text;
        }

        private static void Acknowledge(HtmlToken t) => t.SelfClosingAcknowledged = true;

        // ---------------- active formatting elements ----------------

        private void PushFormatting(HtmlElement el, HtmlToken t)
        {
            // Noah's Ark: at most three identical entries after the last marker
            int count = 0, earliest = -1;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Work();
                var f = _active[i];
                if (f is null) break;
                if (f.Element.LocalName == el.LocalName && f.Element.Namespace == el.Namespace && SameAttributes(f.Token, t))
                {
                    count++;
                    earliest = i;
                }
            }
            if (count >= 3) _active.RemoveAt(earliest);
            // bounded list (Chuvadi): keeps reconstruction linear on adversarial input
            if (_active.Count >= _maxDepth)
            {
                int firstEl = _active.FindIndex(x => x is not null);
                if (firstEl >= 0) _active.RemoveAt(firstEl);
            }
            _active.Add(new Formatting(el, t));
        }

        private static bool SameAttributes(HtmlToken a, HtmlToken b)
        {
            var la = a.AttributeList;
            var lb = b.AttributeList;
            int ca = la?.Count ?? 0, cb = lb?.Count ?? 0;
            if (ca != cb) return false;
            if (ca == 0) return true;
            foreach (var x in la!)
            {
                bool found = false;
                foreach (var y in lb!)
                    if (x.Name == y.Name && x.Value == y.Value) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        private bool OnStack(HtmlElement e)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Work();
                if (ReferenceEquals(_open[i], e)) return true;
            }
            return false;
        }

        private int ActiveIndexOf(HtmlElement e)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                if (_active[i] is { } f && ReferenceEquals(f.Element, e)) return i;
            return -1;
        }

        private void ReconstructFormatting()
        {
            if (_active.Count == 0) return;
            var last = _active[_active.Count - 1];
            if (last is null || OnStack(last.Element)) return;
            int i = _active.Count - 1;
            while (i > 0)
            {
                Work();
                var e = _active[i - 1];
                if (e is null || OnStack(e.Element)) break;
                i--;
            }
            for (; i < _active.Count; i++)
            {
                Work();
                var entry = _active[i]!;
                var el = InsertHtmlElement(entry.Token);
                _active[i] = new Formatting(el, entry.Token);
            }
        }

        private void ClearFormattingToMarker()
        {
            while (_active.Count > 0)
            {
                var e = _active[_active.Count - 1];
                _active.RemoveAt(_active.Count - 1);
                if (e is null) return;
            }
        }

        // ---------------- reset insertion mode ----------------

        private void ResetInsertionMode()
        {
            bool last = false;
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Work();
                var node = _open[i];
                if (i == 0)
                {
                    last = true;
                    if (_context is not null) node = _context;
                }
                if (node.Namespace == HtmlNamespace.Html)
                {
                    switch (node.LocalName)
                    {
                        case "td":
                        case "th":
                            if (!last) { _mode = Mode.InCell; return; }
                            break;
                        case "tr": _mode = Mode.InRow; return;
                        case "tbody": case "thead": case "tfoot": _mode = Mode.InTableBody; return;
                        case "caption": _mode = Mode.InCaption; return;
                        case "colgroup": _mode = Mode.InColumnGroup; return;
                        case "table": _mode = Mode.InTable; return;
                        case "template": _mode = _templateModes[_templateModes.Count - 1]; return;
                        case "head":
                            if (!last) { _mode = Mode.InHead; return; }
                            break;
                        case "body": _mode = Mode.InBody; return;
                        case "frameset": _mode = Mode.InFrameset; return;
                        case "html":
                            _mode = _head is null ? Mode.BeforeHead : Mode.AfterHead;
                            return;
                    }
                }
                if (last)
                {
                    _mode = Mode.InBody;
                    return;
                }
            }
        }
    }
}
