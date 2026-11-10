using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Chuvadi.Markdown.Gate
{
    /// <summary>One example of a CommonMark-style spec file (examples fenced by 32 backticks).</summary>
    public sealed record SpecExample(int Number, string Section, string Tag, string Markdown, string Html, int Line);

    public static class SpecFile
    {
        private const string Fence = "````````````````````````````````";

        public static List<SpecExample> Load(string path)
        {
            var result = new List<SpecExample>();
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            string section = string.Empty;
            int number = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith(Fence + " example", StringComparison.Ordinal))
                {
                    int start = i + 1;
                    string tag = line.Substring(Fence.Length + " example".Length).Trim();
                    var md = new StringBuilder();
                    var html = new StringBuilder();
                    i++;
                    while (i < lines.Length && lines[i] != ".") { md.Append(lines[i]).Append('\n'); i++; }
                    i++;
                    while (i < lines.Length && !lines[i].StartsWith(Fence, StringComparison.Ordinal)) { html.Append(lines[i]).Append('\n'); i++; }
                    number++;
                    result.Add(new SpecExample(number, section, tag, md.ToString().Replace('\u2192', '\t'), html.ToString().Replace('\u2192', '\t'), start));
                }
                else if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    section = line.TrimStart('#').Trim();
                }
            }
            return result;
        }

        /// <summary>
        /// Every heading of a spec file (text without the leading #), for the trace check. Example blocks (from a
        /// line starting with the 32-backtick fence and " example" to the next line starting with the 32-backtick
        /// fence) are skipped whole, so the Markdown inside them is never read as headings. Outside examples, a
        /// code fence opened by three or more backticks is closed only by a backtick fence at least as long, with
        /// nothing after it; headings inside it are skipped too. Only backtick fences are tracked: every code fence
        /// in the spec files' own text uses backticks, and extensions.txt has a paragraph line made of tildes
        /// (in its strikethrough tests) that must not be read as a fence.
        /// </summary>
        public static HashSet<string> Headings(string path)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            bool inExample = false;
            char fenceChar = '\0';
            int fenceLength = 0;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (inExample)
                {
                    if (line.StartsWith(Fence, StringComparison.Ordinal)) inExample = false;
                    continue;
                }
                if (fenceLength == 0 && line.StartsWith(Fence + " example", StringComparison.Ordinal))
                {
                    inExample = true;
                    continue;
                }
                int run = FenceRun(line, out char c);
                if (fenceLength > 0)
                {
                    if (run >= fenceLength && c == fenceChar && line.Substring(run).Trim().Length == 0) fenceLength = 0;
                    continue;
                }
                if (run >= 3)
                {
                    fenceChar = c;
                    fenceLength = run;
                    continue;
                }
                if (IsAtxHeading(line)) set.Add(line.TrimStart('#').Trim());
            }
            return set;
        }

        // Length of the run of backticks that starts the line (0 if none).
        private static int FenceRun(string line, out char c)
        {
            c = line.Length > 0 ? line[0] : '\0';
            if (c != '`') return 0;
            int n = 0;
            while (n < line.Length && line[n] == c) n++;
            return n;
        }

        // "#" to "######" followed by a space or the end of the line.
        private static bool IsAtxHeading(string line)
        {
            int n = 0;
            while (n < line.Length && line[n] == '#') n++;
            return n >= 1 && n <= 6 && (n == line.Length || line[n] == ' ');
        }
    }

    /// <summary>One frozen case: an input and the expected output of every check run on it.</summary>
    public sealed class GateCase
    {
        public string Id { get; set; } = "";
        public string Set { get; set; } = "";
        public Dictionary<string, string> Meta { get; set; } = new();
        public string Input { get; set; } = "";
        public Dictionary<string, string> Expected { get; set; } = new();
    }

    public static class CaseFile
    {
        private static readonly JsonSerializerOptions s_json = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static void Write(string path, IEnumerable<GateCase> cases)
        {
            using var fs = File.Create(path);
            using var gz = new GZipStream(fs, CompressionLevel.SmallestSize);
            using var w = new StreamWriter(gz, new UTF8Encoding(false));
            w.NewLine = "\n";
            foreach (var c in cases) w.WriteLine(JsonSerializer.Serialize(c, s_json));
        }

        public static IEnumerable<GateCase> Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var r = new StreamReader(gz, new UTF8Encoding(false));
            string? line;
            while ((line = r.ReadLine()) is not null)
                if (line.Length > 0) yield return JsonSerializer.Deserialize<GateCase>(line, s_json)!;
        }
    }
}
