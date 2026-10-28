using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Markup.Sanitization
{
    /// <summary>
    /// Everything the <see cref="HtmlSanitizer"/> allows. Start from <see cref="Anjal"/> (strict, for mail),
    /// <see cref="Reader"/> (standard, for a document viewer) or <see cref="Empty"/>, then adjust any
    /// property. Network access is never allowed: every remote URL that would be fetched automatically is
    /// removed unless an <see cref="IResourceResolver"/> maps it.
    /// </summary>
    public sealed class SanitizerPolicy
    {
        private static HashSet<string> Set(params string[] items) => new HashSet<string>(items, StringComparer.Ordinal);

        // ---------------- elements ----------------

        /// <summary>HTML elements kept (lowercase local names).</summary>
        public HashSet<string> AllowedElements { get; private set; } = Set();

        /// <summary>
        /// HTML elements removed together with their content. Elements neither allowed nor listed here get
        /// <see cref="DefaultElementAction"/>.
        /// </summary>
        public HashSet<string> DroppedElements { get; private set; } = Set();

        /// <summary>Action for HTML elements in neither list (unknown, custom). Default <see cref="ElementAction.Unwrap"/>.</summary>
        public ElementAction DefaultElementAction { get; set; } = ElementAction.Unwrap;

        /// <summary>Keep SVG content (only <see cref="AllowedSvgElements"/>, local references only). Default false.</summary>
        public bool AllowSvg { get; set; }

        /// <summary>SVG elements kept when <see cref="AllowSvg"/> is set (case-sensitive local names).</summary>
        public HashSet<string> AllowedSvgElements { get; private set; } = Set();

        /// <summary>SVG attributes kept (local names as adjusted by the parser, e.g. <c>viewBox</c>).</summary>
        public HashSet<string> AllowedSvgAttributes { get; private set; } = Set();

        /// <summary>Keep MathML content (only <see cref="AllowedMathMLElements"/>). Default false.</summary>
        public bool AllowMathML { get; set; }

        /// <summary>MathML elements kept when <see cref="AllowMathML"/> is set.</summary>
        public HashSet<string> AllowedMathMLElements { get; private set; } = Set();

        /// <summary>MathML attributes kept.</summary>
        public HashSet<string> AllowedMathMLAttributes { get; private set; } = Set();

        /// <summary>Keep comments (never conditional comments). Default false.</summary>
        public bool AllowComments { get; set; }

        // ---------------- attributes ----------------

        /// <summary>Attributes allowed on every allowed HTML element.</summary>
        public HashSet<string> GlobalAttributes { get; private set; } = Set();

        /// <summary>Additional attributes allowed per HTML element.</summary>
        public Dictionary<string, HashSet<string>> ElementAttributes { get; private set; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>Allow <c>data-*</c> attributes. Default true.</summary>
        public bool AllowDataAttributes { get; set; } = true;

        /// <summary>Allow <c>aria-*</c> attributes (add <c>role</c> to <see cref="GlobalAttributes"/> separately). Default true.</summary>
        public bool AllowAriaAttributes { get; set; } = true;

        /// <summary>
        /// Prefix added to <c>id</c> and <c>name</c> values (and to references to them: <c>#fragment</c> links,
        /// <c>for</c>, <c>headers</c>, <c>usemap</c>, <c>aria-*</c> id references, CSS id selectors), so content
        /// cannot clobber the host page's DOM. Null or empty disables id prefixing. The same prefix namespaces
        /// the CSS names a style sheet declares (<c>@font-face</c> families, <c>@keyframes</c> and
        /// <c>@counter-style</c> names) and their references; when it is empty, <c>ScopeClass + "-"</c> (or
        /// <c>chuvadi-</c>) is used for those. Default <c>user-content-</c>.
        /// </summary>
        public string? IdPrefix { get; set; } = "user-content-";

        // ---------------- URLs ----------------

        /// <summary>Schemes kept in navigation attributes (<c>href</c>). Default http, https, mailto.</summary>
        public HashSet<string> AllowedLinkSchemes { get; private set; } = Set("http", "https", "mailto");

        /// <summary>Allow relative URLs in links (resolved by the host). Default false.</summary>
        public bool AllowRelativeLinks { get; set; }

        /// <summary>How links are emitted. Default <see cref="LinkHandling.Keep"/>.</summary>
        public LinkHandling Links { get; set; } = LinkHandling.Keep;

        /// <summary>Template for <see cref="LinkHandling.Rewrite"/>; <c>{url}</c> is replaced by the percent-encoded destination.</summary>
        public string LinkRewriteTemplate { get; set; } = "#{url}";

        /// <summary>Attribute that receives the destination of an inert or rewritten link (null to omit). Default <c>data-chuvadi-href</c>; incoming attributes with this name are always removed.</summary>
        public string? InertLinkAttribute { get; set; } = "data-chuvadi-href";

        /// <summary>Force <c>target="_blank"</c> and <c>rel="noopener noreferrer"</c> on kept links. Default true.</summary>
        public bool LinksOpenInNewWindow { get; set; } = true;

        /// <summary>MIME types allowed in <c>data:</c> image URIs (lowercase). Default png, jpeg, gif, webp, bmp, avif, x-icon.</summary>
        public HashSet<string> AllowedDataImageTypes { get; private set; } = Set("image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/bmp", "image/avif", "image/x-icon", "image/vnd.microsoft.icon");

        /// <summary>MIME types allowed in <c>data:</c> font URIs for <c>@font-face</c>. Default woff, woff2, ttf, otf.</summary>
        public HashSet<string> AllowedDataFontTypes { get; private set; } = Set("font/woff", "font/woff2", "font/ttf", "font/otf", "font/sfnt", "application/font-woff", "application/font-woff2", "application/x-font-ttf", "application/x-font-opentype", "application/vnd.ms-fontobject");

        /// <summary>MIME types allowed in <c>data:</c> media URIs (audio/video). Default none.</summary>
        public HashSet<string> AllowedDataMediaTypes { get; private set; } = Set();

        /// <summary>Resolver for <c>cid:</c>, relative and other non-data resource URLs. Default null (all blocked).</summary>
        public IResourceResolver? ResourceResolver { get; set; }

        // ---------------- forms ----------------

        /// <summary>
        /// Keep form controls (inert: <c>form</c> elements are always unwrapped, submit buttons become
        /// <c>type="button"</c>, and form-submission attributes are removed). Default false.
        /// </summary>
        public bool AllowFormControls { get; set; }

        // ---------------- CSS ----------------

        /// <summary>Keep sanitized <c>style</c> attributes. Default true.</summary>
        public bool AllowStyleAttribute { get; set; } = true;

        /// <summary>Handling of <c>&lt;style&gt;</c> elements. Default <see cref="StyleSheetHandling.SanitizeScoped"/>.</summary>
        public StyleSheetHandling StyleSheets { get; set; } = StyleSheetHandling.SanitizeScoped;

        /// <summary>
        /// Class of the wrapper element emitted around the output (and the scope for style sheets). Null or
        /// empty emits no wrapper (scoped sheets then fall back to dropping). Default <c>chuvadi-content</c>.
        /// </summary>
        public string? ScopeClass { get; set; } = "chuvadi-content";

        /// <summary>Element name of the wrapper. Default <c>div</c>.</summary>
        public string WrapperElement { get; set; } = "div";

        /// <summary>Copy the document's <c>body</c> presentation (bgcolor, text color, background, style) onto the wrapper. Default true.</summary>
        public bool TransferBodyStyle { get; set; } = true;

        /// <summary>CSS properties allowed (lowercase). Null allows every property not in <see cref="DeniedCssProperties"/>. Default null.</summary>
        public HashSet<string>? AllowedCssProperties { get; set; }

        /// <summary>CSS properties always removed.</summary>
        /// <remarks>
        /// Besides the script-capable legacy properties, the default list holds properties whose names live in a
        /// document-global namespace and could tie content to host elements: anchor positioning
        /// (<c>anchor-name</c>, <c>position-anchor</c>, <c>anchor-scope</c>, <c>position-try*</c>) and view
        /// transitions (<c>view-transition-name</c>, <c>view-transition-class</c>).
        /// </remarks>
        public HashSet<string> DeniedCssProperties { get; private set; } = Set("behavior", "-ms-behavior", "-moz-binding", "binding", "-o-link", "-o-link-source", "-o-replace", "-webkit-user-modify", "user-modify",
            "anchor-name", "position-anchor", "anchor-scope", "position-try", "position-try-fallbacks", "position-try-options", "position-try-order", "position-visibility", "view-transition-name", "view-transition-class");

        /// <summary>Allow custom properties (<c>--x</c>) and <c>var()</c>. Default false.</summary>
        public bool AllowCssCustomProperties { get; set; }

        /// <summary>Allow <c>position: fixed</c> and <c>position: sticky</c> (content could overlay the host UI). Default false.</summary>
        public bool AllowFixedPositioning { get; set; }

        /// <summary>
        /// When a blocked image URL is a whole top-level value item of an image property (<c>background</c>,
        /// <c>background-image</c>, <c>list-style</c>, <c>border-image</c>, <c>mask</c>, <c>content</c> ...), replace it
        /// with <c>none</c> and keep the rest of the declaration (such as a background colour) instead of
        /// removing the declaration. Default true.
        /// </summary>
        public bool ReplaceBlockedCssImagesWithNone { get; set; } = true;

        /// <summary>
        /// At-rules kept in style sheets (lowercase names without '@'). Default: media, supports, container,
        /// keyframes (and vendor forms), font-face and counter-style. The names a sheet declares with
        /// <c>@font-face</c>, <c>@keyframes</c> and <c>@counter-style</c> are moved into the content's namespace
        /// (see <see cref="IdPrefix"/>) so they cannot restyle the host page. <c>@page</c> (prints the whole host
        /// page) and <c>@layer</c> (global cascade layers) are not allowed; adding them back gives content
        /// page-wide effect.
        /// </summary>
        public HashSet<string> AllowedAtRules { get; private set; } = Set("media", "supports", "keyframes", "-webkit-keyframes", "-moz-keyframes", "font-face", "container", "counter-style");

        /// <summary>CSS functions whose presence removes a declaration (lowercase, vendor prefix stripped before matching).</summary>
        public HashSet<string> DeniedCssFunctions { get; private set; } = Set("expression", "element", "paint", "-moz-element", "anchor", "anchor-size");

        // ---------------- robustness ----------------

        /// <summary>
        /// Maximum extra passes re-parsing and re-sanitizing the output until it is a fixed point
        /// (closes parser-differential mutation XSS). Default 3.
        /// </summary>
        public int MaxStabilityPasses { get; set; } = 3;

        /// <summary>What to return if the output never stabilizes. Default <see cref="UnstableOutputHandling.PlainText"/>.</summary>
        public UnstableOutputHandling UnstableOutput { get; set; } = UnstableOutputHandling.PlainText;

        /// <summary>Resource limits for parsing, CSS and output.</summary>
        public MarkupLimits Limits { get; set; } = new MarkupLimits();

        /// <summary>Returns a deep copy.</summary>
        public SanitizerPolicy Clone()
        {
            var c = (SanitizerPolicy)MemberwiseClone();
            c.AllowedElements = new HashSet<string>(AllowedElements, StringComparer.Ordinal);
            c.DroppedElements = new HashSet<string>(DroppedElements, StringComparer.Ordinal);
            c.AllowedSvgElements = new HashSet<string>(AllowedSvgElements, StringComparer.Ordinal);
            c.AllowedSvgAttributes = new HashSet<string>(AllowedSvgAttributes, StringComparer.Ordinal);
            c.AllowedMathMLElements = new HashSet<string>(AllowedMathMLElements, StringComparer.Ordinal);
            c.AllowedMathMLAttributes = new HashSet<string>(AllowedMathMLAttributes, StringComparer.Ordinal);
            c.GlobalAttributes = new HashSet<string>(GlobalAttributes, StringComparer.Ordinal);
            c.ElementAttributes = ElementAttributes.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            c.AllowedLinkSchemes = new HashSet<string>(AllowedLinkSchemes, StringComparer.Ordinal);
            c.AllowedDataImageTypes = new HashSet<string>(AllowedDataImageTypes, StringComparer.Ordinal);
            c.AllowedDataFontTypes = new HashSet<string>(AllowedDataFontTypes, StringComparer.Ordinal);
            c.AllowedDataMediaTypes = new HashSet<string>(AllowedDataMediaTypes, StringComparer.Ordinal);
            c.AllowedCssProperties = AllowedCssProperties is null ? null : new HashSet<string>(AllowedCssProperties, StringComparer.Ordinal);
            c.DeniedCssProperties = new HashSet<string>(DeniedCssProperties, StringComparer.Ordinal);
            c.AllowedAtRules = new HashSet<string>(AllowedAtRules, StringComparer.Ordinal);
            c.DeniedCssFunctions = new HashSet<string>(DeniedCssFunctions, StringComparer.Ordinal);
            c.Limits = Limits.Clone();
            return c;
        }

        /// <summary>Allows the given attributes on an element (adding to any already allowed).</summary>
        public SanitizerPolicy AllowAttributes(string element, params string[] attributes)
        {
            if (!ElementAttributes.TryGetValue(element, out var set))
                ElementAttributes[element] = set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in attributes) set.Add(a);
            return this;
        }

        // ---------------- presets ----------------

        private static readonly string[] s_textElements =
        {
            "a", "abbr", "address", "article", "aside", "b", "bdi", "bdo", "big", "blockquote", "br", "caption", "center",
            "cite", "code", "col", "colgroup", "data", "dd", "del", "details", "dfn", "div", "dl", "dt", "em", "figcaption",
            "figure", "font", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "i", "img", "ins",
            "kbd", "li", "main", "map", "area", "mark", "nav", "ol", "p", "pre", "q", "rp", "rt", "ruby", "s", "samp",
            "search", "section", "small", "span", "strike", "strong", "sub", "summary", "sup", "table", "tbody", "td",
            "tfoot", "th", "thead", "time", "tr", "tt", "u", "ul", "var", "wbr", "label", "fieldset", "legend",
        };

        private static readonly string[] s_dropped =
        {
            "script", "style", "template", "iframe", "frame", "frameset", "object", "embed", "applet", "param", "noscript",
            "noembed", "noframes", "xmp", "plaintext", "title", "base", "basefont", "link", "meta", "head", "dialog",
            "portal", "fencedframe", "slot", "math", "svg", "canvas", "selectedcontent", "isindex", "keygen", "bgsound",
            "audio", "video", "source", "track", "input", "select", "option", "optgroup", "textarea", "datalist",
            "output", "progress", "meter", "image",
        };

        private static void AddCommonAttributes(SanitizerPolicy p)
        {
            foreach (var a in new[] { "id", "class", "style", "title", "lang", "dir", "role", "translate", "hidden", "spellcheck", "inert" })
                p.GlobalAttributes.Add(a);
            p.AllowAttributes("a", "href", "name", "target", "rel", "hreflang", "type");
            p.AllowAttributes("area", "href", "alt", "coords", "shape", "target", "rel", "hreflang", "type");
            p.AllowAttributes("img", "src", "alt", "width", "height", "border", "align", "hspace", "vspace", "usemap", "ismap", "loading", "decoding");
            p.AllowAttributes("map", "name");
            p.AllowAttributes("table", "border", "cellpadding", "cellspacing", "width", "height", "align", "bgcolor", "background", "frame", "rules", "summary");
            p.AllowAttributes("td", "colspan", "rowspan", "headers", "scope", "abbr", "align", "valign", "width", "height", "bgcolor", "background", "nowrap", "axis", "char", "charoff");
            p.AllowAttributes("th", "colspan", "rowspan", "headers", "scope", "abbr", "align", "valign", "width", "height", "bgcolor", "background", "nowrap", "axis", "char", "charoff");
            p.AllowAttributes("tr", "align", "valign", "bgcolor", "height", "char", "charoff");
            p.AllowAttributes("tbody", "align", "valign", "char", "charoff");
            p.AllowAttributes("thead", "align", "valign", "char", "charoff");
            p.AllowAttributes("tfoot", "align", "valign", "char", "charoff");
            p.AllowAttributes("col", "span", "width", "align", "valign", "char", "charoff");
            p.AllowAttributes("colgroup", "span", "width", "align", "valign", "char", "charoff");
            p.AllowAttributes("caption", "align");
            p.AllowAttributes("font", "color", "face", "size");
            foreach (var e in new[] { "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "center", "legend" }) p.AllowAttributes(e, "align");
            p.AllowAttributes("hr", "align", "noshade", "size", "width", "color");
            p.AllowAttributes("ol", "start", "type", "reversed", "compact");
            p.AllowAttributes("ul", "type", "compact");
            p.AllowAttributes("li", "value", "type");
            p.AllowAttributes("dl", "compact");
            p.AllowAttributes("blockquote", "cite");
            p.AllowAttributes("q", "cite");
            p.AllowAttributes("del", "cite", "datetime");
            p.AllowAttributes("ins", "cite", "datetime");
            p.AllowAttributes("time", "datetime");
            p.AllowAttributes("data", "value");
            p.AllowAttributes("pre", "width");
            p.AllowAttributes("details", "open");
            p.AllowAttributes("label", "for");
            p.AllowAttributes("fieldset", "disabled");
        }

        /// <summary>
        /// Strict policy for untrusted mail shown inside a web page (Anjal): no forms or form controls,
        /// images only as <c>data:</c> URIs (or what the resolver maps, such as <c>cid:</c> parts), no SVG,
        /// MathML, audio or video, no web fonts (<c>@font-face</c> is opt-in), links made inert, style sheets
        /// sanitized and scoped, ids and global CSS names prefixed,
        /// everything that could reach the network removed.
        /// </summary>
        public static SanitizerPolicy Anjal()
        {
            var p = new SanitizerPolicy();
            foreach (var e in s_textElements) p.AllowedElements.Add(e);
            p.AllowedElements.Remove("label");
            foreach (var e in s_dropped) p.DroppedElements.Add(e);
            AddCommonAttributes(p);
            p.Links = LinkHandling.Inert;
            p.AllowRelativeLinks = false;
            p.AllowFormControls = false;
            p.AllowedDataImageTypes.Remove("image/svg+xml");
            // web fonts are opt-in for mail: add "font-face" back to AllowedAtRules to keep them
            p.AllowedAtRules.Remove("font-face");
            p.Limits = MarkupLimits.Strict();
            return p;
        }

        /// <summary>
        /// Standard policy for a document viewer (Reader): everything in <see cref="Anjal"/> plus inert form
        /// controls, audio and video (sources through the resolver only), a safe SVG subset with local
        /// references only, MathML presentation markup, relative and fragment links kept for the host to
        /// handle. Network access is still impossible without the host's resolver.
        /// </summary>
        public static SanitizerPolicy Reader()
        {
            var p = new SanitizerPolicy();
            foreach (var e in s_textElements) p.AllowedElements.Add(e);
            foreach (var e in s_dropped) p.DroppedElements.Add(e);
            foreach (var e in new[] { "input", "select", "option", "optgroup", "textarea", "button", "datalist", "output", "progress", "meter", "audio", "video", "source", "track", "picture" })
            {
                p.DroppedElements.Remove(e);
                p.AllowedElements.Add(e);
            }
            p.DroppedElements.Remove("svg");
            p.DroppedElements.Remove("math");
            AddCommonAttributes(p);
            p.AllowAttributes("input", "type", "name", "value", "checked", "disabled", "readonly", "placeholder", "size", "maxlength", "minlength", "min", "max", "step", "multiple", "list", "pattern", "required", "alt", "width", "height");
            p.AllowAttributes("select", "name", "multiple", "size", "disabled", "required");
            p.AllowAttributes("option", "value", "selected", "disabled", "label");
            p.AllowAttributes("optgroup", "label", "disabled");
            p.AllowAttributes("textarea", "name", "rows", "cols", "placeholder", "disabled", "readonly", "wrap", "maxlength", "minlength", "required");
            p.AllowAttributes("button", "type", "name", "value", "disabled");
            p.AllowAttributes("output", "for", "name");
            p.AllowAttributes("progress", "value", "max");
            p.AllowAttributes("meter", "value", "min", "max", "low", "high", "optimum");
            p.AllowAttributes("audio", "src", "controls", "loop", "muted", "preload");
            p.AllowAttributes("video", "src", "controls", "loop", "muted", "preload", "poster", "width", "height", "playsinline");
            p.AllowAttributes("source", "src", "type", "media", "sizes");
            p.AllowAttributes("track", "src", "kind", "srclang", "label", "default");
            p.AllowFormControls = true;
            p.AllowRelativeLinks = true;
            p.Links = LinkHandling.Keep;
            p.AllowedDataImageTypes.Add("image/svg+xml");
            p.AllowedDataMediaTypes.UnionWith(new[] { "audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav", "audio/webm", "video/mp4", "video/webm", "video/ogg" });
            p.AllowSvg = true;
            foreach (var e in new[]
            {
                "svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "tspan", "textPath",
                "defs", "symbol", "use", "title", "desc", "linearGradient", "radialGradient", "stop", "clipPath", "mask",
                "pattern", "marker", "image", "filter", "feBlend", "feColorMatrix", "feComponentTransfer", "feComposite",
                "feConvolveMatrix", "feDiffuseLighting", "feDisplacementMap", "feDistantLight", "feDropShadow", "feFlood",
                "feFuncA", "feFuncB", "feFuncG", "feFuncR", "feGaussianBlur", "feMerge", "feMergeNode", "feMorphology",
                "feOffset", "fePointLight", "feSpecularLighting", "feSpotLight", "feTile", "feTurbulence", "switch", "view",
            })
                p.AllowedSvgElements.Add(e);
            foreach (var a in new[]
            {
                "id", "class", "style", "transform", "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "fx", "fy",
                "width", "height", "d", "points", "viewBox", "preserveAspectRatio", "fill", "fill-opacity", "fill-rule",
                "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin", "stroke-dasharray",
                "stroke-dashoffset", "stroke-miterlimit", "opacity", "color", "font-family", "font-size", "font-weight",
                "font-style", "text-anchor", "dominant-baseline", "alignment-baseline", "baseline-shift", "letter-spacing",
                "word-spacing", "text-decoration", "dx", "dy", "rotate", "textLength", "lengthAdjust", "offset", "stop-color",
                "stop-opacity", "gradientUnits", "gradientTransform", "spreadMethod", "clip-path", "clipPathUnits",
                "clip-rule", "mask", "maskUnits", "maskContentUnits", "patternUnits", "patternContentUnits",
                "patternTransform", "marker-start", "marker-mid", "marker-end", "markerWidth", "markerHeight", "markerUnits",
                "refX", "refY", "orient", "visibility", "display", "overflow", "vector-effect", "shape-rendering",
                "text-rendering", "image-rendering", "href", "filter", "filterUnits", "primitiveUnits", "in", "in2", "result",
                "stdDeviation", "type", "values", "mode", "operator", "k1", "k2", "k3", "k4", "flood-color",
                "flood-opacity", "lighting-color", "edgeMode", "kernelMatrix", "order", "divisor", "bias", "targetX",
                "targetY", "preserveAlpha", "surfaceScale", "diffuseConstant", "specularConstant", "specularExponent",
                "azimuth", "elevation", "pointsAtX", "pointsAtY", "pointsAtZ", "limitingConeAngle", "scale",
                "xChannelSelector", "yChannelSelector", "radius", "baseFrequency", "numOctaves", "seed", "stitchTiles",
                "tableValues", "slope", "intercept", "amplitude", "exponent", "requiredExtensions", "systemLanguage",
                "role", "aria-label", "aria-hidden", "lang", "xml:lang", "xml:space", "pathLength",
            })
                p.AllowedSvgAttributes.Add(a);
            p.AllowMathML = true;
            foreach (var e in new[]
            {
                "math", "mi", "mn", "mo", "ms", "mtext", "mspace", "mrow", "mfrac", "msqrt", "mroot", "mstyle", "merror",
                "mpadded", "mphantom", "msub", "msup", "msubsup", "munder", "mover", "munderover", "mmultiscripts",
                "mprescripts", "none", "mtable", "mtr", "mtd", "semantics", "annotation", "menclose", "mlabeledtr",
            })
                p.AllowedMathMLElements.Add(e);
            foreach (var a in new[]
            {
                "display", "mathvariant", "mathsize", "mathcolor", "mathbackground", "dir", "displaystyle", "scriptlevel",
                "fence", "separator", "stretchy", "symmetric", "largeop", "movablelimits", "accent", "accentunder", "lspace",
                "rspace", "minsize", "maxsize", "linethickness", "width", "height", "depth", "voffset", "rowspan",
                "columnspan", "columnalign", "rowalign", "columnlines", "rowlines", "frame", "notation", "encoding",
                "id", "class", "style",
            })
                p.AllowedMathMLAttributes.Add(a);
            return p;
        }

        /// <summary>A policy that allows nothing (text only), to build from.</summary>
        public static SanitizerPolicy Empty()
        {
            var p = new SanitizerPolicy
            {
                StyleSheets = StyleSheetHandling.Drop,
                AllowStyleAttribute = false,
                AllowDataAttributes = false,
                AllowAriaAttributes = false,
            };
            foreach (var e in s_dropped) p.DroppedElements.Add(e);
            return p;
        }
    }
}
