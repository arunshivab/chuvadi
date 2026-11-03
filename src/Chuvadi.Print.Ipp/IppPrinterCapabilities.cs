using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Print.Ipp
{
    /// <summary>How a printer expects the back side of a duplex sheet in PWG Raster (pwg-raster-document-sheet-back).</summary>
    public enum PwgSheetBack
    {
        Normal,
        Flipped,
        Rotated,
        ManualTumble
    }

    /// <summary>PWG 5101.1 self-describing media names ("iso_a4_210x297mm", "na_letter_8.5x11in").</summary>
    public static class PwgMediaNames
    {
        private static readonly (string Name, double WidthMm, double HeightMm)[] Known =
        {
            ("iso_a0_841x1189mm", 841, 1189), ("iso_a1_594x841mm", 594, 841), ("iso_a2_420x594mm", 420, 594),
            ("iso_a3_297x420mm", 297, 420), ("iso_a4_210x297mm", 210, 297), ("iso_a5_148x210mm", 148, 210),
            ("iso_a6_105x148mm", 105, 148), ("iso_b4_250x353mm", 250, 353), ("iso_b5_176x250mm", 176, 250),
            ("na_letter_8.5x11in", 215.9, 279.4), ("na_legal_8.5x14in", 215.9, 355.6), ("na_ledger_11x17in", 279.4, 431.8),
            ("na_executive_7.25x10.5in", 184.15, 266.7), ("na_invoice_5.5x8.5in", 139.7, 215.9)
        };

        /// <summary>Media tolerance for matching sizes: 1 mm.</summary>
        private const double ToleranceMm = 1.0;

        /// <summary>
        /// The media name for <paramref name="paper"/>: a name from <paramref name="supported"/> with the same size
        /// when there is one, else the standard PWG name, else a custom name "custom_WxHmm_WxHmm".
        /// </summary>
        public static string NameFor(PaperSize paper, IEnumerable<string>? supported = null)
        {
            double w = Math.Min(paper.WidthMillimetres, paper.HeightMillimetres);
            double h = Math.Max(paper.WidthMillimetres, paper.HeightMillimetres);
            if (supported is not null)
            {
                foreach (string name in supported)
                {
                    if (TryParseSize(name, out double sw, out double sh) && Same(w, h, sw, sh)) return name;
                }
            }
            foreach (var known in Known)
                if (Same(w, h, known.WidthMm, known.HeightMm)) return known.Name;
            string size = Mm(w) + "x" + Mm(h) + "mm";
            return "custom_" + size + "_" + size;
        }

        /// <summary>Reads the dimensions (portrait, millimetres) from a self-describing media name.</summary>
        public static bool TryParseSize(string name, out double widthMm, out double heightMm)
        {
            widthMm = heightMm = 0;
            if (string.IsNullOrEmpty(name) || name.StartsWith("custom_min", StringComparison.Ordinal)
                || name.StartsWith("custom_max", StringComparison.Ordinal))
                return false;
            int underscore = name.LastIndexOf('_');
            if (underscore < 0) return false;
            string dims = name.Substring(underscore + 1);
            double factor;
            if (dims.EndsWith("mm", StringComparison.Ordinal)) factor = 1.0;
            else if (dims.EndsWith("in", StringComparison.Ordinal)) factor = 25.4;
            else return false;
            string[] parts = dims.Substring(0, dims.Length - 2).Split('x');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b)
                || a <= 0 || b <= 0)
                return false;
            widthMm = Math.Min(a, b) * factor;
            heightMm = Math.Max(a, b) * factor;
            return true;
        }

        private static bool Same(double w1, double h1, double w2, double h2)
            => Math.Abs(w1 - w2) <= ToleranceMm && Math.Abs(h1 - h2) <= ToleranceMm;

        private static string Mm(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>What an IPP printer reports about itself (parsed from Get-Printer-Attributes).</summary>
    public sealed class IppPrinterCapabilities
    {
        public IppPrinterCapabilities(IppAttributeGroup printerAttributes)
        {
            Attributes = printerAttributes ?? throw new ArgumentNullException(nameof(printerAttributes));
            PrinterName = Text("printer-name");
            MakeAndModel = Text("printer-make-and-model");
            Info = Text("printer-info");
            Location = Text("printer-location");
            State = Int("printer-state") is int s ? (IppPrinterState)s : null;
            StateReasons = Strings("printer-state-reasons");
            IsAcceptingJobs = Attributes["printer-is-accepting-jobs"]?.Value.Value is bool b ? b : true;
            DocumentFormats = Strings("document-format-supported");
            Sides = Strings("sides-supported");
            Media = Strings("media-supported");
            MediaReady = Strings("media-ready");
            MediaSources = Strings("media-source-supported");
            MediaTypes = Strings("media-type-supported");
            ColorModes = Strings("print-color-mode-supported");
            MultipleDocumentHandling = Strings("multiple-document-handling-supported");
            PrintScaling = Strings("print-scaling-supported");
            PwgRasterTypes = Strings("pwg-raster-document-type-supported");
            PwgRasterSheetBack = ParseSheetBack(Text("pwg-raster-document-sheet-back"));
            PrintQualities = Ints("print-quality-supported");
            Orientations = Ints("orientation-requested-supported");
            OperationsSupported = Ints("operations-supported");
            PrinterResolutions = Resolutions("printer-resolution-supported");
            PwgRasterResolutions = Resolutions("pwg-raster-document-resolution-supported");
            MarginsLeft = Ints("media-left-margin-supported");
            MarginsRight = Ints("media-right-margin-supported");
            MarginsTop = Ints("media-top-margin-supported");
            MarginsBottom = Ints("media-bottom-margin-supported");
            PageRangesSupported = Attributes["page-ranges-supported"]?.Value.Value is true;

            IppAttribute? copies = Attributes["copies-supported"];
            MaxCopies = copies?.Value.Value is IppRange range ? Math.Max(1, range.Upper) : 1;
        }

        /// <summary>Every printer attribute as received, for anything not surfaced below.</summary>
        public IppAttributeGroup Attributes { get; }

        public string? PrinterName { get; }
        public string? MakeAndModel { get; }
        public string? Info { get; }
        public string? Location { get; }
        public IppPrinterState? State { get; }
        public IReadOnlyList<string> StateReasons { get; }
        public bool IsAcceptingJobs { get; }
        public IReadOnlyList<string> DocumentFormats { get; }
        public IReadOnlyList<string> Sides { get; }
        public IReadOnlyList<string> Media { get; }
        public IReadOnlyList<string> MediaReady { get; }
        public IReadOnlyList<string> MediaSources { get; }
        public IReadOnlyList<string> MediaTypes { get; }
        public IReadOnlyList<string> ColorModes { get; }
        public IReadOnlyList<string> MultipleDocumentHandling { get; }
        public IReadOnlyList<string> PrintScaling { get; }
        public IReadOnlyList<string> PwgRasterTypes { get; }
        public PwgSheetBack PwgRasterSheetBack { get; }
        public IReadOnlyList<int> PrintQualities { get; }
        public IReadOnlyList<int> Orientations { get; }
        public IReadOnlyList<int> OperationsSupported { get; }
        public IReadOnlyList<IppResolution> PrinterResolutions { get; }
        public IReadOnlyList<IppResolution> PwgRasterResolutions { get; }

        /// <summary>Supported margins in hundredths of a millimetre.</summary>
        public IReadOnlyList<int> MarginsLeft { get; }
        public IReadOnlyList<int> MarginsRight { get; }
        public IReadOnlyList<int> MarginsTop { get; }
        public IReadOnlyList<int> MarginsBottom { get; }

        public bool PageRangesSupported { get; }
        public int MaxCopies { get; }

        public bool SupportsFormat(string mimeType) => Contains(DocumentFormats, mimeType);
        public bool SupportsDuplex => Contains(Sides, "two-sided-long-edge") || Contains(Sides, "two-sided-short-edge");
        public bool SupportsColor => Contains(ColorModes, "color");
        public bool SupportsCollatedCopies => Contains(MultipleDocumentHandling, "separate-documents-collated-copies");
        public bool SupportsUncollatedCopies => Contains(MultipleDocumentHandling, "separate-documents-uncollated-copies");

        /// <summary>The smallest margins the printer can print with, in points.</summary>
        public Margins MinimumMargins => new(Min(MarginsLeft), Min(MarginsTop), Min(MarginsRight), Min(MarginsBottom));

        internal static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (string s in list) if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static double Min(IReadOnlyList<int> hundredthsMm)
        {
            if (hundredthsMm.Count == 0) return 0;
            int min = int.MaxValue;
            foreach (int v in hundredthsMm) if (v >= 0 && v < min) min = v;
            return min == int.MaxValue ? 0 : min / 100.0 / 25.4 * 72.0;
        }

        private static PwgSheetBack ParseSheetBack(string? value) => value switch
        {
            "flipped" => PwgSheetBack.Flipped,
            "rotated" => PwgSheetBack.Rotated,
            "manual-tumble" => PwgSheetBack.ManualTumble,
            _ => PwgSheetBack.Normal
        };

        private string? Text(string name)
        {
            IppValue? v = Attributes[name]?.Value;
            return v is null || v.IsOutOfBand ? null : v.Value is string or IppStringWithLanguage ? v.AsString() : null;
        }

        private int? Int(string name) => Attributes[name]?.Value.Value is int i ? i : null;

        private IReadOnlyList<string> Strings(string name)
        {
            var list = new List<string>();
            IppAttribute? a = Attributes[name];
            if (a is null) return list;
            foreach (IppValue v in a.Values)
                if (v.Value is string or IppStringWithLanguage) list.Add(v.AsString());
            return list;
        }

        private IReadOnlyList<int> Ints(string name)
        {
            var list = new List<int>();
            IppAttribute? a = Attributes[name];
            if (a is null) return list;
            foreach (IppValue v in a.Values) if (v.Value is int i) list.Add(i);
            return list;
        }

        private IReadOnlyList<IppResolution> Resolutions(string name)
        {
            var list = new List<IppResolution>();
            IppAttribute? a = Attributes[name];
            if (a is null) return list;
            foreach (IppValue v in a.Values) if (v.Value is IppResolution r) list.Add(r);
            return list;
        }
    }
}
