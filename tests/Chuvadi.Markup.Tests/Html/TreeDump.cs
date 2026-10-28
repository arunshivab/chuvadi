using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chuvadi.Markup.Html;

namespace Chuvadi.Markup.Tests.Html
{
    /// <summary>html5lib tree-construction test format: parsing the .dat files and dumping DOM trees.</summary>
    public static class TreeDump
    {
        public sealed record DatTest(string File, int Index, string Data, string? Fragment, bool? Scripting, string Expected)
        {
            public override string ToString() => File + "#" + Index;
        }

        public static List<DatTest> Load(string path)
        {
            var tests = new List<DatTest>();
            var lines = System.IO.File.ReadAllText(path, Encoding.UTF8).Split('\n');
            string file = System.IO.Path.GetFileName(path);
            int i = 0, index = 0;
            while (i < lines.Length)
            {
                if (lines[i] != "#data") { i++; continue; }
                i++;
                var data = new List<string>();
                while (i < lines.Length && lines[i] != "#errors") data.Add(lines[i++]);
                string? fragment = null;
                bool? scripting = null;
                while (i < lines.Length && lines[i] != "#document")
                {
                    if (lines[i] == "#document-fragment") { fragment = lines[i + 1]; i += 2; continue; }
                    if (lines[i] == "#script-off") scripting = false;
                    if (lines[i] == "#script-on") scripting = true;
                    i++;
                }
                i++;
                var doc = new List<string>();
                while (i < lines.Length)
                {
                    if (lines[i] == "#data" && doc.Count > 0 && doc[doc.Count - 1] == string.Empty) break;
                    doc.Add(lines[i++]);
                }
                while (doc.Count > 0 && doc[doc.Count - 1] == string.Empty) doc.RemoveAt(doc.Count - 1);
                index++;
                tests.Add(new DatTest(file, index, string.Join("\n", data), fragment, scripting, string.Join("\n", doc)));
            }
            return tests;
        }

        public static string Dump(HtmlNode root)
        {
            var sb = new StringBuilder();
            Dump(root, 0, sb);
            return sb.ToString().TrimEnd('\n');
        }

        private static void Dump(HtmlNode parent, int depth, StringBuilder sb)
        {
            for (var n = parent.FirstChild; n is not null; n = n.NextSibling)
            {
                string indent = "| " + new string(' ', depth * 2);
                switch (n)
                {
                    case HtmlDocumentType dt:
                        sb.Append(indent).Append("<!DOCTYPE ").Append(dt.Name);
                        if (dt.PublicId.Length > 0 || dt.SystemId.Length > 0)
                            sb.Append(" \"").Append(dt.PublicId).Append("\" \"").Append(dt.SystemId).Append('"');
                        sb.Append(">\n");
                        break;
                    case HtmlElement e:
                    {
                        string prefix = e.Namespace switch { HtmlNamespace.Svg => "svg ", HtmlNamespace.MathML => "math ", _ => "" };
                        sb.Append(indent).Append('<').Append(prefix).Append(e.LocalName).Append(">\n");
                        var attrs = e.Attributes.Select(a =>
                        {
                            string ap = a.Namespace switch
                            {
                                HtmlAttributeNamespace.XLink => "xlink ",
                                HtmlAttributeNamespace.Xml => "xml ",
                                HtmlAttributeNamespace.Xmlns => "xmlns ",
                                _ => "",
                            };
                            return (Name: ap + a.LocalName, a.Value);
                        }).OrderBy(a => a.Name, StringComparer.Ordinal);
                        foreach (var a in attrs)
                            sb.Append(indent).Append("  ").Append(a.Name).Append("=\"").Append(a.Value).Append("\"\n");
                        if (e.Content is not null)
                        {
                            sb.Append(indent).Append("  content\n");
                            Dump(e.Content, depth + 2, sb);
                        }
                        Dump(e, depth + 1, sb);
                        break;
                    }
                    case HtmlText t:
                        sb.Append(indent).Append('"').Append(t.Data).Append("\"\n");
                        break;
                    case HtmlComment c:
                        sb.Append(indent).Append("<!-- ").Append(c.Data).Append(" -->\n");
                        break;
                }
            }
        }

        public static string Run(DatTest test, bool scripting)
        {
            var options = new HtmlParserOptions { Scripting = scripting };
            if (test.Fragment is null)
                return Dump(HtmlParser.Parse(test.Data, options));
            string ctx = test.Fragment;
            var ns = HtmlNamespace.Html;
            if (ctx.StartsWith("svg ", StringComparison.Ordinal)) { ns = HtmlNamespace.Svg; ctx = ctx.Substring(4); }
            else if (ctx.StartsWith("math ", StringComparison.Ordinal)) { ns = HtmlNamespace.MathML; ctx = ctx.Substring(5); }
            return Dump(HtmlParser.ParseFragment(test.Data, ctx, ns, options));
        }
    }
}
