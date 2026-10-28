using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chuvadi.Markup.Tests
{
    /// <summary>One example from a CommonMark-style spec file.</summary>
    public sealed record SpecExample(int Number, string Section, string Tag, string Markdown, string Html)
    {
        public override string ToString() => Number + " (" + Section + ")";
    }

    /// <summary>Reads CommonMark / GFM spec files (examples fenced by 32 backticks).</summary>
    public static class SpecFile
    {
        private const string Fence = "````````````````````````````````";

        public static string ConformancePath(params string[] parts)
        {
            var all = new List<string> { System.AppContext.BaseDirectory, "Conformance" };
            all.AddRange(parts);
            return Path.Combine(all.ToArray());
        }

        public static List<SpecExample> Load(string path)
        {
            var result = new List<SpecExample>();
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            string section = string.Empty;
            int number = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith(Fence + " example", System.StringComparison.Ordinal))
                {
                    string tag = line.Substring(Fence.Length + " example".Length).Trim();
                    var md = new StringBuilder();
                    var html = new StringBuilder();
                    i++;
                    while (i < lines.Length && lines[i] != ".") { md.Append(lines[i]).Append('\n'); i++; }
                    i++;
                    while (i < lines.Length && !lines[i].StartsWith(Fence, System.StringComparison.Ordinal)) { html.Append(lines[i]).Append('\n'); i++; }
                    number++;
                    result.Add(new SpecExample(number, section, tag, md.ToString().Replace('→', '\t'), html.ToString().Replace('→', '\t')));
                }
                else if (line.StartsWith("#", System.StringComparison.Ordinal))
                {
                    section = line.TrimStart('#').Trim();
                }
            }
            return result;
        }
    }
}
