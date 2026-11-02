using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Layout.Model
{
    /// <summary>A paper size in points (portrait).</summary>
    public readonly record struct PaperSize(string Name, double Width, double Height, int SpreadsheetCode);

    /// <summary>
    /// D-30: the paper used when a document states none (an .xlsx without pageSetup/@paperSize, a .docx section
    /// without w:pgSz). Like a Windows printer's default, it follows the region: Letter in the countries that use
    /// US Letter (CLDR paperSize data), A4 everywhere else, including a culture with no region (invariant).
    /// </summary>
    public static class PaperDefaults
    {
        /// <summary>US Letter, 8.5 × 11 in (ECMA-376 paperSize 1).</summary>
        public static PaperSize Letter { get; } = new PaperSize("Letter", 612, 792, 1);

        /// <summary>ISO A4, 210 × 297 mm (ECMA-376 paperSize 9).</summary>
        public static PaperSize A4 { get; } = new PaperSize("A4", 595.3, 841.9, 9);

        /// <summary>Regions (ISO 3166 two-letter codes) whose default paper is US Letter (CLDR supplemental paperSize).</summary>
        public static IReadOnlySet<string> LetterRegions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "US", "PR", "CA", "MX", "PH", "BZ", "CL", "CO", "CR", "GT", "NI", "PA", "SV", "VE",
        };

        /// <summary>The default paper for a culture's region: Letter for <see cref="LetterRegions"/>, otherwise A4.</summary>
        public static PaperSize ForCulture(CultureInfo? culture)
        {
            string? region = RegionOf(culture);
            return region is not null && LetterRegions.Contains(region) ? Letter : A4;
        }

        /// <summary>
        /// Page geometry for a flow document (or a .docx section without w:pgSz) in the culture's default paper, with
        /// Word's default 1" margins: <see cref="PageGeometry.Letter"/> or <see cref="PageGeometry.A4"/>.
        /// </summary>
        public static PageGeometry PageForCulture(CultureInfo? culture)
            => ForCulture(culture).SpreadsheetCode == Letter.SpreadsheetCode ? PageGeometry.Letter : PageGeometry.A4;

        /// <summary>The two-letter region of a culture; a neutral culture uses its default specific culture; null when there is none.</summary>
        internal static string? RegionOf(CultureInfo? culture)
        {
            if (culture is null || string.IsNullOrEmpty(culture.Name))
            {
                return null;
            }
            try
            {
                CultureInfo specific = culture.IsNeutralCulture ? CultureInfo.CreateSpecificCulture(culture.Name) : culture;
                if (string.IsNullOrEmpty(specific.Name))
                {
                    return null;
                }
                return new RegionInfo(specific.Name).TwoLetterISORegionName;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
