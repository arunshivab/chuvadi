using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Documents;
using Chuvadi.Layout.Sheets;
using Chuvadi.Layout.Pdf;

namespace Chuvadi.Layout.Viewer
{
    /// <summary>
    /// Opens a .docx / .xlsx / .txt / .md / .html with Chuvadi.Layout and writes each page as SVG (the desktop-viewer path) and/or PNG
    /// (the server-side preview path) through Chuvadi.Layout.Pdf, plus index.html (lazy page images) and viewer-report.txt.
    /// The reference for docs/Chuvadi.Layout/INTEGRATION.md.
    ///
    /// Usage: Chuvadi.Layout.Viewer &lt;file&gt; [--out &lt;folder&gt;] [--format svg|png|both] [--dpi 150]
    ///        [--pages 1-3,7] [--password &lt;pw&gt;] [--culture en-IN] [--system-fonts] [--untrusted]
    ///        [--timeout &lt;seconds&gt;] [--paper &lt;xlsx paper code&gt;]
    /// Exit codes: 0 done; 1 the document could not be read or laid out; 2 bad arguments; 3 cancelled or timed out;
    /// 4 a limit was exceeded.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Settings s;
            try
            {
                s = Settings.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            // --timeout starts in Run, after the file has been read into memory (D-44).
            using CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            try
            {
                return Run(s, Console.Out, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Cancelled.");
                return 3;
            }
            catch (LayoutLimitExceededException ex)
            {
                Console.Error.WriteLine($"Limit exceeded: {ex.LimitName} (limit {ex.Limit}, actual {ex.Actual}).");
                return 4;
            }
            catch (Exception ex) when (ex is LayoutException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        internal static int Run(Settings s, TextWriter log, CancellationToken ct)
        {
            LayoutOptions options = new LayoutOptions
            {
                Culture = s.Culture,
                AllowSystemFonts = s.SystemFonts,
                Password = s.Password,
                DocumentName = Path.GetFileName(s.File),
                Limits = s.Untrusted ? LayoutLimits.ForUntrustedInput() : new LayoutLimits(),
                Shaper = new PdfShaper(), // Indic and other complex scripts
            };
            options.FontSources.Add(BundledFontSource.Instance);
            Directory.CreateDirectory(s.Out);
            string stem = Path.GetFileNameWithoutExtension(s.File);

            // The source is read once into memory before any timing or timeout starts: on Windows, Microsoft Defender scans
            // an encrypted docx/xlsx on open by trying the default password, which can take seconds inside the OS before
            // any library code runs (D-44). Nothing else is read from or written to disk by the library.
            Stopwatch read = Stopwatch.StartNew();
            byte[] source = File.ReadAllBytes(s.File);
            log.WriteLine($"{s.File}: read {source.Length:N0} bytes in {read.Elapsed.TotalMilliseconds:0} ms (not counted below)");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (s.TimeoutSeconds > 0)
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(s.TimeoutSeconds));
            }
            ct = timeout.Token;
            Stopwatch total = Stopwatch.StartNew();
            using MemoryStream fs = new MemoryStream(source, writable: false);
            string ext = Path.GetExtension(s.File).ToLowerInvariant();
            using LaidOutDocument doc = ext switch
            {
                ".xlsx" when s.PaperCode is int paper => SheetsLayout.Open(fs, new SheetsImportOptions { DefaultPaperSizeCode = paper }, options, ct),
                ".md" or ".markdown" => Chuvadi.Layout.Markup.MarkdownLayout.Open(fs, null, options, null, ct),
                ".html" or ".htm" => Chuvadi.Layout.Markup.HtmlLayout.Open(fs, null, options, ct),
                _ => AutoDetectLayout.Open(fs, LayoutSourceFormat.Auto, options, ct),
            };
            double openMs = total.Elapsed.TotalMilliseconds;
            log.WriteLine($"{s.File}: {doc.Source.Format}, opened in {openMs:0} ms; page count so far {doc.PageCount}{(doc.IsPageCountFinal ? string.Empty : " (estimate)")}");

            LayoutPdfOptions pdf = new LayoutPdfOptions { Title = doc.Source.Title ?? stem };
            List<LayoutWarning> outputWarnings = new List<LayoutWarning>();
            pdf.Warning = outputWarnings.Add;
            List<string> pageFiles = new List<string>();
            StringBuilder report = new StringBuilder();
            int written = 0;
            for (int i = 0; ; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (!s.Wants(i + 1))
                {
                    if (doc.IsPageCountFinal && i >= doc.PageCount)
                    {
                        break;
                    }
                    if (s.LastWanted is int last && i + 1 > last)
                    {
                        break;
                    }
                    continue;
                }
                LaidOutPage page;
                try
                {
                    page = doc.GetPage(i, ct);
                }
                catch (ArgumentOutOfRangeException)
                {
                    break; // past the last page
                }
                Stopwatch sw = Stopwatch.StartNew();
                string name = $"{stem}-p{i + 1:000}";
                if (s.Svg)
                {
                    // A distinct id prefix per page keeps ids unique if a host inlines several pages into one document.
                    pdf.LayoutSvg.IdPrefix = "p" + (i + 1).ToString(CultureInfo.InvariantCulture) + "-";
                    File.WriteAllText(Path.Combine(s.Out, name + ".svg"), page.ToSvg(pdf, ct), new UTF8Encoding(false));
                    pageFiles.Add(name + ".svg");
                }
                if (s.Png)
                {
                    File.WriteAllBytes(Path.Combine(s.Out, name + ".png"), page.ToPng(s.Dpi, pdf, ct));
                    if (!s.Svg)
                    {
                        pageFiles.Add(name + ".png");
                    }
                }
                written++;
                string line = $"page {i + 1} ({page.PageLabel}{(page.SourceName is null ? string.Empty : ", " + page.SourceName)}): {page.WidthPt:0.#} x {page.HeightPt:0.#} pt, " +
                              $"{page.DisplayList.Count} ops, rendered in {sw.Elapsed.TotalMilliseconds:0} ms{(page.IsProvisional ? ", provisional" : string.Empty)}";
                log.WriteLine(line);
                report.AppendLine(line);
            }
            int pages = doc.ResolvePageCount(ct);
            File.WriteAllText(Path.Combine(s.Out, "index.html"), Index(stem, pageFiles), new UTF8Encoding(false));

            report.Insert(0, $"{s.File}\n{doc.Source.Format}, {pages} page(s), {written} written, total {total.Elapsed.TotalMilliseconds:0} ms (open {openMs:0} ms)\n\n");
            report.AppendLine();
            foreach (FontSubstitution f in doc.FontSubstitutions)
            {
                report.AppendLine($"font: {f.RequestedFamily} -> {f.UsedFace} ({f.Kind}, metric-compatible {f.MetricCompatible})");
            }
            foreach (LayoutWarning w in doc.Warnings)
            {
                report.AppendLine("warning: " + w);
            }
            foreach (LayoutWarning w in outputWarnings)
            {
                report.AppendLine("output warning: " + w);
            }
            File.WriteAllText(Path.Combine(s.Out, "viewer-report.txt"), report.ToString(), new UTF8Encoding(false));
            log.WriteLine($"{pages} page(s); {written} written to {Path.GetFullPath(s.Out)}; {doc.Warnings.Count} warning(s); {total.Elapsed.TotalMilliseconds:0} ms");
            return 0;
        }

        /// <summary>A page that shows the pages lazily, as a desktop app's web view does (one image per page, loaded when scrolled to).</summary>
        private static string Index(string title, List<string> files)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>");
            sb.Append("<style>body{margin:0;background:#525659;font-family:sans-serif}main{display:flex;flex-direction:column;align-items:center;gap:16px;padding:16px}");
            sb.Append("img{max-width:min(100%,900px);background:#fff;box-shadow:0 2px 8px rgba(0,0,0,.4)}</style></head><body><main>");
            foreach (string f in files)
            {
                sb.Append("<img loading=\"lazy\" alt=\"").Append(System.Net.WebUtility.HtmlEncode(f)).Append("\" src=\"").Append(Uri.EscapeDataString(f)).Append("\">");
            }
            sb.Append("</main></body></html>");
            return sb.ToString();
        }
    }

    internal sealed class Settings
    {
        public string File { get; private set; } = string.Empty;
        public string Out { get; private set; } = string.Empty;
        public bool Svg { get; private set; } = true;
        public bool Png { get; private set; } = true;
        public double Dpi { get; private set; } = 150;
        public CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;
        public bool SystemFonts { get; private set; }
        public bool Untrusted { get; private set; }
        public string? Password { get; private set; }
        public double TimeoutSeconds { get; private set; }
        public int? PaperCode { get; private set; }
        private List<(int From, int To)>? _pages;

        public int? LastWanted
        {
            get
            {
                if (_pages is null)
                {
                    return null;
                }
                int max = 0;
                foreach ((int _, int to) in _pages)
                {
                    max = Math.Max(max, to);
                }
                return max;
            }
        }

        public bool Wants(int page)
        {
            if (_pages is null)
            {
                return true;
            }
            foreach ((int from, int to) in _pages)
            {
                if (page >= from && page <= to)
                {
                    return true;
                }
            }
            return false;
        }

        public static Settings Parse(string[] args)
        {
            Settings s = new Settings();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--out": s.Out = Next(args, ref i); break;
                    case "--format":
                        string f = Next(args, ref i).ToLowerInvariant();
                        s.Svg = f is "svg" or "both";
                        s.Png = f is "png" or "both";
                        if (!s.Svg && !s.Png)
                        {
                            throw new ArgumentException("--format is svg, png or both.");
                        }
                        break;
                    case "--dpi": s.Dpi = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--pages": s._pages = Ranges(Next(args, ref i)); break;
                    case "--password": s.Password = Next(args, ref i); break;
                    case "--culture": s.Culture = new CultureInfo(Next(args, ref i)); break;
                    case "--system-fonts": s.SystemFonts = true; break;
                    case "--untrusted": s.Untrusted = true; break;
                    case "--timeout": s.TimeoutSeconds = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--paper": s.PaperCode = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    default:
                        if (a.StartsWith("--", StringComparison.Ordinal) || s.File.Length > 0)
                        {
                            throw new ArgumentException("Unknown argument: " + a);
                        }
                        s.File = a;
                        break;
                }
            }
            if (s.File.Length == 0)
            {
                throw new ArgumentException("Usage: Chuvadi.Layout.Viewer <file> [--out <folder>] [--format svg|png|both] [--dpi 150] [--pages 1-3,7] [--password <pw>] [--culture en-IN] [--system-fonts] [--untrusted] [--timeout <s>] [--paper <code>]");
            }
            if (s.Out.Length == 0)
            {
                s.Out = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(s.File)) ?? ".", Path.GetFileNameWithoutExtension(s.File) + "-pages");
            }
            return s;
        }

        private static List<(int, int)> Ranges(string spec)
        {
            List<(int, int)> r = new List<(int, int)>();
            foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] ends = part.Split('-');
                int from = int.Parse(ends[0], CultureInfo.InvariantCulture);
                int to = ends.Length > 1 ? int.Parse(ends[1], CultureInfo.InvariantCulture) : from;
                if (from < 1 || to < from)
                {
                    throw new ArgumentException("Bad page range: " + part);
                }
                r.Add((from, to));
            }
            return r;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException(args[i] + " needs a value.");
            }
            return args[++i];
        }
    }
}
