using System;
using System.Linq;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.Tests.Sanitization
{
    /// <summary>The policy has no presets: a new policy is the strict default and each capability is a switch.</summary>
    public class PolicySwitchTests
    {
        private const string Everything =
            "<a href='https://x.example/a'>abs</a><a href='docs/b.html'>rel</a>"
            + "<label for=i>L</label><input id=i name=n><select><option>1</option></select><textarea>t</textarea><button>B</button>"
            + "<video src='data:video/mp4;base64,AAAA' controls></video><audio src='data:audio/mpeg;base64,AAAA'></audio><picture><img src='data:image/png;base64,AA'></picture>"
            + "<svg width=10><rect width=5 height=5 fill='#f00'/></svg><math><mi>x</mi></math>"
            + "<img src='data:image/svg+xml;base64,PHN2Zy8+' alt=svgimg>"
            + "<style>@font-face{font-family:F;src:url(data:font/woff2;base64,AAAA)} p{font-family:F}</style><p>text</p>";

        [Fact]
        public void NewPolicyIsTheStrictDefault()
        {
            var p = new SanitizerPolicy();
            Assert.Equal(LinkHandling.Inert, p.Links);
            Assert.False(p.AllowRelativeLinks);
            Assert.False(p.AllowFormControls);
            Assert.False(p.AllowMedia);
            Assert.False(p.AllowSvg);
            Assert.False(p.AllowMathML);
            Assert.False(p.AllowSvgDataImages);
            Assert.False(p.AllowEmbeddedFonts);
            Assert.True(p.AllowStyleAttribute);
            Assert.Equal(StyleSheetHandling.SanitizeScoped, p.StyleSheets);
            Assert.Null(p.ResourceResolver);
            Assert.Equal(MarkupLimits.Strict().MaxInputChars, p.Limits.MaxInputChars);
            Assert.Equal(MarkupLimits.Strict().MaxNestingDepth, p.Limits.MaxNestingDepth);
            Assert.DoesNotContain("font-face", p.AllowedAtRules);
            Assert.DoesNotContain("page", p.AllowedAtRules);
            Assert.DoesNotContain("layer", p.AllowedAtRules);

            string h = HtmlSanitizer.Sanitize(Everything, p).Html;
            Assert.DoesNotContain(" href=", h, StringComparison.Ordinal);
            Assert.Contains("data-chuvadi-href=\"https://x.example/a\"", h, StringComparison.Ordinal);
            Assert.DoesNotContain("docs/b.html", h, StringComparison.Ordinal);
            foreach (var tag in new[] { "<label", "<input", "<select", "<option", "<textarea", "<button", "<video", "<audio", "<picture", "<svg", "<math", "@font-face", "svg+xml" })
                Assert.DoesNotContain(tag, h, StringComparison.Ordinal);
            Assert.Contains("LB", h, StringComparison.Ordinal); // label and button unwrapped to their text
            Assert.Contains("data:image/png", h, StringComparison.Ordinal);
            Assert.Empty(SafetyInvariants.Check(h, p));
        }

        [Fact]
        public void NullPolicyIsTheStrictDefault()
        {
            Assert.Equal(HtmlSanitizer.Sanitize(Everything, new SanitizerPolicy()).Html, HtmlSanitizer.Sanitize(Everything).Html);
        }

        [Fact]
        public void LinksActiveAndRelative()
        {
            var p = new SanitizerPolicy { Links = LinkHandling.Active };
            string h = HtmlSanitizer.Sanitize(Everything, p).Html;
            Assert.Contains("href=\"https://x.example/a\"", h, StringComparison.Ordinal);
            Assert.DoesNotContain("docs/b.html", h, StringComparison.Ordinal);
            p.AllowRelativeLinks = true;
            Assert.Contains("href=\"docs/b.html\"", HtmlSanitizer.Sanitize(Everything, p).Html, StringComparison.Ordinal);
        }

        [Fact]
        public void FormControlsSwitch()
        {
            string h = HtmlSanitizer.Sanitize(Everything, new SanitizerPolicy { AllowFormControls = true }).Html;
            foreach (var tag in new[] { "<label for=\"user-content-i\">", "<input id=\"user-content-i\" name=\"n\">", "<select>", "<option>", "<textarea>", "<button>" })
                Assert.Contains(tag, h, StringComparison.Ordinal);
            Assert.DoesNotContain("<form", HtmlSanitizer.Sanitize("<form action=x><input></form>", new SanitizerPolicy { AllowFormControls = true }).Html, StringComparison.Ordinal);
        }

        [Fact]
        public void MediaSwitch()
        {
            string h = HtmlSanitizer.Sanitize(Everything, new SanitizerPolicy { AllowMedia = true }).Html;
            Assert.Contains("<video src=\"data:video/mp4;base64,AAAA\" controls=\"\">", h, StringComparison.Ordinal);
            Assert.Contains("<audio src=\"data:audio/mpeg;base64,AAAA\">", h, StringComparison.Ordinal);
            Assert.Contains("<picture>", h, StringComparison.Ordinal);
            var remote = HtmlSanitizer.Sanitize("<video src='https://x.example/v.mp4'></video>", new SanitizerPolicy { AllowMedia = true });
            Assert.DoesNotContain("x.example", remote.Html, StringComparison.Ordinal);
            Assert.Single(remote.BlockedResources);
        }

        [Fact]
        public void SvgMathAndSvgImageSwitches()
        {
            string svg = HtmlSanitizer.Sanitize(Everything, new SanitizerPolicy { AllowSvg = true }).Html;
            Assert.Contains("<svg width=\"10\"><rect width=\"5\" height=\"5\" fill=\"#f00\"></rect></svg>", svg, StringComparison.Ordinal);
            Assert.DoesNotContain("<math", svg, StringComparison.Ordinal);
            string math = HtmlSanitizer.Sanitize(Everything, new SanitizerPolicy { AllowMathML = true }).Html;
            Assert.Contains("<math><mi>x</mi></math>", math, StringComparison.Ordinal);
            string img = HtmlSanitizer.Sanitize(Everything, new SanitizerPolicy { AllowSvgDataImages = true }).Html;
            Assert.Contains("src=\"data:image/svg+xml;base64,PHN2Zy8+\"", img, StringComparison.Ordinal);
        }

        [Fact]
        public void EmbeddedFontsSwitchKeepsNamespacing()
        {
            var p = new SanitizerPolicy { AllowEmbeddedFonts = true };
            var r = HtmlSanitizer.Sanitize(Everything, p);
            Assert.Contains("@font-face { font-family: \"user-content-F\";", r.Html, StringComparison.Ordinal);
            Assert.Contains("p { font-family: \"user-content-F\"; }", r.Html, StringComparison.Ordinal);
            Assert.Empty(SafetyInvariants.Check(r, p));
        }

        [Fact]
        public void SwitchesNeverModifyThePolicyObject()
        {
            var p = TestPolicies.Viewer();
            int allowed = p.AllowedElements.Count, dropped = p.DroppedElements.Count, attrs = p.ElementAttributes.Count, images = p.AllowedDataImageTypes.Count, atRules = p.AllowedAtRules.Count;
            HtmlSanitizer.Sanitize(Everything, p);
            Assert.Equal(allowed, p.AllowedElements.Count);
            Assert.Equal(dropped, p.DroppedElements.Count);
            Assert.Equal(attrs, p.ElementAttributes.Count);
            Assert.Equal(images, p.AllowedDataImageTypes.Count);
            Assert.Equal(atRules, p.AllowedAtRules.Count);
        }

        [Fact]
        public void ExplicitSetsStillWork()
        {
            var p = new SanitizerPolicy();
            p.AllowedElements.Add("label");
            Assert.Contains("<label for=", HtmlSanitizer.Sanitize("<label for=a>x</label>", p).Html, StringComparison.Ordinal);
            p.AllowedAtRules.Add("font-face");
            Assert.Contains("@font-face", HtmlSanitizer.Sanitize("<style>@font-face{font-family:F;src:url(data:font/woff2;base64,AAAA)}</style>x", p).Html, StringComparison.Ordinal);
        }

        [Fact]
        public void PublicApiHasNoPresets()
        {
            var statics = typeof(SanitizerPolicy).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(SanitizerPolicy)).Select(m => m.Name).ToList();
            Assert.Empty(statics);
        }
    }
}
