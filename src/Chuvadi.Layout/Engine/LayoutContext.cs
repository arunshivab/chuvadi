using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Engine
{
    /// <summary>Per-document state shared by the engines: options, fonts, shaping and the warning log.</summary>
    internal sealed class LayoutContext
    {
        private readonly object _gate = new object();
        private readonly List<LayoutWarning> _warnings = new List<LayoutWarning>();
        private readonly HashSet<(string, string?)> _warned = new HashSet<(string, string?)>();

        public LayoutContext(LayoutOptions options, FontResolver fonts)
        {
            Options = options;
            Fonts = fonts;
            Limits = options.Limits;
        }

        public LayoutOptions Options { get; }

        public FontResolver Fonts { get; }

        public LayoutLimits Limits { get; }

        public IReadOnlyList<LayoutWarning> Warnings
        {
            get
            {
                lock (_gate)
                {
                    return _warnings.ToArray();
                }
            }
        }

        /// <summary>Records a warning once per (code, location).</summary>
        public void Warn(string code, LayoutWarningSeverity severity, string message, string? location = null)
        {
            lock (_gate)
            {
                if (_warned.Add((code, location)))
                {
                    _warnings.Add(new LayoutWarning(code, severity, message, location));
                }
            }
        }

        /// <summary>Chooses the shaper for a run and warns when a complex script cannot be shaped.</summary>
        public IShaper ShaperFor(FontFace face, UnicodeScript script)
        {
            IShaper? complex = Options.Shaper;
            if (complex is not null && complex.Supports(face, script))
            {
                return complex;
            }
            if (UnicodeScripts.NeedsComplexShaping(script))
            {
                if (UnicodeScripts.IsRightToLeft(script))
                {
                    Warn(LayoutWarningCodes.BidiNotSupported, LayoutWarningSeverity.Fidelity,
                        $"{script} text is laid out left-to-right without bidi reordering or joining.", script.ToString());
                }
                else
                {
                    Warn(LayoutWarningCodes.ComplexScriptNotShaped, LayoutWarningSeverity.Fidelity,
                        $"{script} text is laid out with nominal glyphs: no complex-script shaper is configured (LayoutOptions.Shaper).", script.ToString());
                }
            }
            return SimpleShaper.Instance;
        }

        public static void ThrowIfCancelled(CancellationToken ct) => ct.ThrowIfCancellationRequested();
    }

    /// <summary>Stable warning codes. Documented in DESIGN.md.</summary>
    public static class LayoutWarningCodes
    {
        /// <summary>A complex script was laid out without a shaper that supports it.</summary>
        public const string ComplexScriptNotShaped = "ComplexScriptNotShaped";

        /// <summary>Right-to-left text was laid out without bidi.</summary>
        public const string BidiNotSupported = "BidiNotSupported";

        /// <summary>A section has more than one text column; laid out as one column.</summary>
        public const string MultiColumnNotSupported = "MultiColumnNotSupported";

        /// <summary>A font was substituted with one that is not metric-compatible; breaks may move.</summary>
        public const string NonMetricFontSubstitution = "NonMetricFontSubstitution";

        /// <summary>An image format cannot be rendered (EMF/WMF and others); a placeholder box is drawn.</summary>
        public const string ImagePlaceholder = "ImagePlaceholder";

        /// <summary>A line or row is taller than the page and was clipped.</summary>
        public const string ContentTallerThanPage = "ContentTallerThanPage";

        /// <summary>A feature of the source is not laid out by this version.</summary>
        public const string UnsupportedFeature = "UnsupportedFeature";

        /// <summary>A sheet drawing was simplified: rotation not applied, or a preset shape drawn as its rectangle.</summary>
        public const string DrawingApproximated = "DrawingApproximated";

        /// <summary>A chart was drawn as a labelled placeholder (charts are not rendered in this version).</summary>
        public const string ChartPlaceholder = "ChartPlaceholder";

        /// <summary>A pattern or gradient cell fill was drawn as one blended colour.</summary>
        public const string FillApproximated = "FillApproximated";

        /// <summary>A conditional format could not be evaluated (unsupported function, external reference); it was not applied.</summary>
        public const string ConditionalFormatNotEvaluated = "ConditionalFormatNotEvaluated";

        /// <summary>Conditional-format icons were drawn as simplified vector icons.</summary>
        public const string IconSetApproximated = "IconSetApproximated";

        /// <summary>Wrapping was approximated (through as tight; a table placed below a floating object).</summary>
        public const string TextWrapApproximated = "TextWrapApproximated";

        /// <summary>The page count estimate was used for a NUMPAGES field before the count was final.</summary>
        public const string ProvisionalPageCount = "ProvisionalPageCount";

        /// <summary>A font embedded in the source document could not be read; the font is substituted.</summary>
        public const string EmbeddedFontUnreadable = "EmbeddedFontUnreadable";

        /// <summary>
        /// A font embedded in the source document (usually a subset) has no glyph for some characters; they are drawn
        /// with the full font of the same family from the other sources (installed fonts first), or, when no other
        /// source has the family, with the script fallback (D-116). Once per family.
        /// </summary>
        public const string EmbeddedFontMissingGlyphs = "EmbeddedFontMissingGlyphs";
    }
}
