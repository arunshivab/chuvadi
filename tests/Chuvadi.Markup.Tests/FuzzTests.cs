using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Chuvadi.Markup.Css;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Markdown;
using Chuvadi.Markup.Sanitization;
using Chuvadi.Markup.Tests.Sanitization;
using Chuvadi.Markup.Text;

namespace Chuvadi.Markup.Tests
{
    /// <summary>
    /// Time-boxed random-input fuzzing of every entry point: random bytes, and random mixes of markup-significant
    /// fragments with random bytes. The only exception allowed is <see cref="MarkupLimitExceededException"/>;
    /// sanitizer output must also pass the safety invariants. The budget per test is
    /// CHUVADI_FUZZ_SECONDS (default 5); CHUVADI_FUZZ_RUN picks a different seed sequence for long campaigns; a
    /// failing seed is reported so it can be replayed with CHUVADI_FUZZ_SEED.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class FuzzTests
    {
        private static readonly string[] s_fragments =
        {
            "<", ">", "</", "<!--", "-->", "<!", "<?", "&", "&#", "&#x", ";", "\"", "'", "=", "/", " ", "\n", "\r", "\0", "\t",
            "<svg>", "<math>", "<template>", "<table>", "<tr>", "<td>", "<select>", "<option>", "<option selected>",
            "<selectedcontent>", "<button>", "<style>", "</style>", "<script>", "</script>", "<textarea>", "<title>",
            "<plaintext>", "<noscript>", "<iframe>", "<frameset>", "<body>", "<html>", "<head>", "<p>", "<b>", "</b>",
            "<a href=", "<img src=", "<foreignObject>", "<annotation-xml encoding=text/html>", "<mtext>", "<![CDATA[",
            "]]>", "url(", "@import", "{", "}", "*", "_", "`", "```", "[", "](", "#", "> ", "- ", "1. ", "|", "---", "\\",
            "&amp;", "&notin", "&#x110000;", "\uFFFF", "\uD800", "\u0085", "\u2028", "data:image/png;base64,", "javascript:",
            "style=", "expression(", "\uFEFF", "onerror=", "@font-face{font-family:", "@keyframes ", "@counter-style ", "font:", "animation:",
            "list-style:", " ~ ", " + ", ":root", "html ", "@page{", "@layer ", "@container ", "<meta charset=utf-16>", "<meta charset=x-user-defined>",
        };

        private static readonly string[] s_contexts = { "body", "table", "tr", "select", "template", "svg", "math", "textarea", "style", "title", "plaintext", "td", "html" };

        private static double Budget() =>
            double.TryParse(Environment.GetEnvironmentVariable("CHUVADI_FUZZ_SECONDS"), out double s) && s > 0 ? s : 5;

        private static IEnumerable<int> Seeds(int stream)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("CHUVADI_FUZZ_SEED"), out int fixedSeed))
            {
                yield return fixedSeed;
                yield break;
            }
            int run = int.TryParse(Environment.GetEnvironmentVariable("CHUVADI_FUZZ_RUN"), out int r) ? r : 0;
            var master = new Random(stream * 7919 + run * 104729);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < Budget()) yield return master.Next();
        }

        internal static byte[] Input(int seed)
        {
            var rnd = new Random(seed);
            int mode = rnd.Next(3);
            if (mode == 0)
            {
                var b = new byte[rnd.Next(0, 2048)];
                rnd.NextBytes(b);
                return b;
            }
            var ms = new MemoryStream();
            int n = rnd.Next(1, 300);
            for (int i = 0; i < n; i++)
            {
                if (rnd.Next(4) == 0)
                {
                    var b = new byte[rnd.Next(1, 8)];
                    rnd.NextBytes(b);
                    ms.Write(b, 0, b.Length);
                }
                else
                {
                    var t = Encoding.UTF8.GetBytes(s_fragments[rnd.Next(s_fragments.Length)]);
                    ms.Write(t, 0, t.Length);
                }
            }
            var bytes = ms.ToArray();
            if (mode == 2 && bytes.Length > 2) bytes[rnd.Next(bytes.Length)] ^= (byte)rnd.Next(1, 256);
            return bytes;
        }

        private static string Text(byte[] bytes, int seed) =>
            (seed & 1) == 0 ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);

        private static int Run(int stream, Action<int, byte[], string> body)
        {
            int count = 0;
            foreach (int seed in Seeds(stream))
            {
                byte[] bytes = Input(seed);
                string text = Text(bytes, seed);
                try
                {
                    body(seed, bytes, text);
                }
                catch (MarkupLimitExceededException)
                {
                }
                catch (Exception ex)
                {
                    Assert.Fail($"seed {seed} (replay with CHUVADI_FUZZ_SEED={seed}): {ex}");
                }
                count++;
            }
            return count;
        }

        [Fact]
        public void HtmlTokenizerAndParser()
        {
            int n = Run(1, (seed, bytes, text) =>
            {
                foreach (var _ in new HtmlTokenizer(text).Tokens()) { }
                var doc = HtmlParser.Parse(bytes);
                HtmlParser.Parse(text, new HtmlParserOptions { Scripting = (seed & 2) == 0, CollectErrors = true });
                HtmlParser.ParseFragment(text, s_contexts[(uint)seed % s_contexts.Length]);
                HtmlParser.Parse(HtmlSerializer.Serialize(doc));
            });
            Assert.True(n > 0);
        }

        [Fact]
        public void Sanitizer()
        {
            var anjal = SanitizerPolicy.Anjal();
            var reader = SanitizerPolicy.Reader();
            int n = Run(2, (seed, bytes, text) =>
            {
                var a = HtmlSanitizer.Sanitize(bytes, anjal);
                var problems = SafetyInvariants.Check(a, anjal);
                Assert.True(problems.Count == 0, $"seed {seed}: " + string.Join("; ", problems));
                var r = HtmlSanitizer.Sanitize(text, reader);
                problems = SafetyInvariants.Check(r, reader);
                Assert.True(problems.Count == 0, $"seed {seed}: " + string.Join("; ", problems));
            });
            Assert.True(n > 0);
        }

        [Fact]
        public void MarkdownCssAndText()
        {
            int n = Run(3, (seed, bytes, text) =>
            {
                var doc = MarkdownParser.Parse(text);
                MarkdownHtmlRenderer.Render(doc, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough });
                MarkdownWriter.Write(doc);
                PlainText.ToHtml(text, new TextToHtmlOptions { FormatFlowed = (seed & 2) == 0, Layout = (TextLayout)((uint)seed % 3) });
                CssParser.ParseStyleSheet(text).ToString();
                CssParser.ParseDeclarations(text);
            });
            Assert.True(n > 0);
        }
    }
}
