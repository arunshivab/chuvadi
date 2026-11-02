using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Markup.Sanitization
{
    /// <summary>
    /// Everything the <see cref="HtmlSanitizer"/> allows. A new policy is the safest configuration: text,
    /// tables, lists and images as <c>data:</c> URIs only; links inert; no form controls, SVG, MathML,
    /// audio, video or embedded fonts; style sheets sanitized and scoped; ids and document-global CSS names
    /// prefixed; strict limits. Each capability is a plain switch that the application turns on in its own
    /// code. The library never fetches anything: a remote or relative resource appears in the output only
    /// when the application's <see cref="IResourceResolver"/> supplies it.
    /// </summary>
    public sealed class SanitizerPolicy
    {
        private static HashSet<string> Set(params string[] items) => new HashSet<string>(items, StringComparer.Ordinal);

        /// <summary>Creates the safest policy (see the class remarks); turn capabilities on with the switches.</summary>
        public SanitizerPolicy()
        {
            AllowedElements.UnionWith(s_textElements);
            DroppedElements.UnionWith(s_dropped);
            foreach (var a in new[] { "id", "class", "style", "title", "lang", "dir", "role", "translate", "hidden", "spellcheck", "inert" })
                GlobalAttributes.Add(a);
            AddTextAttributes(this);
            AllowedSvgElements.UnionWith(s_svgElements);
            AllowedSvgAttributes.UnionWith(s_svgAttributes);
            AllowedMathMLElements.UnionWith(s_mathElements);
            AllowedMathMLAttributes.UnionWith(s_mathAttributes);
        }

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

        /// <summary>
        /// Keep SVG content: the safe subset in <see cref="AllowedSvgElements"/> and <see cref="AllowedSvgAttributes"/>,
        /// with local (<c>#id</c>) references only. Default false (SVG is removed with its content).
        /// </summary>
        public bool AllowSvg { get; set; }

        /// <summary>SVG elements kept when <see cref="AllowSvg"/> is set (case-sensitive local names). Default: shapes, text, paint servers, clipping, masking, markers, filters.</summary>
        public HashSet<string> AllowedSvgElements { get; private set; } = Set();

        /// <summary>SVG attributes kept (local names as adjusted by the parser, e.g. <c>viewBox</c>).</summary>
        public HashSet<string> AllowedSvgAttributes { get; private set; } = Set();

        /// <summary>Keep MathML presentation markup (only <see cref="AllowedMathMLElements"/>). Default false (MathML is removed with its content).</summary>
        public bool AllowMathML { get; set; }

        /// <summary>MathML elements kept when <see cref="AllowMathML"/> is set. Default: the presentation elements.</summary>
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

        /// <summary>How links are emitted. Default <see cref="LinkHandling.Inert"/> (no navigation; the destination is kept in <see cref="InertLinkAttribute"/>).</summary>
        public LinkHandling Links { get; set; } = LinkHandling.Inert;

        /// <summary>Template for <see cref="LinkHandling.Rewrite"/>; <c>{url}</c> is replaced by the percent-encoded destination.</summary>
        public string LinkRewriteTemplate { get; set; } = "#{url}";

        /// <summary>Attribute that receives the destination of an inert or rewritten link (null to omit). Default <c>data-chuvadi-href</c>; incoming attributes with this name are always removed.</summary>
        public string? InertLinkAttribute { get; set; } = "data-chuvadi-href";

        /// <summary>Force <c>target="_blank"</c> and <c>rel="noopener noreferrer"</c> on kept links. Default true.</summary>
        public bool LinksOpenInNewWindow { get; set; } = true;

        /// <summary>MIME types allowed in <c>data:</c> image URIs (lowercase). Default png, jpeg, gif, webp, bmp, avif, x-icon (SVG images: see <see cref="AllowSvgDataImages"/>).</summary>
        public HashSet<string> AllowedDataImageTypes { get; private set; } = Set("image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/bmp", "image/avif", "image/x-icon", "image/vnd.microsoft.icon");

        /// <summary>MIME types allowed in <c>data:</c> font URIs for <c>@font-face</c>. Default woff, woff2, ttf, otf.</summary>
        public HashSet<string> AllowedDataFontTypes { get; private set; } = Set("font/woff", "font/woff2", "font/ttf", "font/otf", "font/sfnt", "application/font-woff", "application/font-woff2", "application/x-font-ttf", "application/x-font-opentype", "application/vnd.ms-fontobject");

        /// <summary>
        /// Allow <c>data:image/svg+xml</c> images (in <c>img</c>, CSS and SVG <c>image</c>). Browsers never run
        /// scripts in an SVG used as an image, but the image itself can be large or complex. Default false.
        /// </summary>
        public bool AllowSvgDataImages { get; set; }

        /// <summary>MIME types allowed in <c>data:</c> media URIs when <see cref="AllowMedia"/> is set. Default mpeg, mp4, ogg, wav and webm audio and video.</summary>
        public HashSet<string> AllowedDataMediaTypes { get; private set; } = Set("audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav", "audio/webm", "video/mp4", "video/webm", "video/ogg");

        /// <summary>
        /// Keep <c>audio</c>, <c>video</c>, <c>source</c>, <c>track</c> and <c>picture</c>. Their sources must be
        /// <c>data:</c> URIs of <see cref="AllowedDataMediaTypes"/> or come from the <see cref="ResourceResolver"/>.
        /// Default false (audio and video are removed with their content; <c>picture</c> is unwrapped).
        /// </summary>
        public bool AllowMedia { get; set; }

        /// <summary>
        /// The application's resolver for <c>cid:</c>, relative, remote and other non-<c>data:</c> resource URLs.
        /// The library itself never fetches anything. Default null: every such resource is removed and reported
        /// in <see cref="SanitizeResult.BlockedResources"/>.
        /// </summary>
        public IResourceResolver? ResourceResolver { get; set; }

        // ---------------- forms ----------------

        /// <summary>
        /// Keep form controls (<c>label</c>, <c>input</c>, <c>select</c>, <c>option</c>, <c>optgroup</c>, <c>textarea</c>, <c>button</c>,
        /// <c>datalist</c>, <c>output</c>, <c>progress</c>, <c>meter</c>), inert: <c>form</c> elements are always
        /// unwrapped and form-submission attributes are never kept. Default false (controls are removed;
        /// <c>button</c> and <c>label</c> are unwrapped to their text).
        /// </summary>
        public bool AllowFormControls { get; set; }

        // ---------------- CSS ----------------

        /// <summary>Keep <c>style</c> attributes; every declaration is still filtered. Default true.</summary>
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
        /// Keep <c>@font-face</c> rules (fonts only as <c>data:</c> URIs of <see cref="AllowedDataFontTypes"/> or
        /// from the <see cref="ResourceResolver"/>). Their family names are always moved into the content's
        /// namespace, with every reference to them, so they cannot change the host page's fonts. Default false.
        /// </summary>
        public bool AllowEmbeddedFonts { get; set; }

        /// <summary>
        /// At-rules kept in style sheets (lowercase names without '@'). Default: media, supports, container,
        /// keyframes (and vendor forms) and counter-style; <c>@font-face</c> is controlled by
        /// <see cref="AllowEmbeddedFonts"/>. The names a sheet declares with <c>@keyframes</c> and
        /// <c>@counter-style</c> are moved into the content's namespace (see <see cref="IdPrefix"/>) so they cannot
        /// restyle the host page. <c>@page</c> (prints the whole host page) and <c>@layer</c> (global cascade
        /// layers) are not allowed; adding them back gives content page-wide effect.
        /// </summary>
        public HashSet<string> AllowedAtRules { get; private set; } = Set("media", "supports", "keyframes", "-webkit-keyframes", "-moz-keyframes", "container", "counter-style");

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

        /// <summary>Resource limits for parsing, CSS and output. Default <see cref="MarkupLimits.Strict"/>.</summary>
        public MarkupLimits Limits { get; set; } = MarkupLimits.Strict();

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

        // ---------------- built-in sets ----------------

        private static readonly string[] s_textElements =
        {
            "a", "abbr", "address", "article", "aside", "b", "bdi", "bdo", "big", "blockquote", "br", "caption", "center",
            "cite", "code", "col", "colgroup", "data", "dd", "del", "details", "dfn", "div", "dl", "dt", "em", "figcaption",
            "figure", "font", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "i", "img", "ins",
            "kbd", "li", "main", "map", "area", "mark", "nav", "ol", "p", "pre", "q", "rp", "rt", "ruby", "s", "samp",
            "search", "section", "small", "span", "strike", "strong", "sub", "summary", "sup", "table", "tbody", "td",
            "tfoot", "th", "thead", "time", "tr", "tt", "u", "ul", "var", "wbr", "fieldset", "legend",
        };

        private static readonly string[] s_dropped =
        {
            "script", "style", "template", "iframe", "frame", "frameset", "object", "embed", "applet", "param", "noscript",
            "noembed", "noframes", "xmp", "plaintext", "title", "base", "basefont", "link", "meta", "head", "dialog",
            "portal", "fencedframe", "slot", "math", "svg", "canvas", "selectedcontent", "isindex", "keygen", "bgsound",
            "audio", "video", "source", "track", "input", "select", "option", "optgroup", "textarea", "datalist",
            "output", "progress", "meter", "image",
        };

        internal static readonly string[] FormControlElements = { "label", "input", "select", "option", "optgroup", "textarea", "button", "datalist", "output", "progress", "meter" };

        internal static readonly string[] MediaElements = { "audio", "video", "source", "track", "picture" };

        internal static readonly (string Element, string[] Attributes)[] FormControlAttributes =
        {
            ("input", new[] { "type", "name", "value", "checked", "disabled", "readonly", "placeholder", "size", "maxlength", "minlength", "min", "max", "step", "multiple", "list", "pattern", "required", "alt", "width", "height" }),
            ("select", new[] { "name", "multiple", "size", "disabled", "required" }),
            ("option", new[] { "value", "selected", "disabled", "label" }),
            ("optgroup", new[] { "label", "disabled" }),
            ("textarea", new[] { "name", "rows", "cols", "placeholder", "disabled", "readonly", "wrap", "maxlength", "minlength", "required" }),
            ("button", new[] { "type", "name", "value", "disabled" }),
            ("output", new[] { "for", "name" }),
            ("progress", new[] { "value", "max" }),
            ("meter", new[] { "value", "min", "max", "low", "high", "optimum" }),
        };

        internal static readonly (string Element, string[] Attributes)[] MediaAttributes =
        {
            ("audio", new[] { "src", "controls", "loop", "muted", "preload" }),
            ("video", new[] { "src", "controls", "loop", "muted", "preload", "poster", "width", "height", "playsinline" }),
            ("source", new[] { "src", "type", "media", "sizes" }),
            ("track", new[] { "src", "kind", "srclang", "label", "default" }),
        };

        private static readonly string[] s_svgElements =
        {
                "svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "tspan", "textPath",
                "defs", "symbol", "use", "title", "desc", "linearGradient", "radialGradient", "stop", "clipPath", "mask",
                "pattern", "marker", "image", "filter", "feBlend", "feColorMatrix", "feComponentTransfer", "feComposite",
                "feConvolveMatrix", "feDiffuseLighting", "feDisplacementMap", "feDistantLight", "feDropShadow", "feFlood",
                "feFuncA", "feFuncB", "feFuncG", "feFuncR", "feGaussianBlur", "feMerge", "feMergeNode", "feMorphology",
                "feOffset", "fePointLight", "feSpecularLighting", "feSpotLight", "feTile", "feTurbulence", "switch", "view",
            };

        private static readonly string[] s_svgAttributes =
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
            };

        private static readonly string[] s_mathElements =
        {
                "math", "mi", "mn", "mo", "ms", "mtext", "mspace", "mrow", "mfrac", "msqrt", "mroot", "mstyle", "merror",
                "mpadded", "mphantom", "msub", "msup", "msubsup", "munder", "mover", "munderover", "mmultiscripts",
                "mprescripts", "none", "mtable", "mtr", "mtd", "semantics", "annotation", "menclose", "mlabeledtr",
            };

        private static readonly string[] s_mathAttributes =
        {
                "display", "mathvariant", "mathsize", "mathcolor", "mathbackground", "dir", "displaystyle", "scriptlevel",
                "fence", "separator", "stretchy", "symmetric", "largeop", "movablelimits", "accent", "accentunder", "lspace",
                "rspace", "minsize", "maxsize", "linethickness", "width", "height", "depth", "voffset", "rowspan",
                "columnspan", "columnalign", "rowalign", "columnlines", "rowlines", "frame", "notation", "encoding",
                "id", "class", "style",
            };

        private static void AddTextAttributes(SanitizerPolicy p)
        {
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

    }
}
