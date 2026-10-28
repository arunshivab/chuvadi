using System.Linq;
using Chuvadi.Markup.Css;

namespace Chuvadi.Markup.Tests.Css
{
    public class CssTests
    {
        [Fact]
        public void TokenizesEscapesUrlsAndNumbers()
        {
            var t = new CssTokenizer("\\61 bc url( a\\29 b ) 1.5em -2 +.5% #id #1x 'str' \"bad\n");
            var tokens = t.TokenizeAll().Where(x => x.Type != CssTokenType.Whitespace).ToList();
            Assert.Equal(CssTokenType.Ident, tokens[0].Type);
            Assert.Equal("abc", tokens[0].Value);
            Assert.Equal(CssTokenType.Url, tokens[1].Type);
            Assert.Equal("a)b", tokens[1].Value);
            Assert.Equal(CssTokenType.Dimension, tokens[2].Type);
            Assert.Equal("em", tokens[2].Unit);
            Assert.Equal(1.5, tokens[2].Number);
            Assert.Equal(-2, tokens[3].Number);
            Assert.True(tokens[3].IsInteger);
            Assert.Equal(CssTokenType.Percentage, tokens[4].Type);
            Assert.True(tokens[5].IsIdHash);
            Assert.False(tokens[6].IsIdHash);
            Assert.Equal("str", tokens[7].Value);
            Assert.Equal(CssTokenType.BadString, tokens[8].Type);
        }

        [Fact]
        public void ParsesDeclarationsWithImportant()
        {
            var decls = CssParser.ParseDeclarations("COLOR: Red !IMPORTANT; margin:0 auto; --Custom: { a b }; bad; ;width :1px");
            Assert.Equal(new[] { "color", "margin", "--Custom", "width" }, decls.Select(d => d.Name));
            Assert.True(decls[0].Important);
            Assert.Equal("Red", decls[0].ValueText);
            Assert.Equal("0 auto", decls[1].ValueText);
        }

        [Fact]
        public void ParsesStyleSheetWithAtRulesAndNesting()
        {
            var sheet = CssParser.ParseStyleSheet("@import 'x.css'; @media (min-width: 1px) { p { color: red } } .a { color: blue; & .b { color: green } }");
            Assert.Equal(3, sheet.Rules.Count);
            var import = Assert.IsType<CssAtRule>(sheet.Rules[0]);
            Assert.False(import.HasBlock);
            var media = Assert.IsType<CssAtRule>(sheet.Rules[1]);
            Assert.Equal("media", media.Name);
            Assert.Equal("(min-width: 1px)", media.PreludeText);
            Assert.Equal("p", media.Rules.Single().PreludeText);
            var a = Assert.IsType<CssStyleRule>(sheet.Rules[2]);
            Assert.Equal(".a", a.SelectorText);
            Assert.Equal("color", a.Declarations.Single().Name);
            Assert.Equal("& .b", a.Rules.Single().PreludeText);
        }

        [Fact]
        public void SerializationIsStableAndSafeInStyleElements()
        {
            const string css = "p::after{content:'</style><script>'} a{background:url(x.png)} .x{width:calc(1px+2em)} b{margin:-1px}";
            string once = CssParser.ParseStyleSheet(css).ToString();
            string twice = CssParser.ParseStyleSheet(once).ToString();
            Assert.Equal(once, twice);
            Assert.DoesNotContain("<", once);
            Assert.Contains("url(\"x.png\")", once);
        }

        [Fact]
        public void AdjacentTokensDoNotMerge()
        {
            var tokens = new CssComponentValue[] { CssToken.Ident("a"), CssToken.Ident("b") };
            Assert.Equal("a/**/b", CssSerializer.Serialize(tokens));
            Assert.Equal("\\31 x", CssSerializer.Ident("1x"));
        }

        [Fact]
        public void DeepNestingIsBounded()
        {
            string deep = new string('(', 100_000) + new string(')', 100_000);
            var decls = CssParser.ParseDeclarations("a: " + deep + "; b: 1");
            Assert.Equal("b", decls.Last().Name);
        }
    }
}
