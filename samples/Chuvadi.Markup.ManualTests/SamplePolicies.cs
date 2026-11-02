using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.ManualTests
{
    /// <summary>
    /// Policies built the way an application builds its own: the library default for untrusted mail, and a
    /// document-viewer configuration with every capability switch turned on.
    /// </summary>
    public static class SamplePolicies
    {
        /// <summary>Untrusted mail: the library default (strict). Optionally a resolver for <c>cid:</c> parts.</summary>
        public static SanitizerPolicy Mail(IResourceResolver? resolver = null) => new SanitizerPolicy { ResourceResolver = resolver };

        /// <summary>Document viewer: active and relative links, inert form controls, media, SVG, MathML, SVG images, embedded fonts, default limits.</summary>
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
    }
}
