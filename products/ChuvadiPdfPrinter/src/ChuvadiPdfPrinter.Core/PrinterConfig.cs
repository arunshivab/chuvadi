using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChuvadiPdfPrinter
{
    /// <summary>What to do when the output file name is already taken.</summary>
    public enum CollisionPolicy
    {
        /// <summary>Append " (2)", " (3)", ... until the name is free. Default.</summary>
        AppendNumber,
        /// <summary>Replace the existing file.</summary>
        Overwrite,
        /// <summary>Keep the existing file and fail the job.</summary>
        Fail
    }

    /// <summary>The kind of printer a finished PDF is also sent to.</summary>
    public enum ForwardKind
    {
        /// <summary>An installed Windows printer: the PDF is rendered by Chuvadi.Print.Pdf and printed through the GDI spooler.</summary>
        WindowsPrinter,
        /// <summary>An IPP printer that accepts PDF; the PDF is passed through unchanged.</summary>
        Ipp
    }

    /// <summary>A printer every captured PDF is also printed on ("print and also send to a physical printer").</summary>
    public sealed class ForwardTarget
    {
        public ForwardKind Kind { get; set; } = ForwardKind.WindowsPrinter;

        /// <summary>Windows queue name, or the IPP printer URI.</summary>
        public string Printer { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        /// <summary>Copies on the physical printer. Default 1.</summary>
        public int Copies { get; set; } = 1;

        /// <summary>Two-sided printing on the physical printer (Simplex, Vertical = long edge, Horizontal = short edge).</summary>
        public Chuvadi.Print.Duplex Duplex { get; set; } = Chuvadi.Print.Duplex.Simplex;

        /// <summary>Accept a self-signed certificate (ipps:// only).</summary>
        public bool AcceptAnyServerCertificate { get; set; }

        /// <summary>
        /// Windows printers: the sheet size. "Auto" (default) uses the paper that matches the PDF's first page
        /// (A4 when nothing matches); or a name: A0-A6, B4, B5, Letter, Legal, Tabloid, Ledger, Executive, Statement.
        /// </summary>
        public string Paper { get; set; } = "Auto";

        /// <summary>Windows printers: Color (default), Grayscale or Monochrome.</summary>
        public Chuvadi.Print.ColorMode Color { get; set; } = Chuvadi.Print.ColorMode.Color;

        /// <summary>Windows printers: how pages are scaled onto the sheet. Default ShrinkToFit (actual size unless too large).</summary>
        public Chuvadi.Print.ScaleMode Scale { get; set; } = Chuvadi.Print.ScaleMode.ShrinkToFit;

        /// <summary>Windows printers: highest resolution pages are rendered at, in dpi. Default 300.</summary>
        public double MaxRasterDpi { get; set; } = 300;

        private static readonly (string Name, Chuvadi.Print.PaperSize Size)[] Papers =
        {
            ("A0", Chuvadi.Print.PaperSize.A0), ("A1", Chuvadi.Print.PaperSize.A1), ("A2", Chuvadi.Print.PaperSize.A2),
            ("A3", Chuvadi.Print.PaperSize.A3), ("A4", Chuvadi.Print.PaperSize.A4), ("A5", Chuvadi.Print.PaperSize.A5),
            ("A6", Chuvadi.Print.PaperSize.A6), ("B4", Chuvadi.Print.PaperSize.B4), ("B5", Chuvadi.Print.PaperSize.B5),
            ("Letter", Chuvadi.Print.PaperSize.Letter), ("Legal", Chuvadi.Print.PaperSize.Legal),
            ("Tabloid", Chuvadi.Print.PaperSize.Tabloid), ("Ledger", Chuvadi.Print.PaperSize.Ledger),
            ("Executive", Chuvadi.Print.PaperSize.Executive), ("Statement", Chuvadi.Print.PaperSize.Statement)
        };

        /// <summary>True when <see cref="Paper"/> is "Auto" or a known name.</summary>
        public bool IsPaperValid() =>
            string.Equals(Paper, "Auto", StringComparison.OrdinalIgnoreCase) || Array.Exists(Papers, p => string.Equals(p.Name, Paper, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The sheet for this target. For "Auto", <paramref name="firstPage"/> supplies the document's first page
        /// size (either orientation; within 2 mm), falling back to A4.
        /// </summary>
        public Chuvadi.Print.PaperSize ResolvePaper(Func<Chuvadi.Print.PageDimensions?> firstPage)
        {
            if (firstPage is null) throw new ArgumentNullException(nameof(firstPage));
            foreach (var (name, size) in Papers)
                if (string.Equals(name, Paper, StringComparison.OrdinalIgnoreCase)) return size;
            if (!string.Equals(Paper, "Auto", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unknown paper '" + Paper + "'.");

            const double tolerance = 2 * 72 / 25.4;
            if (firstPage() is Chuvadi.Print.PageDimensions page)
            {
                double shortSide = Math.Min(page.WidthPoints, page.HeightPoints);
                double longSide = Math.Max(page.WidthPoints, page.HeightPoints);
                foreach (var (_, size) in Papers)
                {
                    double s = Math.Min(size.WidthPoints, size.HeightPoints);
                    double l = Math.Max(size.WidthPoints, size.HeightPoints);
                    if (Math.Abs(s - shortSide) <= tolerance && Math.Abs(l - longSide) <= tolerance) return size;
                }
            }
            return Chuvadi.Print.PaperSize.A4;
        }
    }

    /// <summary>
    /// Configuration of the Chuvadi PDF virtual printer, stored as JSON (default
    /// %ProgramData%\Chuvadi\PdfPrinter\config.json). Missing values take the defaults below.
    /// </summary>
    public sealed class PrinterConfig
    {
        public const string DefaultFileName = "config.json";

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Name of the fallback print queue (Windows 10 / any Windows). Default "Chuvadi PDF".</summary>
        public string QueueName { get; set; } = "Chuvadi PDF";

        /// <summary>Named pipe the queue's port writes to (port name \\.\pipe\&lt;PipeName&gt;). Default "ChuvadiPdfPrinter".</summary>
        public string PipeName { get; set; } = "ChuvadiPdfPrinter";

        /// <summary>
        /// Folder finished PDFs are saved to. Environment variables are expanded. Default
        /// "%PUBLIC%\Documents\Chuvadi PDF" (writable by the service and readable by every user).
        /// </summary>
        public string OutputFolder { get; set; } = @"%PUBLIC%\Documents\Chuvadi PDF";

        /// <summary>Save each user's PDFs in a subfolder named after the user. Default true.</summary>
        public bool PerUserSubfolder { get; set; } = true;

        /// <summary>
        /// File name pattern. Tokens: {title} {user} {machine} {job} {date} {date:format} (any .NET date format).
        /// Default "{date:yyyy-MM-dd HHmmss} {title}". ".pdf" is appended.
        /// </summary>
        public string FileNamePattern { get; set; } = "{date:yyyy-MM-dd HHmmss} {title}";

        public CollisionPolicy Collision { get; set; } = CollisionPolicy.AppendNumber;

        /// <summary>Largest job accepted, in bytes. Default 2 GiB.</summary>
        public long MaxJobBytes { get; set; } = 2L * 1024 * 1024 * 1024;

        /// <summary>Printers every captured PDF is also sent to.</summary>
        public List<ForwardTarget> ForwardTo { get; set; } = new();

        /// <summary>Audit trail: one tab-delimited line per job (and per forward). Default %ProgramData%\Chuvadi\PdfPrinter\audit.log.</summary>
        public string AuditLog { get; set; } = @"%ProgramData%\Chuvadi\PdfPrinter\audit.log";

        /// <summary>Diagnostic log. Default %ProgramData%\Chuvadi\PdfPrinter\agent.log.</summary>
        public string DiagnosticLog { get; set; } = @"%ProgramData%\Chuvadi\PdfPrinter\agent.log";

        /// <summary>
        /// Package family name of the Windows 11 Print Support App. When set, the agent collects the app's
        /// finished jobs from every user's %LOCALAPPDATA%\Packages\&lt;family&gt;\LocalState\outbox.
        /// </summary>
        public string? PsaPackageFamilyName { get; set; }

        /// <summary>Extra outbox folders to collect from (tests, custom setups).</summary>
        public List<string> ExtraOutboxFolders { get; set; } = new();

        /// <summary>How often outboxes are scanned. Default 2 seconds.</summary>
        public int OutboxScanMilliseconds { get; set; } = 2000;

        public static PrinterConfig Load(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) return new PrinterConfig();
            string text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return new PrinterConfig();
            try
            {
                return JsonSerializer.Deserialize<PrinterConfig>(text, Json) ?? new PrinterConfig();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Invalid configuration file '" + path + "': " + ex.Message, ex);
            }
        }

        public void Save(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, ToJson(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        /// <summary>The output folder for a user, with environment variables expanded.</summary>
        public string ResolveOutputFolder(string? userName)
        {
            string folder = Environment.ExpandEnvironmentVariables(OutputFolder);
            if (PerUserSubfolder && !string.IsNullOrWhiteSpace(userName))
                folder = Path.Combine(folder, OutputNamer.SanitizeFileName(StripDomain(userName)));
            return folder;
        }

        public string ResolveAuditLog() => Environment.ExpandEnvironmentVariables(AuditLog);
        public string ResolveDiagnosticLog() => Environment.ExpandEnvironmentVariables(DiagnosticLog);

        /// <summary>%ProgramData%\Chuvadi\PdfPrinter\config.json.</summary>
        public static string DefaultPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Chuvadi", "PdfPrinter", DefaultFileName);

        /// <summary>Checks values that would make the agent misbehave.</summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(QueueName)) problems.Add("QueueName is empty.");
            if (string.IsNullOrWhiteSpace(PipeName) || PipeName.IndexOfAny(new[] { '\\', '/' }) >= 0)
                problems.Add("PipeName must be a plain name without slashes.");
            if (string.IsNullOrWhiteSpace(OutputFolder)) problems.Add("OutputFolder is empty.");
            if (string.IsNullOrWhiteSpace(FileNamePattern)) problems.Add("FileNamePattern is empty.");
            if (MaxJobBytes <= 0) problems.Add("MaxJobBytes must be positive.");
            if (OutboxScanMilliseconds < 100) problems.Add("OutboxScanMilliseconds must be at least 100.");
            foreach (ForwardTarget f in ForwardTo)
            {
                if (string.IsNullOrWhiteSpace(f.Printer)) problems.Add("A ForwardTo entry has no Printer.");
                if (f.Copies < 1) problems.Add("ForwardTo '" + f.Printer + "' needs at least 1 copy.");
                if (!f.IsPaperValid()) problems.Add("ForwardTo '" + f.Printer + "' has an unknown Paper '" + f.Paper + "'.");
                if (!(f.MaxRasterDpi >= 72 && f.MaxRasterDpi <= 1200)) problems.Add("ForwardTo '" + f.Printer + "' MaxRasterDpi must be 72..1200.");
            }
            return problems;
        }

        /// <summary>"DOMAIN\user" -> "user".</summary>
        internal static string StripDomain(string userName)
        {
            int slash = userName.LastIndexOf('\\');
            return slash >= 0 ? userName.Substring(slash + 1) : userName;
        }
    }
}
