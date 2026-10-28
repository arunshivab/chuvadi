using System;
using System.Collections.Generic;

namespace Chuvadi.Markup.Html
{
    internal sealed partial class TreeBuilder
    {
        private bool ParsingTemplateContents => StackHas("template") || (_context is not null && _context.IsHtml("template"));

        // ---------------- initial / before html / before head ----------------

        private bool Initial(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t, (_doc, null));
                    return false;
                case HtmlTokenKind.Doctype:
                {
                    if (t.Name != "html" || t.PublicId is not null || (t.SystemId is not null && t.SystemId != "about:legacy-compat"))
                        Error("bad-doctype", t.Offset);
                    _budget.Take();
                    _doc.InsertBeforeCore(new HtmlDocumentType(t.Name ?? string.Empty, t.PublicId ?? string.Empty, t.SystemId ?? string.Empty) { SourceOffset = t.Offset }, null);
                    _doc.QuirksMode = QuirksModeFor(t);
                    _mode = Mode.BeforeHtml;
                    return false;
                }
                default:
                    Error("expected-doctype", t.Offset);
                    _doc.QuirksMode = HtmlQuirksMode.Quirks;
                    _mode = Mode.BeforeHtml;
                    return true;
            }
        }

        private static readonly string[] s_quirksPublicPrefixes =
        {
            "+//Silmaril//dtd html Pro v0r11 19970101//", "-//AS//DTD HTML 3.0 asWedit + extensions//",
            "-//AdvaSoft Ltd//DTD HTML 3.0 asWedit + extensions//", "-//IETF//DTD HTML 2.0 Level 1//",
            "-//IETF//DTD HTML 2.0 Level 2//", "-//IETF//DTD HTML 2.0 Strict Level 1//", "-//IETF//DTD HTML 2.0 Strict Level 2//",
            "-//IETF//DTD HTML 2.0 Strict//", "-//IETF//DTD HTML 2.0//", "-//IETF//DTD HTML 2.1E//", "-//IETF//DTD HTML 3.0//",
            "-//IETF//DTD HTML 3.2 Final//", "-//IETF//DTD HTML 3.2//", "-//IETF//DTD HTML 3//", "-//IETF//DTD HTML Level 0//",
            "-//IETF//DTD HTML Level 1//", "-//IETF//DTD HTML Level 2//", "-//IETF//DTD HTML Level 3//",
            "-//IETF//DTD HTML Strict Level 0//", "-//IETF//DTD HTML Strict Level 1//", "-//IETF//DTD HTML Strict Level 2//",
            "-//IETF//DTD HTML Strict Level 3//", "-//IETF//DTD HTML Strict//", "-//IETF//DTD HTML//",
            "-//Metrius//DTD Metrius Presentational//", "-//Microsoft//DTD Internet Explorer 2.0 HTML Strict//",
            "-//Microsoft//DTD Internet Explorer 2.0 HTML//", "-//Microsoft//DTD Internet Explorer 2.0 Tables//",
            "-//Microsoft//DTD Internet Explorer 3.0 HTML Strict//", "-//Microsoft//DTD Internet Explorer 3.0 HTML//",
            "-//Microsoft//DTD Internet Explorer 3.0 Tables//", "-//Netscape Comm. Corp.//DTD HTML//",
            "-//Netscape Comm. Corp.//DTD Strict HTML//", "-//O'Reilly and Associates//DTD HTML 2.0//",
            "-//O'Reilly and Associates//DTD HTML Extended 1.0//", "-//O'Reilly and Associates//DTD HTML Extended Relaxed 1.0//",
            "-//SQ//DTD HTML 2.0 HoTMetaL + extensions//",
            "-//SoftQuad Software//DTD HoTMetaL PRO 6.0::19990601::extensions to HTML 4.0//",
            "-//SoftQuad//DTD HoTMetaL PRO 4.0::19971010::extensions to HTML 4.0//", "-//Spyglass//DTD HTML 2.0 Extended//",
            "-//Sun Microsystems Corp.//DTD HotJava HTML//", "-//Sun Microsystems Corp.//DTD HotJava Strict HTML//",
            "-//W3C//DTD HTML 3 1995-03-24//", "-//W3C//DTD HTML 3.2 Draft//", "-//W3C//DTD HTML 3.2 Final//",
            "-//W3C//DTD HTML 3.2//", "-//W3C//DTD HTML 3.2S Draft//", "-//W3C//DTD HTML 4.0 Frameset//",
            "-//W3C//DTD HTML 4.0 Transitional//", "-//W3C//DTD HTML Experimental 19960712//",
            "-//W3C//DTD HTML Experimental 970421//", "-//W3C//DTD W3 HTML//", "-//W3O//DTD W3 HTML 3.0//",
            "-//WebTechs//DTD Mozilla HTML 2.0//", "-//WebTechs//DTD Mozilla HTML//",
        };

        private static HtmlQuirksMode QuirksModeFor(HtmlToken t)
        {
            static bool Eq(string? a, string b) => a is not null && a.Equals(b, StringComparison.OrdinalIgnoreCase);
            static bool Starts(string? a, string b) => a is not null && a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
            string? pub = t.PublicId, sys = t.SystemId;
            if (t.ForceQuirks || t.Name != "html") return HtmlQuirksMode.Quirks;
            if (Eq(pub, "-//W3O//DTD W3 HTML Strict 3.0//EN//") || Eq(pub, "-/W3C/DTD HTML 4.0 Transitional/EN") || Eq(pub, "HTML")) return HtmlQuirksMode.Quirks;
            if (Eq(sys, "http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd")) return HtmlQuirksMode.Quirks;
            foreach (var p in s_quirksPublicPrefixes)
                if (Starts(pub, p)) return HtmlQuirksMode.Quirks;
            if (sys is null && (Starts(pub, "-//W3C//DTD HTML 4.01 Frameset//") || Starts(pub, "-//W3C//DTD HTML 4.01 Transitional//"))) return HtmlQuirksMode.Quirks;
            if (Starts(pub, "-//W3C//DTD XHTML 1.0 Frameset//") || Starts(pub, "-//W3C//DTD XHTML 1.0 Transitional//")) return HtmlQuirksMode.LimitedQuirks;
            if (sys is not null && (Starts(pub, "-//W3C//DTD HTML 4.01 Frameset//") || Starts(pub, "-//W3C//DTD HTML 4.01 Transitional//"))) return HtmlQuirksMode.LimitedQuirks;
            return HtmlQuirksMode.NoQuirks;
        }

        private bool BeforeHtml(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t, (_doc, null));
                    return false;
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "html":
                {
                    var el = CreateElement(t, HtmlNamespace.Html);
                    _doc.InsertBeforeCore(el, null);
                    _open.Add(el);
                    _mode = Mode.BeforeHead;
                    return false;
                }
                case HtmlTokenKind.EndTag when t.Name is not ("head" or "body" or "html" or "br"):
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                default:
                {
                    var el = CreateElement(SyntheticTag("html"), HtmlNamespace.Html);
                    _doc.InsertBeforeCore(el, null);
                    _open.Add(el);
                    _mode = Mode.BeforeHead;
                    return true;
                }
            }
        }

        private bool BeforeHead(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.StartTag when t.Name == "head":
                    _head = InsertHtmlElement(t);
                    _mode = Mode.InHead;
                    return false;
                case HtmlTokenKind.EndTag when t.Name is not ("head" or "body" or "html" or "br"):
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                default:
                    _head = InsertHtmlElement(SyntheticTag("head"));
                    _mode = Mode.InHead;
                    return true;
            }
        }

        // ---------------- in head ----------------

        private bool InHead(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    InsertCharacters(t.Data, t.Offset);
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag:
                    switch (t.Name)
                    {
                        case "html":
                            return InBody(t);
                        case "base": case "basefont": case "bgsound": case "link": case "meta":
                            InsertHtmlElement(t);
                            Pop();
                            Acknowledge(t);
                            return false;
                        case "title":
                            GenericRawText(t, HtmlTokenizerState.Rcdata);
                            return false;
                        case "noscript" when _scripting:
                        case "noframes": case "style":
                            GenericRawText(t, HtmlTokenizerState.Rawtext);
                            return false;
                        case "noscript":
                            InsertHtmlElement(t);
                            _mode = Mode.InHeadNoscript;
                            return false;
                        case "script":
                            InsertHtmlElement(t);
                            _tk.SetState(HtmlTokenizerState.ScriptData);
                            _originalMode = _mode;
                            _mode = Mode.Text;
                            return false;
                        case "template":
                            _active.Add(null);
                            _framesetOk = false;
                            _mode = Mode.InTemplate;
                            _templateModes.Add(Mode.InTemplate);
                            InsertHtmlElement(t);
                            return false;
                        case "head":
                            Error("unexpected-start-tag", t.Offset);
                            return false;
                    }
                    break;
                case HtmlTokenKind.EndTag:
                    switch (t.Name)
                    {
                        case "head":
                            Pop();
                            _mode = Mode.AfterHead;
                            return false;
                        case "body": case "html": case "br":
                            break;
                        case "template":
                            EndTemplate(t);
                            return false;
                        default:
                            Error("unexpected-end-tag", t.Offset);
                            return false;
                    }
                    break;
            }
            Pop();
            _mode = Mode.AfterHead;
            return true;
        }

        private void EndTemplate(HtmlToken t)
        {
            if (!StackHas("template"))
            {
                Error("unexpected-end-tag", t.Offset);
                return;
            }
            GenerateImpliedEndTagsThoroughly();
            if (!CurrentIs("template")) Error("end-tag-mismatch", t.Offset);
            PopUntil("template");
            ClearFormattingToMarker();
            if (_templateModes.Count > 0) _templateModes.RemoveAt(_templateModes.Count - 1);
            ResetInsertionMode();
        }

        private bool InHeadNoscript(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.EndTag when t.Name == "noscript":
                    Pop();
                    _mode = Mode.InHead;
                    return false;
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                case HtmlTokenKind.Comment:
                case HtmlTokenKind.StartTag when t.Name is "basefont" or "bgsound" or "link" or "meta" or "noframes" or "style":
                    return InHead(t);
                case HtmlTokenKind.EndTag when t.Name == "br":
                    break;
                case HtmlTokenKind.StartTag when t.Name is "head" or "noscript":
                case HtmlTokenKind.EndTag:
                    Error("unexpected-tag", t.Offset);
                    return false;
            }
            Error("unexpected-in-noscript", t.Offset);
            Pop();
            _mode = Mode.InHead;
            return true;
        }

        private bool AfterHead(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    InsertCharacters(t.Data, t.Offset);
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag:
                    switch (t.Name)
                    {
                        case "html":
                            return InBody(t);
                        case "body":
                            InsertHtmlElement(t);
                            _framesetOk = false;
                            _mode = Mode.InBody;
                            return false;
                        case "frameset":
                            InsertHtmlElement(t);
                            _mode = Mode.InFrameset;
                            return false;
                        case "base": case "basefont": case "bgsound": case "link": case "meta": case "noframes":
                        case "script": case "style": case "template": case "title":
                        {
                            Error("unexpected-start-tag", t.Offset);
                            var head = _head!;
                            _open.Add(head);
                            bool r = InHead(t);
                            _open.Remove(head);
                            return r;
                        }
                        case "head":
                            Error("unexpected-start-tag", t.Offset);
                            return false;
                    }
                    break;
                case HtmlTokenKind.EndTag:
                    switch (t.Name)
                    {
                        case "template":
                            return InHead(t);
                        case "body": case "html": case "br":
                            break;
                        default:
                            Error("unexpected-end-tag", t.Offset);
                            return false;
                    }
                    break;
            }
            InsertHtmlElement(SyntheticTag("body"));
            _framesetOk = true;
            _mode = Mode.InBody;
            return true;
        }

        // ---------------- in body ----------------

        private static readonly HashSet<string> s_closesPBlock = Set(
            "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
            "figcaption", "figure", "footer", "header", "hgroup", "main", "menu", "nav", "ol", "p", "search", "section",
            "summary", "ul");

        private static readonly HashSet<string> s_headings = Set("h1", "h2", "h3", "h4", "h5", "h6");

        private static readonly HashSet<string> s_blockEnd = Set(
            "address", "article", "aside", "blockquote", "button", "center", "details", "dialog", "dir", "div", "dl",
            "fieldset", "figcaption", "figure", "footer", "header", "hgroup", "listing", "main", "menu", "nav", "ol", "pre",
            "search", "section", "select", "summary", "ul");

        private static readonly HashSet<string> s_formattingStart = Set("b", "big", "code", "em", "font", "i", "s", "small", "strike", "strong", "tt", "u");

        private static readonly HashSet<string> s_eofAllowed = Set(
            "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc", "tbody", "td", "tfoot", "th", "thead", "tr", "body", "html");

        private void CheckOpenElementsAtEnd(int offset)
        {
            foreach (var e in _open)
            {
                if (!(e.Namespace == HtmlNamespace.Html && s_eofAllowed.Contains(e.LocalName)))
                {
                    Error("open-elements-at-end", offset);
                    return;
                }
            }
        }

        private void CloseP()
        {
            GenerateImpliedEndTags("p");
            if (!CurrentIs("p")) Error("end-tag-mismatch");
            PopUntil("p");
        }

        private bool InBody(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character:
                    if (IsNullSeg(t))
                    {
                        Error("unexpected-null-character", t.Offset);
                        return false;
                    }
                    ReconstructFormatting();
                    InsertCharacters(t.Data, t.Offset);
                    if (!IsWhitespaceSeg(t)) _framesetOk = false;
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.EndOfFile:
                    if (_templateModes.Count > 0) return InTemplate(t);
                    CheckOpenElementsAtEnd(t.Offset);
                    _stopped = true;
                    return false;
                case HtmlTokenKind.StartTag:
                    return InBodyStart(t);
                case HtmlTokenKind.EndTag:
                    return InBodyEnd(t);
            }
            return false;
        }

        private bool InBodyStart(HtmlToken t)
        {
            string name = t.Name!;
            switch (name)
            {
                case "html":
                    Error("unexpected-start-tag", t.Offset);
                    if (StackHas("template")) return false;
                    MergeAttributes(_open[0], t);
                    return false;
                case "base": case "basefont": case "bgsound": case "link": case "meta": case "noframes":
                case "script": case "style": case "template": case "title":
                    return InHead(t);
                case "body":
                    Error("unexpected-start-tag", t.Offset);
                    if (_open.Count == 1 || !_open[1].IsHtml("body") || StackHas("template")) return false;
                    _framesetOk = false;
                    MergeAttributes(_open[1], t);
                    return false;
                case "frameset":
                    Error("unexpected-start-tag", t.Offset);
                    if (_open.Count == 1 || !_open[1].IsHtml("body")) return false;
                    if (!_framesetOk) return false;
                    _open[1].Remove();
                    while (_open.Count > 1) Pop();
                    InsertHtmlElement(t);
                    _mode = Mode.InFrameset;
                    return false;
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    if (InScope("p", Scope.Button)) CloseP();
                    if (CurrentNode is { } cur && cur.Namespace == HtmlNamespace.Html && s_headings.Contains(cur.LocalName))
                    {
                        Error("nested-heading", t.Offset);
                        Pop();
                    }
                    InsertHtmlElement(t);
                    return false;
                case "pre": case "listing":
                    if (InScope("p", Scope.Button)) CloseP();
                    InsertHtmlElement(t);
                    _skipNextNewline = true;
                    _framesetOk = false;
                    return false;
                case "form":
                    if (_form is not null && !StackHas("template"))
                    {
                        Error("unexpected-start-tag", t.Offset);
                        return false;
                    }
                    if (InScope("p", Scope.Button)) CloseP();
                    {
                        var f = InsertHtmlElement(t);
                        if (!StackHas("template")) _form = f;
                    }
                    return false;
                case "li":
                case "dd": case "dt":
                    _framesetOk = false;
                    for (int i = _open.Count - 1; i >= 0; i--)
                    {
                        Work();
                        var node = _open[i];
                        if (name == "li" ? node.IsHtml("li") : (node.IsHtml("dd") || node.IsHtml("dt")))
                        {
                            string closing = node.LocalName;
                            GenerateImpliedEndTags(closing);
                            if (!CurrentIs(closing)) Error("end-tag-mismatch", t.Offset);
                            PopUntil(closing);
                            break;
                        }
                        if (IsSpecial(node) && !(node.Namespace == HtmlNamespace.Html && node.LocalName is "address" or "div" or "p")) break;
                    }
                    if (InScope("p", Scope.Button)) CloseP();
                    InsertHtmlElement(t);
                    return false;
                case "plaintext":
                    if (InScope("p", Scope.Button)) CloseP();
                    InsertHtmlElement(t);
                    _tk.SetState(HtmlTokenizerState.Plaintext);
                    return false;
                case "button":
                    if (InScope("button"))
                    {
                        Error("unexpected-start-tag", t.Offset);
                        GenerateImpliedEndTags();
                        PopUntil("button");
                    }
                    ReconstructFormatting();
                    InsertHtmlElement(t);
                    _framesetOk = false;
                    return false;
                case "a":
                {
                    for (int i = _active.Count - 1; i >= 0; i--)
                    {
                        var f = _active[i];
                        if (f is null) break;
                        if (f.Element.IsHtml("a"))
                        {
                            Error("unexpected-start-tag", t.Offset);
                            var a = f.Element;
                            AdoptionAgency(t);
                            int idx = ActiveIndexOf(a);
                            if (idx >= 0) _active.RemoveAt(idx);
                            _open.Remove(a);
                            break;
                        }
                    }
                    ReconstructFormatting();
                    var el = InsertHtmlElement(t);
                    PushFormatting(el, t);
                    return false;
                }
                case "nobr":
                {
                    ReconstructFormatting();
                    if (InScope("nobr"))
                    {
                        Error("unexpected-start-tag", t.Offset);
                        AdoptionAgency(t);
                        ReconstructFormatting();
                    }
                    var el = InsertHtmlElement(t);
                    PushFormatting(el, t);
                    return false;
                }
                case "applet": case "marquee": case "object":
                    ReconstructFormatting();
                    InsertHtmlElement(t);
                    _active.Add(null);
                    _framesetOk = false;
                    return false;
                case "table":
                    if (_doc.QuirksMode != HtmlQuirksMode.Quirks && InScope("p", Scope.Button)) CloseP();
                    InsertHtmlElement(t);
                    _framesetOk = false;
                    _mode = Mode.InTable;
                    return false;
                case "area": case "br": case "embed": case "img": case "keygen": case "wbr":
                    ReconstructFormatting();
                    InsertHtmlElement(t);
                    Pop();
                    Acknowledge(t);
                    _framesetOk = false;
                    return false;
                case "input":
                {
                    if (_context is not null && _context.IsHtml("select"))
                    {
                        Error("unexpected-start-tag", t.Offset);
                        return false;
                    }
                    if (InScope("select"))
                    {
                        Error("unexpected-start-tag", t.Offset);
                        PopUntil("select");
                    }
                    ReconstructFormatting();
                    InsertHtmlElement(t);
                    Pop();
                    Acknowledge(t);
                    string? type = t.GetAttribute("type");
                    if (type is null || !type.Equals("hidden", StringComparison.OrdinalIgnoreCase)) _framesetOk = false;
                    return false;
                }
                case "param": case "source": case "track":
                    InsertHtmlElement(t);
                    Pop();
                    Acknowledge(t);
                    return false;
                case "hr":
                    if (InScope("p", Scope.Button)) CloseP();
                    if (InScope("select"))
                    {
                        GenerateImpliedEndTags();
                        if (InScope("option") || InScope("optgroup")) Error("unexpected-start-tag", t.Offset);
                    }
                    InsertHtmlElement(t);
                    Pop();
                    Acknowledge(t);
                    _framesetOk = false;
                    return false;
                case "image":
                    Error("image-tag", t.Offset);
                    t.Name = "img";
                    return InBodyStart(t);
                case "textarea":
                    InsertHtmlElement(t);
                    _skipNextNewline = true;
                    _tk.SetState(HtmlTokenizerState.Rcdata);
                    _originalMode = _mode;
                    _framesetOk = false;
                    _mode = Mode.Text;
                    return false;
                case "xmp":
                    if (InScope("p", Scope.Button)) CloseP();
                    ReconstructFormatting();
                    _framesetOk = false;
                    GenericRawText(t, HtmlTokenizerState.Rawtext);
                    return false;
                case "iframe":
                    _framesetOk = false;
                    GenericRawText(t, HtmlTokenizerState.Rawtext);
                    return false;
                case "noembed":
                    GenericRawText(t, HtmlTokenizerState.Rawtext);
                    return false;
                case "noscript" when _scripting:
                    GenericRawText(t, HtmlTokenizerState.Rawtext);
                    return false;
                case "select":
                    if (_context is not null && _context.IsHtml("select"))
                    {
                        Error("unexpected-start-tag", t.Offset);
                        return false;
                    }
                    if (InScope("select"))
                    {
                        Error("unexpected-start-tag", t.Offset);
                        PopUntil("select");
                        return false;
                    }
                    ReconstructFormatting();
                    InsertHtmlElement(t);
                    _framesetOk = false;
                    return false;
                case "option":
                    if (InScope("select"))
                    {
                        GenerateImpliedEndTags("optgroup");
                        if (InScope("option")) Error("unexpected-start-tag", t.Offset);
                    }
                    else if (CurrentIs("option"))
                    {
                        Pop();
                    }
                    ReconstructFormatting();
                    InsertHtmlElement(t);
                    return false;
                case "optgroup":
                    if (InScope("select"))
                    {
                        GenerateImpliedEndTags();
                        if (InScope("option") || InScope("optgroup")) Error("unexpected-start-tag", t.Offset);
                    }
                    else if (CurrentIs("option"))
                    {
                        Pop();
                    }
                    ReconstructFormatting();
                    InsertHtmlElement(t);
                    return false;
                case "rb": case "rtc":
                    if (InScope("ruby"))
                    {
                        GenerateImpliedEndTags();
                        if (!CurrentIs("ruby")) Error("unexpected-start-tag", t.Offset);
                    }
                    InsertHtmlElement(t);
                    return false;
                case "rp": case "rt":
                    if (InScope("ruby"))
                    {
                        GenerateImpliedEndTags("rtc");
                        if (!CurrentIs("rtc") && !CurrentIs("ruby")) Error("unexpected-start-tag", t.Offset);
                    }
                    InsertHtmlElement(t);
                    return false;
                case "math":
                    ReconstructFormatting();
                    InsertForeignElement(t, HtmlNamespace.MathML);
                    if (t.SelfClosing)
                    {
                        Pop();
                        Acknowledge(t);
                    }
                    return false;
                case "svg":
                    ReconstructFormatting();
                    InsertForeignElement(t, HtmlNamespace.Svg);
                    if (t.SelfClosing)
                    {
                        Pop();
                        Acknowledge(t);
                    }
                    return false;
                case "caption": case "col": case "colgroup": case "frame": case "head": case "tbody":
                case "td": case "tfoot": case "th": case "thead": case "tr":
                    Error("unexpected-start-tag", t.Offset);
                    return false;
            }
            if (s_closesPBlock.Contains(name))
            {
                if (InScope("p", Scope.Button)) CloseP();
                InsertHtmlElement(t);
                return false;
            }
            if (s_formattingStart.Contains(name))
            {
                ReconstructFormatting();
                var el = InsertHtmlElement(t);
                PushFormatting(el, t);
                return false;
            }
            ReconstructFormatting();
            InsertHtmlElement(t);
            return false;
        }

        private void MergeAttributes(HtmlElement target, HtmlToken t)
        {
            if (t.AttributeList is null) return;
            foreach (var a in t.AttributeList)
            {
                if (!target.Attributes.Contains(a.Name))
                {
                    _budget.Take();
                    target.Attributes.AddUnchecked(new HtmlAttribute(a.Name, a.Value));
                }
            }
        }

        private bool InBodyEnd(HtmlToken t)
        {
            string name = t.Name!;
            switch (name)
            {
                case "template":
                    return InHead(t);
                case "body":
                    if (!InScope("body"))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    CheckOpenElementsAtEnd(t.Offset);
                    _mode = Mode.AfterBody;
                    return false;
                case "html":
                    if (!InScope("body"))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    CheckOpenElementsAtEnd(t.Offset);
                    _mode = Mode.AfterBody;
                    return true;
                case "form":
                    if (!StackHas("template"))
                    {
                        var node = _form;
                        _form = null;
                        if (node is null || !InScope(node))
                        {
                            Error("unexpected-end-tag", t.Offset);
                            return false;
                        }
                        GenerateImpliedEndTags();
                        if (!ReferenceEquals(CurrentNode, node)) Error("end-tag-mismatch", t.Offset);
                        _open.Remove(node);
                    }
                    else
                    {
                        if (!InScope("form"))
                        {
                            Error("unexpected-end-tag", t.Offset);
                            return false;
                        }
                        GenerateImpliedEndTags();
                        if (!CurrentIs("form")) Error("end-tag-mismatch", t.Offset);
                        PopUntil("form");
                    }
                    return false;
                case "p":
                    if (!InScope("p", Scope.Button))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        InsertHtmlElement(SyntheticTag("p"));
                    }
                    CloseP();
                    return false;
                case "li":
                    if (!InScope("li", Scope.ListItem))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    GenerateImpliedEndTags("li");
                    if (!CurrentIs("li")) Error("end-tag-mismatch", t.Offset);
                    PopUntil("li");
                    return false;
                case "dd": case "dt":
                    if (!InScope(name))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    GenerateImpliedEndTags(name);
                    if (!CurrentIs(name)) Error("end-tag-mismatch", t.Offset);
                    PopUntil(name);
                    return false;
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    if (!InScopeAny(s_headings))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    GenerateImpliedEndTags();
                    if (!CurrentIs(name)) Error("end-tag-mismatch", t.Offset);
                    PopUntilAny(s_headings);
                    return false;
                case "a": case "b": case "big": case "code": case "em": case "font": case "i": case "nobr":
                case "s": case "small": case "strike": case "strong": case "tt": case "u":
                    AdoptionAgency(t);
                    return false;
                case "applet": case "marquee": case "object":
                    if (!InScope(name))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    GenerateImpliedEndTags();
                    if (!CurrentIs(name)) Error("end-tag-mismatch", t.Offset);
                    PopUntil(name);
                    ClearFormattingToMarker();
                    return false;
                case "br":
                {
                    Error("unexpected-end-tag", t.Offset);
                    var br = new HtmlToken(HtmlTokenKind.StartTag) { Name = "br", Offset = t.Offset };
                    return InBodyStart(br);
                }
            }
            if (s_blockEnd.Contains(name))
            {
                if (!InScope(name))
                {
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                }
                GenerateImpliedEndTags();
                if (!CurrentIs(name)) Error("end-tag-mismatch", t.Offset);
                PopUntil(name);
                return false;
            }
            AnyOtherEndTag(t);
            return false;
        }

        private void AnyOtherEndTag(HtmlToken t)
        {
            string name = t.Name!;
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Work();
                var node = _open[i];
                if (node.IsHtml(name))
                {
                    GenerateImpliedEndTags(name);
                    if (!ReferenceEquals(CurrentNode, node)) Error("end-tag-mismatch", t.Offset);
                    while (_open.Count > i) Pop();
                    return;
                }
                if (IsSpecial(node))
                {
                    Error("unexpected-end-tag", t.Offset);
                    return;
                }
            }
        }

        private void AdoptionAgency(HtmlToken t)
        {
            string subject = t.Name!;
            var current = CurrentNode;
            if (current is not null && current.IsHtml(subject) && ActiveIndexOf(current) < 0)
            {
                Pop();
                return;
            }
            for (int outer = 0; outer < 8; outer++)
            {
                Work(4);
                int feIdx = -1;
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var f = _active[i];
                    if (f is null) break;
                    if (f.Element.IsHtml(subject)) { feIdx = i; break; }
                }
                if (feIdx < 0)
                {
                    AnyOtherEndTag(t);
                    return;
                }
                var feEntry = _active[feIdx]!;
                var fe = feEntry.Element;
                int stackIdx = _open.LastIndexOf(fe);
                if (stackIdx < 0)
                {
                    Error("adoption-agency-1.2", t.Offset);
                    _active.RemoveAt(feIdx);
                    return;
                }
                if (!InScope(fe))
                {
                    Error("adoption-agency-4.4", t.Offset);
                    return;
                }
                if (!ReferenceEquals(fe, CurrentNode)) Error("adoption-agency-1.3", t.Offset);

                HtmlElement? furthest = null;
                int furthestIdx = -1;
                for (int i = stackIdx + 1; i < _open.Count; i++)
                {
                    Work();
                    if (IsSpecial(_open[i])) { furthest = _open[i]; furthestIdx = i; break; }
                }
                if (furthest is null)
                {
                    while (_open.Count > stackIdx) Pop();
                    _active.RemoveAt(feIdx);
                    return;
                }
                var commonAncestor = _open[stackIdx - 1];
                int bookmark = feIdx;
                HtmlElement node, lastNode = furthest;
                int nodeIdx = furthestIdx;
                int inner = 0;
                while (true)
                {
                    Work();
                    inner++;
                    nodeIdx--;
                    node = _open[nodeIdx];
                    if (ReferenceEquals(node, fe)) break;
                    int nodeActive = ActiveIndexOf(node);
                    if (inner > 3 && nodeActive >= 0)
                    {
                        _active.RemoveAt(nodeActive);
                        if (nodeActive < bookmark) bookmark--;
                        nodeActive = -1;
                    }
                    if (nodeActive < 0)
                    {
                        _open.RemoveAt(nodeIdx);
                        continue;
                    }
                    var entry = _active[nodeActive]!;
                    var newEl = CreateElement(entry.Token, HtmlNamespace.Html);
                    _active[nodeActive] = new Formatting(newEl, entry.Token);
                    _open[nodeIdx] = newEl;
                    node = newEl;
                    if (ReferenceEquals(lastNode, furthest)) bookmark = nodeActive + 1;
                    lastNode.Remove();
                    node.InsertBeforeCore(lastNode, null);
                    lastNode = node;
                }
                var place = AppropriatePlace(commonAncestor);
                lastNode.Remove();
                InsertAt(lastNode, place);

                var newElement = CreateElement(feEntry.Token, HtmlNamespace.Html);
                while (furthest.FirstChild is { } c)
                {
                    furthest.RemoveChild(c);
                    newElement.InsertBeforeCore(c, null);
                }
                furthest.InsertBeforeCore(newElement, null);

                int feNow = _active.IndexOf(feEntry);
                if (feNow >= 0)
                {
                    _active.RemoveAt(feNow);
                    if (feNow < bookmark) bookmark--;
                }
                if (bookmark > _active.Count) bookmark = _active.Count;
                _active.Insert(bookmark, new Formatting(newElement, feEntry.Token));

                _open.Remove(fe);
                int fIdx = _open.LastIndexOf(furthest);
                _open.Insert(fIdx + 1, newElement);
            }
        }

        // ---------------- text ----------------

        private bool TextMode(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character:
                    InsertCharacters(t.Data, t.Offset);
                    return false;
                case HtmlTokenKind.EndOfFile:
                    Error("eof-in-text", t.Offset);
                    Pop();
                    _mode = _originalMode;
                    return true;
                case HtmlTokenKind.EndTag:
                    Pop();
                    _mode = _originalMode;
                    return false;
            }
            return false;
        }

        // ---------------- tables ----------------

        private void ClearStackToTableContext()
        {
            while (CurrentNode is { } c && !(c.Namespace == HtmlNamespace.Html && c.LocalName is "table" or "template" or "html"))
            {
                Work();
                Pop();
            }
        }

        private void ClearStackToTableBodyContext()
        {
            while (CurrentNode is { } c && !(c.Namespace == HtmlNamespace.Html && c.LocalName is "tbody" or "tfoot" or "thead" or "template" or "html"))
            {
                Work();
                Pop();
            }
        }

        private void ClearStackToTableRowContext()
        {
            while (CurrentNode is { } c && !(c.Namespace == HtmlNamespace.Html && c.LocalName is "tr" or "template" or "html"))
            {
                Work();
                Pop();
            }
        }

        private bool InTable(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when CurrentNode is { } cn && cn.Namespace == HtmlNamespace.Html && cn.LocalName is "table" or "tbody" or "template" or "tfoot" or "thead" or "tr":
                    _pendingTableText.Clear();
                    _pendingTableTextNonWs = false;
                    _originalMode = _mode;
                    _mode = Mode.InTableText;
                    return true;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag:
                    switch (t.Name)
                    {
                        case "caption":
                            ClearStackToTableContext();
                            _active.Add(null);
                            InsertHtmlElement(t);
                            _mode = Mode.InCaption;
                            return false;
                        case "colgroup":
                            ClearStackToTableContext();
                            InsertHtmlElement(t);
                            _mode = Mode.InColumnGroup;
                            return false;
                        case "col":
                            ClearStackToTableContext();
                            InsertHtmlElement(SyntheticTag("colgroup"));
                            _mode = Mode.InColumnGroup;
                            return true;
                        case "tbody": case "tfoot": case "thead":
                            ClearStackToTableContext();
                            InsertHtmlElement(t);
                            _mode = Mode.InTableBody;
                            return false;
                        case "td": case "th": case "tr":
                            ClearStackToTableContext();
                            InsertHtmlElement(SyntheticTag("tbody"));
                            _mode = Mode.InTableBody;
                            return true;
                        case "table":
                            Error("unexpected-start-tag", t.Offset);
                            if (!InScope("table", Scope.Table)) return false;
                            PopUntil("table");
                            ResetInsertionMode();
                            return true;
                        case "style": case "script": case "template":
                            return InHead(t);
                        case "input":
                        {
                            string? type = t.GetAttribute("type");
                            if (type is null || !type.Equals("hidden", StringComparison.OrdinalIgnoreCase)) break;
                            Error("unexpected-start-tag", t.Offset);
                            InsertHtmlElement(t);
                            Pop();
                            Acknowledge(t);
                            return false;
                        }
                        case "form":
                            Error("unexpected-start-tag", t.Offset);
                            if (_form is not null && !ParsingTemplateContents) return false;
                            {
                                var f = InsertHtmlElement(t);
                                if (!ParsingTemplateContents) _form = f;
                                Pop();
                            }
                            return false;
                    }
                    break;
                case HtmlTokenKind.EndTag:
                    switch (t.Name)
                    {
                        case "table":
                            if (!InScope("table", Scope.Table))
                            {
                                Error("unexpected-end-tag", t.Offset);
                                return false;
                            }
                            PopUntil("table");
                            ResetInsertionMode();
                            return false;
                        case "body": case "caption": case "col": case "colgroup": case "html": case "tbody":
                        case "td": case "tfoot": case "th": case "thead": case "tr":
                            Error("unexpected-end-tag", t.Offset);
                            return false;
                        case "template":
                            return InHead(t);
                    }
                    break;
                case HtmlTokenKind.EndOfFile:
                    return InBody(t);
            }
            Error("foster-parenting", t.Offset);
            _fosterParenting = true;
            try
            {
                return InBody(t);
            }
            finally
            {
                _fosterParenting = false;
            }
        }

        private bool InTableText(HtmlToken t)
        {
            if (t.Kind == HtmlTokenKind.Character)
            {
                if (IsNullSeg(t))
                {
                    Error("unexpected-null-character", t.Offset);
                    return false;
                }
                _pendingTableText.Append(t.Data);
                if (!IsWhitespaceSeg(t)) _pendingTableTextNonWs = true;
                return false;
            }
            if (_pendingTableText.Length > 0)
            {
                string text = _pendingTableText.ToString();
                _pendingTableText.Clear();
                if (_pendingTableTextNonWs)
                {
                    Error("foster-parenting-text", t.Offset);
                    var tok = new HtmlToken(HtmlTokenKind.Character) { Data = text, Offset = t.Offset };
                    _fosterParenting = true;
                    try
                    {
                        ReconstructFormatting();
                        InsertCharacters(text, tok.Offset);
                        _framesetOk = false;
                    }
                    finally
                    {
                        _fosterParenting = false;
                    }
                }
                else
                {
                    InsertCharacters(text, t.Offset);
                }
            }
            _mode = _originalMode;
            return true;
        }

        private bool InCaption(HtmlToken t)
        {
            if (IsEnd(t, "caption") || (t.Kind == HtmlTokenKind.StartTag && t.Name is "caption" or "col" or "colgroup" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr") || IsEnd(t, "table"))
            {
                if (!InScope("caption", Scope.Table))
                {
                    Error("unexpected-tag", t.Offset);
                    return false;
                }
                GenerateImpliedEndTags();
                if (!CurrentIs("caption")) Error("end-tag-mismatch", t.Offset);
                PopUntil("caption");
                ClearFormattingToMarker();
                _mode = Mode.InTable;
                return !IsEnd(t, "caption");
            }
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "body" or "col" or "colgroup" or "html" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr")
            {
                Error("unexpected-end-tag", t.Offset);
                return false;
            }
            return InBody(t);
        }

        private bool InColumnGroup(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    InsertCharacters(t.Data, t.Offset);
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.StartTag when t.Name == "col":
                    InsertHtmlElement(t);
                    Pop();
                    Acknowledge(t);
                    return false;
                case HtmlTokenKind.EndTag when t.Name == "colgroup":
                    if (!CurrentIs("colgroup"))
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    Pop();
                    _mode = Mode.InTable;
                    return false;
                case HtmlTokenKind.EndTag when t.Name == "col":
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "template":
                case HtmlTokenKind.EndTag when t.Name == "template":
                    return InHead(t);
                case HtmlTokenKind.EndOfFile:
                    return InBody(t);
            }
            if (!CurrentIs("colgroup"))
            {
                Error("unexpected-in-colgroup", t.Offset);
                return false;
            }
            Pop();
            _mode = Mode.InTable;
            return true;
        }

        private bool InTableBody(HtmlToken t)
        {
            if (IsStart(t, "tr"))
            {
                ClearStackToTableBodyContext();
                InsertHtmlElement(t);
                _mode = Mode.InRow;
                return false;
            }
            if (t.Kind == HtmlTokenKind.StartTag && t.Name is "th" or "td")
            {
                Error("unexpected-start-tag", t.Offset);
                ClearStackToTableBodyContext();
                InsertHtmlElement(SyntheticTag("tr"));
                _mode = Mode.InRow;
                return true;
            }
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "tbody" or "tfoot" or "thead")
            {
                if (!InScope(t.Name!, Scope.Table))
                {
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                }
                ClearStackToTableBodyContext();
                Pop();
                _mode = Mode.InTable;
                return false;
            }
            if ((t.Kind == HtmlTokenKind.StartTag && t.Name is "caption" or "col" or "colgroup" or "tbody" or "tfoot" or "thead") || IsEnd(t, "table"))
            {
                if (!InScope("tbody", Scope.Table) && !InScope("thead", Scope.Table) && !InScope("tfoot", Scope.Table))
                {
                    Error("unexpected-tag", t.Offset);
                    return false;
                }
                ClearStackToTableBodyContext();
                Pop();
                _mode = Mode.InTable;
                return true;
            }
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "body" or "caption" or "col" or "colgroup" or "html" or "td" or "th" or "tr")
            {
                Error("unexpected-end-tag", t.Offset);
                return false;
            }
            return InTable(t);
        }

        private bool InRow(HtmlToken t)
        {
            if (t.Kind == HtmlTokenKind.StartTag && t.Name is "th" or "td")
            {
                ClearStackToTableRowContext();
                InsertHtmlElement(t);
                _mode = Mode.InCell;
                _active.Add(null);
                return false;
            }
            if (IsEnd(t, "tr"))
            {
                if (!InScope("tr", Scope.Table))
                {
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                }
                ClearStackToTableRowContext();
                Pop();
                _mode = Mode.InTableBody;
                return false;
            }
            if ((t.Kind == HtmlTokenKind.StartTag && t.Name is "caption" or "col" or "colgroup" or "tbody" or "tfoot" or "thead" or "tr") || IsEnd(t, "table"))
            {
                if (!InScope("tr", Scope.Table))
                {
                    Error("unexpected-tag", t.Offset);
                    return false;
                }
                ClearStackToTableRowContext();
                Pop();
                _mode = Mode.InTableBody;
                return true;
            }
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "tbody" or "tfoot" or "thead")
            {
                if (!InScope(t.Name!, Scope.Table))
                {
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                }
                if (!InScope("tr", Scope.Table)) return false;
                ClearStackToTableRowContext();
                Pop();
                _mode = Mode.InTableBody;
                return true;
            }
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "body" or "caption" or "col" or "colgroup" or "html" or "td" or "th")
            {
                Error("unexpected-end-tag", t.Offset);
                return false;
            }
            return InTable(t);
        }

        private void CloseCell()
        {
            GenerateImpliedEndTags();
            if (!CurrentIs("td") && !CurrentIs("th")) Error("end-tag-mismatch");
            while (_open.Count > 0)
            {
                Work();
                var e = Pop();
                if (e.IsHtml("td") || e.IsHtml("th")) break;
            }
            ClearFormattingToMarker();
            _mode = Mode.InRow;
        }

        private bool InCell(HtmlToken t)
        {
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "td" or "th")
            {
                if (!InScope(t.Name!, Scope.Table))
                {
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                }
                GenerateImpliedEndTags();
                if (!CurrentIs(t.Name!)) Error("end-tag-mismatch", t.Offset);
                PopUntil(t.Name!);
                ClearFormattingToMarker();
                _mode = Mode.InRow;
                return false;
            }
            if (t.Kind == HtmlTokenKind.StartTag && t.Name is "caption" or "col" or "colgroup" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr")
            {
                if (!InScope("td", Scope.Table) && !InScope("th", Scope.Table))
                {
                    Error("unexpected-start-tag", t.Offset);
                    return false;
                }
                CloseCell();
                return true;
            }
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "body" or "caption" or "col" or "colgroup" or "html")
            {
                Error("unexpected-end-tag", t.Offset);
                return false;
            }
            if (t.Kind == HtmlTokenKind.EndTag && t.Name is "table" or "tbody" or "tfoot" or "thead" or "tr")
            {
                if (!InScope(t.Name!, Scope.Table))
                {
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                }
                CloseCell();
                return true;
            }
            return InBody(t);
        }

        // ---------------- template ----------------

        private bool InTemplate(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character:
                case HtmlTokenKind.Comment:
                case HtmlTokenKind.Doctype:
                    return InBody(t);
                case HtmlTokenKind.StartTag:
                    switch (t.Name)
                    {
                        case "base": case "basefont": case "bgsound": case "link": case "meta": case "noframes":
                        case "script": case "style": case "template": case "title":
                            return InHead(t);
                        case "caption": case "colgroup": case "tbody": case "tfoot": case "thead":
                            SwitchTemplateMode(Mode.InTable);
                            return true;
                        case "col":
                            SwitchTemplateMode(Mode.InColumnGroup);
                            return true;
                        case "tr":
                            SwitchTemplateMode(Mode.InTableBody);
                            return true;
                        case "td": case "th":
                            SwitchTemplateMode(Mode.InRow);
                            return true;
                        default:
                            SwitchTemplateMode(Mode.InBody);
                            return true;
                    }
                case HtmlTokenKind.EndTag:
                    if (t.Name == "template") return InHead(t);
                    Error("unexpected-end-tag", t.Offset);
                    return false;
                case HtmlTokenKind.EndOfFile:
                    if (!StackHas("template"))
                    {
                        _stopped = true;
                        return false;
                    }
                    Error("eof-in-template", t.Offset);
                    PopUntil("template");
                    ClearFormattingToMarker();
                    if (_templateModes.Count > 0) _templateModes.RemoveAt(_templateModes.Count - 1);
                    ResetInsertionMode();
                    return true;
            }
            return false;
        }

        private void SwitchTemplateMode(Mode m)
        {
            if (_templateModes.Count > 0) _templateModes.RemoveAt(_templateModes.Count - 1);
            _templateModes.Add(m);
            _mode = m;
        }

        // ---------------- after body / frameset ----------------

        private bool AfterBody(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    return InBody(t);
                case HtmlTokenKind.Comment:
                    InsertComment(t, (_open[0], null));
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.EndTag when t.Name == "html":
                    if (_context is not null)
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    _mode = Mode.AfterAfterBody;
                    return false;
                case HtmlTokenKind.EndOfFile:
                    _stopped = true;
                    return false;
            }
            Error("unexpected-after-body", t.Offset);
            _mode = Mode.InBody;
            return true;
        }

        private bool InFrameset(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    InsertCharacters(t.Data, t.Offset);
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.StartTag when t.Name == "frameset":
                    InsertHtmlElement(t);
                    return false;
                case HtmlTokenKind.EndTag when t.Name == "frameset":
                    if (_open.Count == 1)
                    {
                        Error("unexpected-end-tag", t.Offset);
                        return false;
                    }
                    Pop();
                    if (_context is null && !CurrentIs("frameset")) _mode = Mode.AfterFrameset;
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "frame":
                    InsertHtmlElement(t);
                    Pop();
                    Acknowledge(t);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "noframes":
                    return InHead(t);
                case HtmlTokenKind.EndOfFile:
                    if (_open.Count != 1) Error("eof-in-frameset", t.Offset);
                    _stopped = true;
                    return false;
            }
            Error("unexpected-in-frameset", t.Offset);
            return false;
        }

        private bool AfterFrameset(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                    InsertCharacters(t.Data, t.Offset);
                    return false;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return false;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.EndTag when t.Name == "html":
                    _mode = Mode.AfterAfterFrameset;
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "noframes":
                    return InHead(t);
                case HtmlTokenKind.EndOfFile:
                    _stopped = true;
                    return false;
            }
            Error("unexpected-after-frameset", t.Offset);
            return false;
        }

        private bool AfterAfterBody(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Comment:
                    InsertComment(t, (_doc, null));
                    return false;
                case HtmlTokenKind.Doctype:
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.EndOfFile:
                    _stopped = true;
                    return false;
            }
            Error("unexpected-after-after-body", t.Offset);
            _mode = Mode.InBody;
            return true;
        }

        private bool AfterAfterFrameset(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Comment:
                    InsertComment(t, (_doc, null));
                    return false;
                case HtmlTokenKind.Doctype:
                case HtmlTokenKind.Character when IsWhitespaceSeg(t):
                case HtmlTokenKind.StartTag when t.Name == "html":
                    return InBody(t);
                case HtmlTokenKind.EndOfFile:
                    _stopped = true;
                    return false;
                case HtmlTokenKind.StartTag when t.Name == "noframes":
                    return InHead(t);
            }
            Error("unexpected-after-after-frameset", t.Offset);
            return false;
        }

        // ---------------- foreign content ----------------

        private static readonly HashSet<string> s_foreignBreakout = Set(
            "b", "big", "blockquote", "body", "br", "center", "code", "dd", "div", "dl", "dt", "em", "embed", "h1", "h2",
            "h3", "h4", "h5", "h6", "head", "hr", "i", "img", "li", "listing", "menu", "meta", "nobr", "ol", "p", "pre",
            "ruby", "s", "small", "span", "strong", "strike", "sub", "sup", "table", "tt", "u", "ul", "var");

        private void ForeignContent(HtmlToken t)
        {
            switch (t.Kind)
            {
                case HtmlTokenKind.Character:
                    if (IsNullSeg(t))
                    {
                        Error("unexpected-null-character", t.Offset);
                        InsertCharacters(new string('�', t.Data.Length), t.Offset);
                        return;
                    }
                    InsertCharacters(t.Data, t.Offset);
                    if (!IsWhitespaceSeg(t)) _framesetOk = false;
                    return;
                case HtmlTokenKind.Comment:
                    InsertComment(t);
                    return;
                case HtmlTokenKind.Doctype:
                    Error("unexpected-doctype", t.Offset);
                    return;
                case HtmlTokenKind.StartTag:
                {
                    bool breakout = s_foreignBreakout.Contains(t.Name!)
                        || (t.Name == "font" && (t.GetAttribute("color") is not null || t.GetAttribute("face") is not null || t.GetAttribute("size") is not null));
                    if (breakout)
                    {
                        Error("unexpected-html-in-foreign-content", t.Offset);
                        PopToHtmlOrIntegrationPoint();
                        ProcessHtml(t);
                        return;
                    }
                    var acn = AdjustedCurrentNode!;
                    var ns = acn.Namespace;
                    string? localName = null;
                    if (ns == HtmlNamespace.Svg && s_svgTags.TryGetValue(t.Name!, out var adjusted)) localName = adjusted;
                    InsertForeignElement(t, ns, localName);
                    if (t.SelfClosing)
                    {
                        Pop();
                        Acknowledge(t);
                    }
                    return;
                }
                case HtmlTokenKind.EndTag:
                {
                    if (t.Name is "br" or "p")
                    {
                        Error("unexpected-html-in-foreign-content", t.Offset);
                        PopToHtmlOrIntegrationPoint();
                        ProcessHtml(t);
                        return;
                    }
                    if (t.Name == "script" && CurrentNode is { } cs && cs.Namespace == HtmlNamespace.Svg && cs.LocalName == "script")
                    {
                        Pop();
                        return;
                    }
                    int i = _open.Count - 1;
                    var node = _open[i];
                    if (!AsciiLowerEquals(node.LocalName, t.Name!)) Error("end-tag-mismatch", t.Offset);
                    while (true)
                    {
                        Work();
                        if (i == 0) return;
                        if (AsciiLowerEquals(node.LocalName, t.Name!))
                        {
                            while (_open.Count > i) Pop();
                            return;
                        }
                        i--;
                        node = _open[i];
                        if (node.Namespace != HtmlNamespace.Html) continue;
                        ProcessHtml(t);
                        return;
                    }
                }
            }
        }

        private void PopToHtmlOrIntegrationPoint()
        {
            while (CurrentNode is { } c && !(IsMathMLTextIntegrationPoint(c) || IsHtmlIntegrationPoint(c) || c.Namespace == HtmlNamespace.Html))
            {
                Work();
                Pop();
            }
        }

        private static bool AsciiLowerEquals(string a, string lowerB)
        {
            if (a.Length != lowerB.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Internal.CharUtil.AsciiLower(a[i]) != lowerB[i]) return false;
            return true;
        }

        // ---------------- fragment support ----------------

        public void InitializeFragment(HtmlElement context)
        {
            _context = context;
            if (context.Namespace == HtmlNamespace.Html)
            {
                switch (context.LocalName)
                {
                    case "title": case "textarea":
                        _tk.SetState(HtmlTokenizerState.Rcdata);
                        break;
                    case "style": case "xmp": case "iframe": case "noembed": case "noframes":
                        _tk.SetState(HtmlTokenizerState.Rawtext);
                        break;
                    case "script":
                        _tk.SetState(HtmlTokenizerState.ScriptData);
                        break;
                    case "noscript":
                        if (_scripting) _tk.SetState(HtmlTokenizerState.Rawtext);
                        break;
                    case "plaintext":
                        _tk.SetState(HtmlTokenizerState.Plaintext);
                        break;
                }
            }
            _budget.Take();
            var root = new HtmlElement("html");
            _doc.InsertBeforeCore(root, null);
            _open.Add(root);
            if (context.IsHtml("template")) _templateModes.Add(Mode.InTemplate);
            ResetInsertionMode();
            for (HtmlNode? n = context; n is not null; n = n.Parent)
            {
                if (n is HtmlElement fe && fe.IsHtml("form"))
                {
                    _form = fe;
                    break;
                }
            }
        }
    }
}
