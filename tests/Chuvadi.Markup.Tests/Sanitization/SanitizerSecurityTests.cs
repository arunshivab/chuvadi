using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.Tests.Sanitization
{
    public class SanitizerSecurityTests
    {
        public static IEnumerable<object[]> Vectors() => XssVectors.All.Select((v, i) => new object[] { i, v });

        private static void AssertSafe(int index, string input, SanitizerPolicy policy)
        {
            var result = HtmlSanitizer.Sanitize(input, policy);
            var problems = SafetyInvariants.Check(result, policy);
            Assert.True(problems.Count == 0, "vector " + index + ": " + input + "\noutput: " + result.Html + "\nproblems:\n" + string.Join("\n", problems));
            Assert.True(result.IsStable, "unstable output for vector " + index + ": " + input + "\n" + result.Html);
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public void Strict(int index, string input) => AssertSafe(index, input, TestPolicies.Strict());

        [Theory]
        [MemberData(nameof(Vectors))]
        public void Viewer(int index, string input) => AssertSafe(index, input, TestPolicies.Viewer());

        [Theory]
        [MemberData(nameof(Vectors))]
        public void ViewerUnscoped(int index, string input)
        {
            var p = TestPolicies.Viewer();
            p.StyleSheets = StyleSheetHandling.Sanitize;
            p.ScopeClass = null;
            var result = HtmlSanitizer.Sanitize(input, p);
            var problems = SafetyInvariants.Check(result, p).Where(x => !x.StartsWith("style element not first child", StringComparison.Ordinal)).ToList();
            Assert.True(problems.Count == 0, "vector " + index + ": " + input + "\noutput: " + result.Html + "\nproblems:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void InvariantCheckerFlagsRawAttacks()
        {
            // control: the checker must see the attacks in unsanitized input, or the other tests prove nothing
            int flagged = XssVectors.All.Count(v => SafetyInvariants.Check(v, TestPolicies.Strict()).Count > 0);
            Assert.True(flagged >= XssVectors.All.Length * 8 / 10, flagged + " of " + XssVectors.All.Length + " flagged");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Fuzz(int seed)
        {
            var rnd = new Random(seed);
            string[] pieces = XssVectors.All.Concat(new[]
            {
                "<p>", "</p>", "<div>", "</div>", "<table>", "<tr>", "<td>", "</table>", "<b>", "</b>", "<i>", "<a href=x>", "</a>",
                "<svg>", "</svg>", "<math>", "<mtext>", "<style>", "</style>", "<!--", "-->", "\"", "'", ">", "<", "&", "&lt;",
                "<select>", "<option>", "<textarea>", "</textarea>", "<noscript>", "</noscript>", "<template>", "</template>",
                "<li>", "<ul>", "<pre>", "\n", " ", "x", "<img src=data:image/png;base64,AA>", "<form>", "<button>", "<frameset>",
            }).ToArray();
            for (int i = 0; i < 300; i++)
            {
                int n = rnd.Next(1, 12);
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < n; k++)
                {
                    string piece = pieces[rnd.Next(pieces.Length)];
                    if (rnd.Next(4) == 0 && piece.Length > 2) piece = piece.Substring(0, rnd.Next(1, piece.Length));
                    sb.Append(piece);
                }
                string input = sb.ToString();
                foreach (var policy in new[] { TestPolicies.Strict(), TestPolicies.Viewer() })
                {
                    var result = HtmlSanitizer.Sanitize(input, policy);
                    var problems = SafetyInvariants.Check(result, policy);
                    Assert.True(problems.Count == 0, "input: " + input + "\noutput: " + result.Html + "\nproblems:\n" + string.Join("\n", problems));
                }
            }
        }
    }
}
