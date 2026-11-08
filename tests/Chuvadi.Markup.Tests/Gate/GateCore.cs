using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Chuvadi.Markup;
using Chuvadi.Markup.Markdown;
using Chuvadi.Markup.Text;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markdown.Gate
{
    /// <summary>
    /// The named configurations and the canonical dumps the gate compares. The same code produced the frozen
    /// expected outputs, so a mismatch is always a difference in the library's public output.
    /// </summary>
    /// <summary>A configuration name the gate does not know (a gate bug, never library output).</summary>
    public sealed class GateConfigException : Exception
    {
        public GateConfigException(string message) : base(message) { }
    }

    public static class GateCore
    {
        // ---------- parse configurations ----------

        public static readonly string[] ParseConfigs =
        {
            "P-CM", "P-GFM", "P-GFM-NoTables", "P-GFM-NoTasks", "P-GFM-NoStrike", "P-GFM-NoAutolinks",
        };

        /// <summary>Limit configurations: the outcome (AST or exception) is compared.</summary>
        public static readonly string[] LimitConfigs =
        {
            "L-Depth", "L-Table", "L-Defs", "L-Nodes", "L-Work", "L-Input", "L-Strict",
        };

        public static MarkdownOptions Parse(string name)
        {
            switch (name)
            {
                case "P-CM": return MarkdownOptions.CommonMark();
                case "P-GFM": return MarkdownOptions.Gfm();
                case "P-GFM-NoTables": { var o = MarkdownOptions.Gfm(); o.Tables = false; return o; }
                case "P-GFM-NoTasks": { var o = MarkdownOptions.Gfm(); o.TaskLists = false; return o; }
                case "P-GFM-NoStrike": { var o = MarkdownOptions.Gfm(); o.Strikethrough = false; return o; }
                case "P-GFM-NoAutolinks": { var o = MarkdownOptions.Gfm(); o.Autolinks = false; return o; }
                case "L-Depth": { var o = MarkdownOptions.Gfm(); o.Limits.MaxNestingDepth = 6; return o; }
                case "L-Table": { var o = MarkdownOptions.Gfm(); o.Limits.MaxTableColumns = 3; o.Limits.MaxTableCells = 12; return o; }
                case "L-Defs": { var o = MarkdownOptions.Gfm(); o.Limits.MaxLinkReferenceDefinitions = 2; return o; }
                case "L-Nodes": { var o = MarkdownOptions.Gfm(); o.Limits.MaxNodes = 40; return o; }
                case "L-Work": { var o = MarkdownOptions.Gfm(); o.Limits.MaxParserWorkPerChar = 2; return o; }
                case "L-Input": { var o = MarkdownOptions.Gfm(); o.Limits.MaxInputChars = 200; return o; }
                case "L-Strict": { var o = MarkdownOptions.Gfm(); o.Limits = MarkupLimits.Strict(); return o; }
                default: throw new GateConfigException("unknown parse configuration " + name);
            }
        }

        // ---------- HTML configurations ----------

        public static readonly string[] HtmlConfigs =
        {
            "H-Spec", "H-GfmSpec", "H-Default", "H-Strip", "H-Alt1", "H-Alt2", "H-DataSourcepos", "H-Out",
        };

        public static MarkdownHtmlOptions Html(string name)
        {
            switch (name)
            {
                case "H-Spec": return new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough, TagFilter = false, UnsafeUrls = UnsafeUrlPolicy.Keep };
                case "H-GfmSpec": return new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough, TagFilter = true, UnsafeUrls = UnsafeUrlPolicy.Keep };
                case "H-Default": return new MarkdownHtmlOptions();
                case "H-Strip": return new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Strip };
                case "H-Alt1":
                    return new MarkdownHtmlOptions
                    {
                        RawHtml = RawHtmlMode.Passthrough, TagFilter = true, UnsafeUrls = UnsafeUrlPolicy.Remove,
                        SoftBreak = SoftBreakStyle.Space, HeadingIds = true, HeadingIdPrefix = "h-",
                        CodeLanguageClassPrefix = null, TaskLists = TaskListStyle.Glyph,
                        TableAlignment = TableAlignmentStyle.StyleAttribute, XhtmlVoidElements = false, SourcePositions = true,
                    };
                case "H-Alt2":
                    return new MarkdownHtmlOptions
                    {
                        RawHtml = RawHtmlMode.Escape, TagFilter = false, SoftBreak = SoftBreakStyle.LineBreak,
                        HeadingIds = true, CodeLanguageClassPrefix = "lang-", TaskLists = TaskListStyle.Text,
                    };
                case "H-DataSourcepos": return new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough, TagFilter = false, UnsafeUrls = UnsafeUrlPolicy.Keep, SourcePositions = true };
                case "H-Out": { var o = new MarkdownHtmlOptions(); o.Limits.MaxOutputChars = 300; return o; }
                default: throw new GateConfigException("unknown HTML configuration " + name);
            }
        }

        // ---------- writer configurations ----------

        public static readonly string[] WriterConfigs = { "W-Default", "W-Normalized", "W-Alt" };

        public static MarkdownWriterOptions Writer(string name)
        {
            switch (name)
            {
                case "W-Default": return new MarkdownWriterOptions();
                case "W-Normalized": return new MarkdownWriterOptions { PreserveSourceStyle = false, BackslashHardBreaks = false, SetextHeadings = true };
                case "W-Alt":
                    return new MarkdownWriterOptions
                    {
                        BulletChar = '*', EmphasisChar = '_', StrongChar = '_', OrderedDelimiter = ')', FenceChar = '~',
                        PreserveIndentedCode = true, ThematicBreak = "***", AlignTableColumns = false, PreserveReferenceLinks = true,
                        PreserveSourceStyle = false,
                    };
                default: throw new GateConfigException("unknown writer configuration " + name);
            }
        }

        // ---------- plain-text configurations ----------

        public static readonly string[] TextConfigs = { "T-Active", "T-Paragraphs" };

        public static TextToHtmlOptions Text(string name)
        {
            switch (name)
            {
                case "T-Active": return new TextToHtmlOptions { Links = LinkHandling.Active };
                case "T-Paragraphs": return new TextToHtmlOptions { Links = LinkHandling.Inert, Layout = TextLayout.Paragraphs };
                default: throw new GateConfigException("unknown text configuration " + name);
            }
        }

        // ---------- checks ----------

        /// <summary>
        /// The checks run for one input. Key = check id ("kind/config[/config]"), value = the output.
        /// Kinds: blocks, ast, html, writer, limit, text.
        /// </summary>
        public static IEnumerable<string> Checks(string set)
        {
            bool big = set == "pathological";
            foreach (var p in new[] { "P-CM", "P-GFM" })
            {
                yield return "blocks/" + p;
                yield return "ast/" + p;
            }
            yield return "html/P-CM/H-Spec";
            yield return "html/P-CM/H-DataSourcepos";
            yield return "html/P-GFM/H-GfmSpec";
            if (!big)
            {
                foreach (var p in ParseConfigs.Skip(2))
                {
                    yield return "ast/" + p;
                    yield return "html/" + p + "/H-GfmSpec";
                }
                foreach (var h in new[] { "H-Default", "H-Strip", "H-Alt1", "H-Alt2", "H-DataSourcepos", "H-Out" })
                    yield return "html/P-GFM/" + h;
                yield return "writer/P-CM/W-Default";
                foreach (var w in WriterConfigs) yield return "writer/P-GFM/" + w;
                foreach (var l in LimitConfigs) yield return "limit/" + l;
                foreach (var t in TextConfigs) yield return "text/" + t;
            }
            else
            {
                yield return "writer/P-GFM/W-Default";
                yield return "limit/L-Strict";
            }
        }

        /// <summary>
        /// How the checks parse. The gate always uses the library's MarkdownParser. (The program that froze the
        /// expected outputs replaced it with a wrapper that applies the approved MaxNodes rule, D-130 R5-01 B, to the
        /// previous library's results.)
        /// </summary>
        public static Func<string, MarkdownOptions, MarkdownDocument> ParseDocument { get; set; } = (s, o) => MarkdownParser.Parse(s, o);

        public static string Run(string check, string input)
        {
            try
            {
                string[] parts = check.Split('/');
                switch (parts[0])
                {
                    case "blocks": return DumpAst(ParseDocument(input, Parse(parts[1])), blocksOnly: true);
                    case "ast": return DumpAst(ParseDocument(input, Parse(parts[1])), blocksOnly: false);
                    case "html": return MarkdownHtmlRenderer.Render(ParseDocument(input, Parse(parts[1])), Html(parts[2]));
                    case "writer": return MarkdownWriter.Write(ParseDocument(input, Parse(parts[1])), Writer(parts[2]));
                    case "limit": return DumpAst(ParseDocument(input, Parse(parts[1])), blocksOnly: false);
                    case "text": return PlainText.ToHtml(input, Text(parts[1]));
                    default: throw new GateConfigException("unknown check " + check);
                }
            }
            catch (Exception ex) when (ex is not GateConfigException)
            {
                return DescribeException(ex);
            }
        }

        public static string DescribeException(Exception ex)
        {
            var sb = new StringBuilder("!EXCEPTION ").Append(ex.GetType().FullName);
            if (ex is MarkupLimitExceededException m)
                sb.Append(" limit=").Append(m.LimitName).Append(" max=").Append(m.Limit.ToString(CultureInfo.InvariantCulture))
                  .Append(" actual=").Append(m.Actual.ToString(CultureInfo.InvariantCulture));
            // Messages built by the runtime use Environment.NewLine; the frozen text uses "\n" on every OS.
            sb.Append(" message=").Append(Quote(ex.Message.Replace("\r\n", "\n")));
            return sb.ToString();
        }

        // ---------- contract probes (argument checks, cancellation, readers) ----------

        public static readonly string[] Probes =
        {
            "parse-null-string", "parse-null-reader", "parse-null-options-limits", "parse-invalid-limits",
            "parse-cancelled", "parse-reader-equals-string", "render-null-node", "render-null-writer",
            "render-writer-equals-string", "render-cancelled", "render-subtree", "convert-defaults",
            "parse-input-limit-exact", "parse-input-limit-over", "render-built-tree", "writer-built-tree",
            "text-autolinks", "work-limit-1", "render-output-limit-writer", "writer-output-limit-writer",
            "normalize-label",
        };

        public static string Probe(string name)
        {
            const string sample = "# Title\n\nSome *text* with [a link](http://example.com \"t\") and `code`.\n\n- [x] done\n- [ ] todo\n\n| a | b |\n|:-|-:|\n| 1 | 2 |\n\n> quote\n\n    indented\n\n```cs\nfenced\n```\n\nwww.example.org ~~gone~~ <b>raw</b>\n";
            try
            {
                switch (name)
                {
                    case "parse-null-string": MarkdownParser.Parse((string)null!); return "no exception";
                    case "parse-null-reader": MarkdownParser.Parse((TextReader)null!); return "no exception";
                    case "parse-null-options-limits": MarkdownParser.Parse("a", new MarkdownOptions { Limits = null! }); return "no exception";
                    case "parse-invalid-limits": { var o = new MarkdownOptions(); o.Limits.MaxNodes = 0; MarkdownParser.Parse("a", o); return "no exception"; }
                    case "parse-cancelled": { using var c = new System.Threading.CancellationTokenSource(); c.Cancel(); MarkdownParser.Parse(sample, null, c.Token); return "no exception"; }
                    case "parse-reader-equals-string":
                        return (DumpAst(MarkdownParser.Parse(new StringReader(sample)), false) == DumpAst(MarkdownParser.Parse(sample), false)).ToString();
                    case "render-null-node": MarkdownHtmlRenderer.Render(null!); return "no exception";
                    case "render-null-writer": MarkdownHtmlRenderer.Render(MarkdownParser.Parse("a"), (TextWriter)null!); return "no exception";
                    case "render-writer-equals-string":
                        {
                            var d = MarkdownParser.Parse(sample); var sw = new StringWriter();
                            MarkdownHtmlRenderer.Render(d, sw, null);
                            return (sw.ToString() == MarkdownHtmlRenderer.Render(d)).ToString() + "\n" + sw;
                        }
                    case "render-cancelled": { using var c = new System.Threading.CancellationTokenSource(); c.Cancel(); MarkdownHtmlRenderer.Render(MarkdownParser.Parse(sample), null, c.Token); return "no exception"; }
                    case "render-subtree":
                        {
                            var d = MarkdownParser.Parse(sample); var sb = new StringBuilder();
                            foreach (var n in d.Descendants()) sb.Append(n.Kind).Append(": ").Append(MarkdownHtmlRenderer.Render(n, Html("H-DataSourcepos"))).Append('\n');
                            return sb.ToString();
                        }
                    case "convert-defaults": return MarkdownConverter.ToHtml(sample);
                    case "parse-input-limit-exact": { var o = new MarkdownOptions(); o.Limits.MaxInputChars = 5; return DumpAst(MarkdownParser.Parse("abcde", o), false); }
                    case "parse-input-limit-over": { var o = new MarkdownOptions(); o.Limits.MaxInputChars = 5; return DumpAst(MarkdownParser.Parse("abcdef", o), false); }
                    case "render-built-tree":
                    case "writer-built-tree":
                        {
                            var doc = new MarkdownDocument();
                            var h = new Heading { Level = 2 }; h.AppendChild(new TextInline("Built <tree> & *stars*")); doc.AppendChild(h);
                            var p = new Paragraph();
                            var link = new Link { Destination = "javascript:alert(1)", Title = "a\"b" }; link.AppendChild(new TextInline("x"));
                            p.AppendChild(link); p.AppendChild(new SoftBreak()); p.AppendChild(new CodeSpan("a`b"));
                            p.AppendChild(new HardBreak()); p.AppendChild(new HtmlInline("<script>x</script>"));
                            var img = new Image { Destination = "data:image/png;base64,AA==" }; img.AppendChild(new TextInline("alt"));
                            p.AppendChild(img); doc.AppendChild(p);
                            doc.AppendChild(new CodeBlock { IsFenced = true, Info = "c# extra", Code = "x < y\n" });
                            doc.AppendChild(new HtmlBlock { Html = "<div>\n</div>\n", BlockType = 6 });
                            return name == "render-built-tree"
                                ? MarkdownHtmlRenderer.Render(doc) + "\n---\n" + MarkdownHtmlRenderer.Render(doc, Html("H-Alt1"))
                                : MarkdownWriter.Write(doc) + "\n---\n" + MarkdownWriter.Write(doc, Writer("W-Alt"));
                        }
                    case "text-autolinks":
                        return PlainText.ToHtml("see http://a.b/c?d=(e)). and www.x.org/y_(z)! or a@b.co, <http://q.r> https://e.f/g&amp; end.", Text("T-Active"));
                    case "render-output-limit-writer":
                    case "writer-output-limit-writer":
                        {
                            var d = MarkdownParser.Parse(sample);
                            var sb = new StringBuilder();
                            foreach (int limit in new[] { 1, 17, 50, 123 })
                            {
                                var sw = new StringWriter();
                                string outcome;
                                try
                                {
                                    if (name == "render-output-limit-writer")
                                        MarkdownHtmlRenderer.Render(d, sw, new MarkdownHtmlOptions { Limits = new MarkupLimits { MaxOutputChars = limit } });
                                    else
                                        MarkdownWriter.Write(d, sw, new MarkdownWriterOptions { Limits = new MarkupLimits { MaxOutputChars = limit } });
                                    outcome = "no exception";
                                }
                                catch (Exception ex) { outcome = DescribeException(ex); }
                                sb.Append(limit).Append(": ").Append(outcome).Append(" written=").Append(Quote(sw.ToString())).Append('\n');
                            }
                            return sb.ToString();
                        }
                    case "normalize-label":
                        {
                            var sb = new StringBuilder();
                            foreach (var l in new[] { "a b", " a ", "\ta\t", "\na\r\n", "a \t\r\n b", "a\u000Bb", "a\u000Cb", "a\u00A0b", "a\u2028b",
                                "\u000Ca\u000C", "\u000Ba\u000B", "\u00A0a\u00A0", " \u000C a \u000C ", "A\u000CB", "\u1E9E\u000C", "\u00DF", "\u0130", "" })
                                sb.Append(Quote(l)).Append(" -> ").Append(Quote(LinkReferenceDefinition.NormalizeLabel(l))).Append('\n');
                            return sb.ToString();
                        }
                    case "work-limit-1":
                        {
                            var o = MarkdownOptions.Gfm(); o.Limits.MaxParserWorkPerChar = 1;
                            return DumpAst(MarkdownParser.Parse(string.Concat(Enumerable.Repeat("*a_ ", 500_000)), o), false).Length.ToString(CultureInfo.InvariantCulture);
                        }
                    default: throw new GateConfigException("unknown probe " + name);
                }
            }
            catch (Exception ex) when (ex is not GateConfigException)
            {
                return DescribeException(ex);
            }
        }

        // ---------- AST dump ----------

        private static readonly HashSet<string> s_skip = new(StringComparer.Ordinal)
        {
            "Kind", "Span", "Parent", "Children", "CanHaveChildren", "IsBlock", "Source", "LinkDefinitions",
        };

        /// <summary>
        /// One line per node: indentation, kind, span, then every other public property in name order.
        /// The document line is followed by its link reference definitions in key order.
        /// With <paramref name="blocksOnly"/>, inline nodes are left out (block structure and spans only).
        /// </summary>
        public static string DumpAst(MarkdownDocument doc, bool blocksOnly)
        {
            var sb = new StringBuilder();
            DumpNode(sb, doc, 0, blocksOnly);
            foreach (var kv in doc.LinkDefinitions.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var d = kv.Value;
                sb.Append("  def key=").Append(Quote(kv.Key)).Append(" span=").Append(Span(d.Span))
                  .Append(" Label=").Append(Quote(d.Label)).Append(" Destination=").Append(Quote(d.Destination))
                  .Append(" Title=").Append(d.Title is null ? "null" : Quote(d.Title)).Append('\n');
            }
            return sb.ToString();
        }

        private static void DumpNode(StringBuilder sb, MarkdownNode n, int depth, bool blocksOnly)
        {
            // iterative walk: deep trees must not overflow the stack
            var stack = new Stack<(MarkdownNode node, int depth)>();
            stack.Push((n, depth));
            while (stack.Count > 0)
            {
                var (node, d) = stack.Pop();
                if (blocksOnly && !node.IsBlock) continue;
                sb.Append(' ', 2 * d).Append(node.Kind.ToString()).Append(" span=").Append(Span(node.Span));
                foreach (var p in Props(node.GetType()))
                    sb.Append(' ').Append(p.Name).Append('=').Append(Value(p.GetValue(node)));
                sb.Append('\n');
                var ch = node.Children;
                for (int i = ch.Count - 1; i >= 0; i--) stack.Push((ch[i], d + 1));
            }
        }

        private static readonly Dictionary<Type, PropertyInfo[]> s_props = new();

        private static PropertyInfo[] Props(Type t)
        {
            lock (s_props)
            {
                if (!s_props.TryGetValue(t, out var ps))
                {
                    ps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !s_skip.Contains(p.Name))
                        .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                    s_props[t] = ps;
                }
                return ps;
            }
        }

        private static string Value(object? v)
        {
            switch (v)
            {
                case null: return "null";
                case string s: return Quote(s);
                case char c: return Quote(c.ToString());
                case bool b: return b ? "true" : "false";
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case long l: return l.ToString(CultureInfo.InvariantCulture);
                case Enum e: return e.ToString();
                case SourceSpan sp: return Span(sp);
                case System.Collections.IEnumerable list:
                    return "[" + string.Join(",", list.Cast<object?>().Select(Value)) + "]";
                default: return Quote(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        private static string Span(SourceSpan s) => s.Start.ToString(CultureInfo.InvariantCulture) + "+" + s.Length.ToString(CultureInfo.InvariantCulture);

        /// <summary>JSON-style quoting; every control, non-ASCII-safe and lone surrogate character is escaped.</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == 0x7F || char.IsSurrogate(c) || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Outputs longer than this are stored and compared as a SHA-256 hash.</summary>
        public const int HashThreshold = 64 * 1024;

        public static string Frozen(string output)
        {
            if (output.Length <= HashThreshold) return output;
            return "#sha256 " + Sha(output) + " length=" + output.Length.ToString(CultureInfo.InvariantCulture);
        }

        public static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

        // ---------- public API listing ----------

        /// <summary>Every public type and member of the assembly, one per line, sorted.</summary>
        public static string ApiListing(Assembly asm)
        {
            var lines = new List<string>();
            foreach (var t in asm.GetExportedTypes())
            {
                string kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : t.IsAbstract && t.IsSealed ? "static class" : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";
                string bases = t.BaseType is null || t.BaseType == typeof(object) || t.BaseType == typeof(ValueType) || t.BaseType == typeof(Enum) ? "" : " : " + Name(t.BaseType);
                string ifaces = string.Join(", ", t.GetInterfaces().Select(Name).OrderBy(x => x, StringComparer.Ordinal));
                lines.Add(Name(t) + " | " + kind + bases + (ifaces.Length > 0 ? " | " + ifaces : ""));
                const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (var m in t.GetMembers(F))
                {
                    string? sig = m switch
                    {
                        ConstructorInfo c => ".ctor(" + Params(c.GetParameters()) + ")",
                        MethodInfo mi when !mi.IsSpecialName => (mi.IsStatic ? "static " : mi.IsAbstract ? "abstract " : mi.IsVirtual && mi.GetBaseDefinition() == mi ? "virtual " : mi.IsVirtual ? "override " : "") + Name(mi.ReturnType) + " " + mi.Name + "(" + Params(mi.GetParameters()) + ")",
                        PropertyInfo pi => "property " + Name(pi.PropertyType) + " " + pi.Name + " {" + (pi.GetMethod?.IsPublic == true ? " get;" : "") + (pi.SetMethod?.IsPublic == true ? (pi.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(x => x.Name == "IsExternalInit") ? " init;" : " set;") : "") + " }",
                        FieldInfo fi when t.IsEnum && fi.IsStatic => "value " + fi.Name + " = " + Convert.ToInt64(fi.GetRawConstantValue(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                        FieldInfo fi when !t.IsEnum => "field " + Name(fi.FieldType) + " " + fi.Name,
                        EventInfo ei => "event " + ei.Name,
                        _ => null,
                    };
                    if (sig is not null) lines.Add(Name(t) + " :: " + sig);
                }
            }
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines) + "\n";
        }

        private static string Params(ParameterInfo[] ps) => string.Join(", ", ps.Select(p => Name(p.ParameterType) + " " + p.Name + (p.HasDefaultValue ? " = " + (p.DefaultValue is null ? "null" : Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture)) : "")));

        private static string Name(Type t)
        {
            if (t.IsGenericType)
                return (t.Namespace + "." + t.Name.Split('`')[0]) + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">";
            return t.FullName ?? t.Name;
        }
    }

    /// <summary>
    /// Cancellation responsiveness (the contract: a cancelled token stops parsing and rendering promptly, on any
    /// input). Each shape is parsed (or rendered) with a token that is cancelled after each of
    /// <see cref="CancelAfterMs"/>; every run must end within <see cref="MaxSeconds"/> of the cancel, normally with
    /// <see cref="OperationCanceledException"/>. A run that finishes in that time without seeing the cancel also
    /// passes (the input was too fast on this machine to tell). The limit leaves room for slow machines: 1.0.0 ends
    /// within about 0.3 s of the cancel on every shape.
    /// </summary>
    public static class GateCancel
    {
        public static readonly int[] CancelAfterMs = { 20, 120, 300 };
        public const double MaxSeconds = 0.75;

        private static string Rep(string s, int n) => new StringBuilder(s.Length * n).Insert(0, s, n).ToString();

        /// <summary>Name, input, and whether the cancel is given to the renderer (the parse runs first, uncancelled).</summary>
        public static IEnumerable<(string Name, string Input, bool Render)> Shapes()
        {
            const string Section = "# h\n\npara *em* [l](u) `c`\n\n- a\n- b\n\n";
            yield return ("emphasis: 3M '*', 'a', 3M '*'", new string('*', 3_000_000) + "a" + new string('*', 3_000_000), false);
            yield return ("emphasis: '*a_ ' x 500k", Rep("*a_ ", 500_000), false);
            yield return ("brackets: '[' x 3M", new string('[', 3_000_000), false);
            yield return ("brackets: 'a]' x 1.5M", Rep("a]", 1_500_000), false);
            yield return ("code spans: '`a' x 3M", Rep("`a", 3_000_000), false);
            yield return ("raw HTML: '<a ' x 6M", Rep("<a ", 6_000_000), false);
            yield return ("autolinks: 'www.a ' x 1M", Rep("www.a ", 1_000_000), false);
            yield return ("lines: 'a\\n' x 1M", Rep("a\n", 1_000_000), false);
            yield return ("list items: '- a\\n' x 300k", Rep("- a\n", 300_000), false);
            yield return ("table: 200k rows", "a|b\n-|-\n" + Rep("c|d\n", 200_000), false);
            yield return ("document: 100k sections", Rep(Section, 100_000), false);
            yield return ("render: 100k sections", Rep(Section, 100_000), true);
        }

        /// <summary>Null when the shape passes at every cancel time, otherwise the reasons.</summary>
        public static string? Check(string input, bool render)
        {
            var o = MarkdownOptions.Gfm();
            MarkdownDocument? doc = render ? MarkdownParser.Parse(input, o) : null;
            var why = new List<string>();
            foreach (int ms in CancelAfterMs)
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(ms));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool cancelled = false;
                try
                {
                    if (doc is not null) MarkdownHtmlRenderer.Render(doc, null, cts.Token);
                    else MarkdownParser.Parse(input, o, cts.Token);
                }
                catch (OperationCanceledException) { cancelled = true; }
                double late = sw.Elapsed.TotalSeconds - ms / 1000.0;
                if (cancelled && late >= MaxSeconds)
                    why.Add(string.Format(CultureInfo.InvariantCulture, "cancel at {0} ms: seen {1:F3} s later (limit {2} s)", ms, late, MaxSeconds));
                else if (!cancelled && late >= MaxSeconds)
                    why.Add(string.Format(CultureInfo.InvariantCulture, "cancel at {0} ms: not seen, finished {1:F3} s later (limit {2} s)", ms, late, MaxSeconds));
            }
            return why.Count == 0 ? null : string.Join("; ", why);
        }
    }
}
