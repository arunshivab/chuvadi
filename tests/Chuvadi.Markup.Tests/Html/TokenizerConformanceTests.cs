using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Chuvadi.Markup.Html;

namespace Chuvadi.Markup.Tests.Html
{
    /// <summary>The html5lib tokenizer suite (every test, every initial state).</summary>
    public class TokenizerConformanceTests
    {
        public static IEnumerable<object[]> Files() =>
            Directory.GetFiles(SpecFile.ConformancePath("html5lib", "tokenizer"), "*.test")
                .Select(f => new object[] { Path.GetFileName(f) });

        [Theory]
        [MemberData(nameof(Files))]
        public void File(string name)
        {
            var failures = Run(name, checkErrors: false);
            Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(15)));
        }

        [Theory]
        [MemberData(nameof(Files))]
        public void FileErrors(string name)
        {
            var failures = Run(name, checkErrors: true);
            Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(15)));
        }

        internal static List<string> Run(string file, bool checkErrors)
        {
            var failures = new List<string>();
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(SpecFile.ConformancePath("html5lib", "tokenizer", file)));
            if (!doc.RootElement.TryGetProperty("tests", out var tests)) return failures;
            foreach (var test in tests.EnumerateArray())
            {
                bool dbl = test.TryGetProperty("doubleEscaped", out var de) && de.GetBoolean();
                string input = Unescape(test.GetProperty("input").GetString()!, dbl);
                var states = test.TryGetProperty("initialStates", out var st)
                    ? st.EnumerateArray().Select(s => s.GetString()!).ToList()
                    : new List<string> { "Data state" };
                string? lastStartTag = test.TryGetProperty("lastStartTag", out var lst) ? lst.GetString() : null;
                string expected = Normalize(test.GetProperty("output"), dbl);
                foreach (var state in states)
                {
                    var tk = new HtmlTokenizer(input);
                    tk.SetState(state switch
                    {
                        "PLAINTEXT state" => HtmlTokenizerState.Plaintext,
                        "RCDATA state" => HtmlTokenizerState.Rcdata,
                        "RAWTEXT state" => HtmlTokenizerState.Rawtext,
                        "Script data state" => HtmlTokenizerState.ScriptData,
                        "CDATA section state" => HtmlTokenizerState.CdataSection,
                        _ => HtmlTokenizerState.Data,
                    });
                    tk.LastStartTagName = lastStartTag;
                    string actual;
                    try
                    {
                        actual = Serialize(tk.Tokens().Where(t => t.Kind != HtmlTokenKind.EndOfFile));
                    }
                    catch (Exception ex)
                    {
                        actual = "EXCEPTION " + ex.GetType().Name + ": " + ex.Message;
                    }
                    string desc = test.GetProperty("description").GetString() + " [" + state + "]";
                    if (actual != expected)
                    {
                        failures.Add(desc + "\n  input: " + JsonSerializer.Serialize(input) + "\n  expected: " + expected + "\n  actual:   " + actual);
                        continue;
                    }
                    if (checkErrors)
                    {
                        var exp = test.TryGetProperty("errors", out var errs)
                            ? errs.EnumerateArray().Select(e => e.GetProperty("code").GetString()!).OrderBy(x => x, StringComparer.Ordinal).ToList()
                            : new List<string>();
                        var act = tk.Errors.Select(e => e.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
                        if (!exp.SequenceEqual(act))
                            failures.Add(desc + "\n  input: " + JsonSerializer.Serialize(input) + "\n  expected errors: " + string.Join(",", exp) + "\n  actual errors:   " + string.Join(",", act));
                    }
                }
            }
            return failures;
        }

        private static string Unescape(string s, bool dbl)
        {
            if (!dbl) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 5 < s.Length + 0 && s[i + 1] == 'u')
                {
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 2, 4), 16));
                    i += 5;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static string Normalize(JsonElement output, bool dbl)
        {
            var parts = new List<string>();
            var chars = new StringBuilder();
            void Flush()
            {
                if (chars.Length > 0) { parts.Add("C:" + chars); chars.Clear(); }
            }
            foreach (var tok in output.EnumerateArray())
            {
                string kind = tok[0].GetString()!;
                switch (kind)
                {
                    case "Character":
                        chars.Append(Unescape(tok[1].GetString()!, dbl));
                        break;
                    case "StartTag":
                        Flush();
                        var attrs = tok[2].EnumerateObject().Select(p => p.Name + "=" + Unescape(p.Value.GetString()!, dbl)).OrderBy(x => x, StringComparer.Ordinal);
                        bool sc = tok.GetArrayLength() > 3 && tok[3].GetBoolean();
                        parts.Add("S:" + Unescape(tok[1].GetString()!, dbl) + "[" + string.Join("|", attrs) + "]" + (sc ? "/" : ""));
                        break;
                    case "EndTag":
                        Flush();
                        parts.Add("E:" + Unescape(tok[1].GetString()!, dbl));
                        break;
                    case "Comment":
                        Flush();
                        parts.Add("M:" + Unescape(tok[1].GetString()!, dbl));
                        break;
                    case "DOCTYPE":
                        Flush();
                        parts.Add("D:" + Str(tok[1], dbl) + "|" + Str(tok[2], dbl) + "|" + Str(tok[3], dbl) + "|" + tok[4].GetBoolean());
                        break;
                }
            }
            Flush();
            return string.Join(" ", parts);
        }

        private static string Str(JsonElement e, bool dbl) => e.ValueKind == JsonValueKind.Null ? "<null>" : Unescape(e.GetString()!, dbl);

        private static string Serialize(IEnumerable<HtmlToken> tokens)
        {
            var parts = new List<string>();
            var chars = new StringBuilder();
            void Flush()
            {
                if (chars.Length > 0) { parts.Add("C:" + chars); chars.Clear(); }
            }
            foreach (var t in tokens)
            {
                switch (t.Kind)
                {
                    case HtmlTokenKind.Character:
                        chars.Append(t.Data);
                        break;
                    case HtmlTokenKind.StartTag:
                        Flush();
                        parts.Add("S:" + t.Name + "[" + string.Join("|", t.Attributes.Select(a => a.Name + "=" + a.Value).OrderBy(x => x, StringComparer.Ordinal)) + "]" + (t.SelfClosing ? "/" : ""));
                        break;
                    case HtmlTokenKind.EndTag:
                        Flush();
                        parts.Add("E:" + t.Name);
                        break;
                    case HtmlTokenKind.Comment:
                        Flush();
                        parts.Add("M:" + t.Data);
                        break;
                    case HtmlTokenKind.Doctype:
                        Flush();
                        parts.Add("D:" + (t.Name ?? "<null>") + "|" + (t.PublicId ?? "<null>") + "|" + (t.SystemId ?? "<null>") + "|" + !t.ForceQuirks);
                        break;
                }
            }
            Flush();
            return string.Join(" ", parts);
        }
    }
}
