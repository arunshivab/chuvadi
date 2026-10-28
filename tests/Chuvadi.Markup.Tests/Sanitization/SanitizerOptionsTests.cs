using System;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.Tests.Sanitization
{
    public class SanitizerOptionsTests
    {
        private const string Png = "data:image/png;base64,iVBORw0KGgo=";

        [Fact]
        public void AnjalBlocksRemoteImagesAndReportsThem()
        {
            var r = HtmlSanitizer.Sanitize("<p><img src='https://t.example/pixel.gif' alt=x><img src='" + Png + "'></p>");
            Assert.DoesNotContain("t.example", r.Html);
            Assert.Contains(Png, r.Html);
            var b = Assert.Single(r.BlockedResources);
            Assert.Equal("https://t.example/pixel.gif", b.Url);
            Assert.Equal(ResourceKind.Image, b.Kind);
            Assert.Equal("img", b.Element);
            Assert.True(r.IsStable);
        }

        [Fact]
        public void ResolverMapsCidImages()
        {
            var p = SanitizerPolicy.Anjal();
            p.ResourceResolver = new DelegateResourceResolver(req => req.Url == "cid:logo@x" ? Png : null);
            var r = HtmlSanitizer.Sanitize("<img src='cid:logo@x'><img src='cid:other'>", p);
            Assert.Contains("src=\"" + Png + "\"", r.Html);
            Assert.Single(r.BlockedResources, b => b.Url == "cid:other");
        }

        [Fact]
        public void ResolverResultIsTrustedButNeverScriptCapable()
        {
            var p = SanitizerPolicy.Anjal();
            p.ResourceResolver = new DelegateResourceResolver(req => req.Url switch
            {
                "cid:a" => "/mail/42/part/2",
                "cid:b" => " JavaScript:alert(1)",
                "cid:c" => "data:text/html,<script>alert(1)</script>",
                _ => null,
            });
            var r = HtmlSanitizer.Sanitize("<img src='cid:a'><img src='cid:b'><img src='cid:c'>", p);
            Assert.Contains("src=\"/mail/42/part/2\"", r.Html);
            Assert.DoesNotContain("alert", r.Html, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, r.BlockedResources.Count);
        }

        [Fact]
        public void LinkModes()
        {
            const string html = "<a href='https://x.example/?a=1&b=2'>go</a>";
            var keep = HtmlSanitizer.Sanitize(html, SanitizerPolicy.Reader());
            Assert.Contains("href=\"https://x.example/?a=1&amp;b=2\"", keep.Html);
            Assert.Contains("rel=\"noopener noreferrer\"", keep.Html);

            var inert = SanitizerPolicy.Reader();
            inert.Links = LinkHandling.Inert;
            var ri = HtmlSanitizer.Sanitize(html, inert);
            Assert.DoesNotContain(" href=", ri.Html);
            Assert.Contains("data-chuvadi-href=\"https://x.example/?a=1&amp;b=2\"", ri.Html);
            Assert.Contains("title=\"https://x.example/?a=1&amp;b=2\"", ri.Html);

            var rw = SanitizerPolicy.Reader();
            rw.Links = LinkHandling.Rewrite;
            rw.LinkRewriteTemplate = "/go?u={url}";
            var rr = HtmlSanitizer.Sanitize(html, rw);
            Assert.Contains("href=\"/go?u=https%3A%2F%2Fx.example%2F%3Fa%3D1%26b%3D2\"", rr.Html);
        }

        [Fact]
        public void DangerousLinkSchemesRemoved()
        {
            var r = HtmlSanitizer.Sanitize("<a href=' jav&#x09;ascript:alert(1)'>x</a><a href='mailto:a@b'>m</a>", SanitizerPolicy.Reader());
            Assert.DoesNotContain("javascript", r.Html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("mailto:a@b", r.Html);
        }

        [Fact]
        public void StyleSheetsAreScopedAndBodyStyleTransferred()
        {
            var r = HtmlSanitizer.Sanitize("<html><head><title>T</title><style>body{color:red} p.a{margin:0} #x{color:blue}</style></head><body bgcolor=#eee style='font-size:12px'><p class=a id=x>t</p></body></html>");
            Assert.Equal("T", r.Title);
            Assert.StartsWith("<div class=\"chuvadi-content\"", r.Html);
            Assert.Contains(".chuvadi-content p.a", r.Html);
            Assert.Contains(".chuvadi-content #user-content-x", r.Html);
            Assert.Contains("id=\"user-content-x\"", r.Html);
            Assert.Contains("font-size: 12px", r.Html);
            Assert.DoesNotContain("<title", r.Html);
        }

        [Fact]
        public void StyleSheetsCanBeDropped()
        {
            var p = SanitizerPolicy.Anjal();
            p.StyleSheets = StyleSheetHandling.Drop;
            var r = HtmlSanitizer.Sanitize("<style>p{color:red}</style><p>x</p>", p);
            Assert.DoesNotContain("<style", r.Html);
        }

        [Fact]
        public void CssUrlsAndExpressionsBlocked()
        {
            var r = HtmlSanitizer.Sanitize("<p style=\"background:url(https://t.example/a.png); width: expression(alert(1)); color: red\">x</p>");
            Assert.DoesNotContain("t.example", r.Html);
            Assert.DoesNotContain("expression", r.Html);
            Assert.Contains("color: red", r.Html);
            Assert.Contains(r.BlockedResources, b => b.Url == "https://t.example/a.png");
        }

        [Fact]
        public void BlockedBackgroundImageKeepsColour()
        {
            var r = HtmlSanitizer.Sanitize("<style>td { background: url(https://t.example/bg.png) #036; color: #fff }</style><div style=\"background-image: url('https://t.example/a.png'), url(" + Png + ")\">x");
            Assert.Contains("background: none #036", r.Html);
            Assert.Contains("background-image: none, url(&quot;" + Png + "&quot;)", r.Html);
            Assert.Equal(2, r.BlockedResources.Count);

            var p = SanitizerPolicy.Anjal();
            p.ReplaceBlockedCssImagesWithNone = false;
            var r2 = HtmlSanitizer.Sanitize("<p style=\"background: url(https://t.example/bg.png) #036; color: red\">x", p);
            Assert.DoesNotContain("background", r2.Html);
            Assert.Contains("color: red", r2.Html);
        }

        [Theory]
        [InlineData("{ color: red }")]
        [InlineData(" , { color: red }")]
        [InlineData("p,,b { color: red }")]
        [InlineData("p, { color: red }")]
        [InlineData("p { & , { color: red } }")]
        public void RulesWithEmptySelectorsAreDropped(string css)
        {
            var r = HtmlSanitizer.Sanitize("<style>" + css + " i { color: blue }</style><p>x");
            Assert.DoesNotContain("red", r.Html);
            Assert.Contains(".chuvadi-content i { color: blue; }", r.Html);
            Assert.Empty(SafetyInvariants.Check(r, SanitizerPolicy.Anjal()));
        }

        [Fact]
        public void FuzzSeed1930389171StyleWithoutSelector()
        {
            var bytes = FuzzTests.Input(1930389171);
            var policy = SanitizerPolicy.Anjal();
            Assert.Empty(SafetyInvariants.Check(HtmlSanitizer.Sanitize(bytes, policy), policy));
        }

        [Theory]
        [InlineData("\u0099javascript:alert(1)")]
        [InlineData("\u0085javascript:alert(1)")]
        [InlineData("\u200Bjavascript:alert(1)")]
        [InlineData("\uFEFFjavascript:alert(1)")]
        [InlineData("\u00ADjava\u00ADscript:alert(1)")]
        [InlineData("\u2028javascript:alert(1)")]
        [InlineData("\u00A0vbscript:x")]
        [InlineData("/\u001E/script.example/")]
        [InlineData("\\\u200B\\script.example/")]
        [InlineData("/\u00AD\\script.example/")]
        public void RelativeLinksHidingASchemeAreRemoved(string href)
        {
            var r = HtmlSanitizer.Sanitize("<a href=\"" + href + "\">x</a><a href=\"\u0099docs/a.html\">y</a>", SanitizerPolicy.Reader());
            Assert.DoesNotContain("script", r.Html, StringComparison.Ordinal);
            Assert.Contains("docs/a.html", r.Html, StringComparison.Ordinal);
        }

        // ---------------- security review 2026-10-28 (F1, F3, F4) ----------------

        private const string FontData = "data:font/ttf;base64,AAEAAAALAIAAAwAwT1MvMg8SBfcAAAC8AAAAYGNtYXA=";

        [Fact]
        public void F1_FontFaceFamiliesAreNamespacedWithTheirReferences()
        {
            var p = SanitizerPolicy.Reader();
            string html = "<style>@font-face{font-family:HostFont;src:url(" + FontData + ")} p{font-family:HostFont, serif} .x{font:italic bold 12px/2 HostFont, sans-serif} .y{font-family:Arial}</style>"
                + "<p style=\"font-family:'hostfont'\">x</p><svg><text font-family=\"HostFont, serif\">t</text></svg>";
            var r = HtmlSanitizer.Sanitize(html, p);
            Assert.Contains("@font-face { font-family: \"user-content-HostFont\";", r.Html, StringComparison.Ordinal);
            Assert.Contains("p { font-family: \"user-content-HostFont\", serif; }", r.Html, StringComparison.Ordinal);
            Assert.Contains("font: italic bold 12px/2 \"user-content-HostFont\", sans-serif;", r.Html, StringComparison.Ordinal);
            Assert.Contains("font-family: Arial;", r.Html, StringComparison.Ordinal); // undeclared (system) families untouched
            Assert.Contains("style=\"font-family: &quot;user-content-hostfont&quot;\"", r.Html, StringComparison.Ordinal);
            Assert.Contains("font-family=\"&quot;user-content-HostFont&quot;, serif\"", r.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("font-family: HostFont", r.Html, StringComparison.Ordinal);
            Assert.True(r.IsStable);
            Assert.Empty(SafetyInvariants.Check(r, p));
        }

        [Fact]
        public void F1_AnjalDropsFontFaceUnlessOptedIn()
        {
            string html = "<style>@font-face{font-family:HostFont;src:url(" + FontData + ")} p{font-family:HostFont}</style><p>x";
            var strict = HtmlSanitizer.Sanitize(html, SanitizerPolicy.Anjal());
            Assert.DoesNotContain("@font-face", strict.Html, StringComparison.Ordinal);
            var optIn = SanitizerPolicy.Anjal();
            optIn.AllowedAtRules.Add("font-face");
            var r = HtmlSanitizer.Sanitize(html, optIn);
            Assert.Contains("@font-face { font-family: \"user-content-HostFont\";", r.Html, StringComparison.Ordinal);
            Assert.Contains("p { font-family: \"user-content-HostFont\"; }", r.Html, StringComparison.Ordinal);
            Assert.Empty(SafetyInvariants.Check(r, optIn));
        }

        [Fact]
        public void F1_NamesFallBackToScopeClassWhenIdPrefixIsEmpty()
        {
            var p = SanitizerPolicy.Reader();
            p.IdPrefix = null;
            var r = HtmlSanitizer.Sanitize("<style>@font-face{font-family:'Host Font';src:url(" + FontData + ")} p{font-family:Host  Font}</style><p>x", p);
            Assert.Contains("font-family: \"chuvadi-content-Host Font\";", r.Html, StringComparison.Ordinal);
            Assert.Contains("p { font-family: \"chuvadi-content-Host Font\"; }", r.Html, StringComparison.Ordinal);
        }

        [Fact]
        public void F3_KeyframesAndCounterStylesAreNamespaced()
        {
            foreach (var p in new[] { SanitizerPolicy.Anjal(), SanitizerPolicy.Reader() })
            {
                var r = HtmlSanitizer.Sanitize("<style>@keyframes hostspin{from{opacity:1}to{opacity:0}} p{animation:hostspin 1s infinite;animation-name:hostspin, other}"
                    + "@counter-style lower-roman{system:cyclic;symbols:'!';fallback:lower-roman} ol{list-style:lower-roman inside} li::before{content:counter(c, lower-roman)}</style>"
                    + "<p style='animation-name:hostspin'>x</p><ol style='list-style-type:lower-roman'><li>a</ol>", p);
                Assert.Contains("@keyframes user-content-hostspin {", r.Html, StringComparison.Ordinal);
                Assert.Contains("animation: user-content-hostspin 1s infinite; animation-name: user-content-hostspin, other;", r.Html, StringComparison.Ordinal);
                Assert.Contains("style=\"animation-name: user-content-hostspin\"", r.Html, StringComparison.Ordinal);
                Assert.Contains("@counter-style user-content-lower-roman { system: cyclic; symbols: \"!\"; fallback: user-content-lower-roman; }", r.Html, StringComparison.Ordinal);
                Assert.Contains("list-style: user-content-lower-roman inside;", r.Html, StringComparison.Ordinal);
                Assert.Contains("content: counter(c, user-content-lower-roman);", r.Html, StringComparison.Ordinal);
                Assert.Contains("style=\"list-style-type: user-content-lower-roman\"", r.Html, StringComparison.Ordinal);
                Assert.True(r.IsStable);
                Assert.Empty(SafetyInvariants.Check(r, p));
            }
        }

        [Fact]
        public void F3_PageAndLayerAreDroppedAndContainerContentsScoped()
        {
            foreach (var p in new[] { SanitizerPolicy.Anjal(), SanitizerPolicy.Reader() })
            {
                var r = HtmlSanitizer.Sanitize("<style>@page{margin:0} @layer base{p{color:red}} @layer a, b; @container x (min-width: 1px){p{color:blue}}</style><p>x", p);
                Assert.DoesNotContain("@page", r.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("@layer", r.Html, StringComparison.Ordinal);
                Assert.DoesNotContain("red", r.Html, StringComparison.Ordinal);
                Assert.Contains("@container x (min-width: 1px) { .chuvadi-content p { color: blue; } }", r.Html, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void F3_AnchorPositioningAndViewTransitionNamesAreRemoved()
        {
            var r = HtmlSanitizer.Sanitize("<p style='anchor-name:--a;position-anchor:--host;top:anchor(--host bottom);view-transition-name:host;color:red'>x", SanitizerPolicy.Reader());
            Assert.DoesNotContain("anchor", r.Html, StringComparison.Ordinal);
            Assert.DoesNotContain("view-transition", r.Html, StringComparison.Ordinal);
            Assert.Contains("color: red", r.Html, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("~ * { color: red }")]
        [InlineData("+ p { color: red }")]
        [InlineData("> p { color: red }")]
        [InlineData(".chuvadi-content ~ x { color: red }")]
        [InlineData(".chuvadi-content.a + x { color: red }")]
        [InlineData("html ~ x { color: red }")]
        [InlineData("body.a + y { color: red }")]
        [InlineData(":root ~ z { color: red }")]
        [InlineData("i, ~ b { color: red }")]
        [InlineData(":root { & ~ z { color: red } }")]
        [InlineData("html { ~ w { color: red } }")]
        [InlineData("@media screen { ~ * { color: red } }")]
        public void F4_SelectorsReachingOutsideTheWrapperAreDropped(string css)
        {
            foreach (var p in new[] { SanitizerPolicy.Anjal(), SanitizerPolicy.Reader() })
            {
                var r = HtmlSanitizer.Sanitize("<style>" + css + " p ~ q { color: blue } .chuvadi-content > i { color: green }</style><p>m", p);
                Assert.DoesNotContain("red", r.Html, StringComparison.Ordinal);
                Assert.Contains(".chuvadi-content p ~ q { color: blue; }", r.Html, StringComparison.Ordinal);
                Assert.Contains(".chuvadi-content > i { color: green; }", r.Html, StringComparison.Ordinal);
                Assert.Empty(SafetyInvariants.Check(r, p));
            }
        }

        [Fact]
        public void FormsAreNeutralised()
        {
            var r = HtmlSanitizer.Sanitize("<form action='https://x.example'><input name=p value=secret><button>Go</button><textarea>t</textarea></form>");
            Assert.DoesNotContain("<form", r.Html);
            Assert.DoesNotContain("<input", r.Html);
            Assert.DoesNotContain("secret", r.Html);
        }

        [Fact]
        public void EmptyPolicyKeepsOnlyText()
        {
            var r = HtmlSanitizer.Sanitize("<p onclick=x>a<b>b</b><script>evil()</script></p>", SanitizerPolicy.Empty());
            Assert.DoesNotContain("<", r.Html.Replace("<div class=\"chuvadi-content\">", "").Replace("</div>", ""));
            Assert.Contains("ab", r.Html);
            Assert.DoesNotContain("evil", r.Html);
        }

        [Fact]
        public void CustomAllowList()
        {
            var p = SanitizerPolicy.Empty();
            p.AllowedElements.Add("b");
            p.AllowAttributes("b", "title");
            p.ScopeClass = null;
            var r = HtmlSanitizer.Sanitize("<b title=t class=c>x</b><i>y</i>", p);
            Assert.Equal("<b title=\"t\">x</b>y", r.Html); // no ScopeClass: no wrapper element
        }

        [Fact]
        public void ActionsAreReported()
        {
            var r = HtmlSanitizer.Sanitize("<script>x</script><p onclick=y>t</p>");
            Assert.True(r.ActionCount >= 2);
            Assert.Contains(r.Actions, a => a.Element == "script");
            Assert.Contains(r.Actions, a => a.Attribute == "onclick");
        }

        [Fact]
        public void ByteInputAndIdempotence()
        {
            var bytes = Encoding.ASCII.GetBytes("<meta charset=windows-1252><p>caf").Concat(new byte[] { 0xE9 }).ToArray();
            var r = HtmlSanitizer.Sanitize(bytes);
            Assert.Contains("café", r.Html);
            var again = HtmlSanitizer.Sanitize(r.Html);
            Assert.Contains(r.Html, again.Html); // output survives re-sanitizing unchanged (inside a new wrapper)
        }

        [Fact]
        public void LimitsAndCancellation()
        {
            var p = SanitizerPolicy.Anjal();
            p.Limits.MaxInputChars = 10;
            Assert.Throws<MarkupLimitExceededException>(() => HtmlSanitizer.Sanitize(new string('x', 100), p));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => HtmlSanitizer.Sanitize("<p>x", null, cts.Token));
        }

        [Fact]
        public void PolicyCloneIsDeep()
        {
            var a = SanitizerPolicy.Reader();
            var b = a.Clone();
            b.AllowedElements.Remove("p");
            b.Limits.MaxNodes = 1;
            Assert.Contains("p", a.AllowedElements);
            Assert.NotEqual(1, a.Limits.MaxNodes);
        }
    }
}
