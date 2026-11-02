using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.Tests.Sanitization
{
    /// <summary>
    /// Policies the tests build with the public switches, the way an application would. The library itself
    /// has no presets: a new <see cref="SanitizerPolicy"/> is the strict default.
    /// </summary>
    internal static class TestPolicies
    {
        /// <summary>The library default (strict): what an application gets with no configuration.</summary>
        public static SanitizerPolicy Strict() => new SanitizerPolicy();

        /// <summary>A document-viewer configuration: every capability switch on, default limits.</summary>
        public static SanitizerPolicy Viewer() => new SanitizerPolicy
        {
            Links = LinkHandling.Active,
            AllowRelativeLinks = true,
            AllowFormControls = true,
            AllowMedia = true,
            AllowSvg = true,
            AllowMathML = true,
            AllowSvgDataImages = true,
            AllowEmbeddedFonts = true,
            Limits = new MarkupLimits(),
        };

        /// <summary>Nothing but text: every allowlist emptied, no styles, no data or aria attributes.</summary>
        public static SanitizerPolicy TextOnly()
        {
            var p = new SanitizerPolicy
            {
                StyleSheets = StyleSheetHandling.Drop,
                AllowStyleAttribute = false,
                AllowDataAttributes = false,
                AllowAriaAttributes = false,
            };
            p.AllowedElements.Clear();
            p.GlobalAttributes.Clear();
            p.ElementAttributes.Clear();
            return p;
        }
    }
}
