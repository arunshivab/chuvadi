using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout
{
    /// <summary>
    /// Hard limits that bound the work and memory spent on one document. Every limit throws
    /// <see cref="LayoutLimitExceededException"/> before it is exceeded.
    /// <para>The defaults are safe for files from anyone (D-406, Rule 6): <c>new LayoutLimits()</c> has the same values
    /// as <see cref="ForUntrustedInput"/>, and the readers start from their own untrusted presets. For very large files
    /// you made or trust (over 2,000 pages, 50 MiB, 200,000 blocks or 1,000,000 cells per sheet), use
    /// <see cref="ForTrustedInput"/>.</para>
    /// </summary>
    public sealed class LayoutLimits
    {
        /// <summary>
        /// Maximum number of pages that may be built. Default 2,000 (<see cref="ForTrustedInput"/>: 10,000). A flow
        /// document (docx, text, markup) stops paginating here. A workbook opens whatever its page count, since its pages
        /// are computed only when asked for, and building, exporting or printing a page at or past this limit throws
        /// <see cref="LayoutLimitExceededException"/> with the workbook's page count and the sheet and range in
        /// <see cref="LayoutLimitExceededException.Location"/> (D-291). A workbook of more than <see cref="int.MaxValue"/>
        /// pages is refused at open.
        /// </summary>
        public int MaxPages { get; set; } = 2_000;

        /// <summary>Maximum number of populated cells per sheet that will be laid out. Default 1,000,000
        /// (<see cref="ForTrustedInput"/>: 5,000,000).</summary>
        public long MaxCellsPerSheet { get; set; } = 1_000_000;

        /// <summary>
        /// Maximum number of blocks (paragraphs, tables, rows) in a flow document. Default 200,000
        /// (<see cref="ForTrustedInput"/>: 2,000,000).
        /// <para>For a .docx it also caps the XML elements Chuvadi.Docs reads while reading, before the document model is
        /// built (D-406 S2b): the smaller of five per block and one per KiB of <see cref="MaxLayoutBytes"/>. That is
        /// 262,144 elements by default and 524,288 under <see cref="ForTrustedInput"/>. Real documents have about 6 to 8
        /// elements per block, so the default opens documents of about 33,000 blocks or more; the model costs up to about
        /// 0.8 KB per element (measured with a file of empty paragraphs).</para>
        /// </summary>
        public long MaxBlocks { get; set; } = 200_000;

        /// <summary>Maximum nesting depth of tables within tables. Default 8 (<see cref="ForTrustedInput"/>: 32).</summary>
        public int MaxNestingDepth { get; set; } = 8;

        /// <summary>Maximum bytes of a single image or font program. Default 16 MiB (<see cref="ForTrustedInput"/>:
        /// 64 MiB).</summary>
        public long MaxResourceBytes { get; set; } = 16L * 1024 * 1024;

        /// <summary>Approximate cap on memory retained by the layout (cached pages, measured lines). Default 256 MiB
        /// (<see cref="ForTrustedInput"/>: 512 MiB).</summary>
        public long MaxLayoutBytes { get; set; } = 256L * 1024 * 1024;

        /// <summary>Maximum bytes read from a source stream. Default 50 MiB (<see cref="ForTrustedInput"/>:
        /// 256 MiB).</summary>
        public long MaxSourceBytes { get; set; } = 50L * 1024 * 1024;

        /// <summary>Maximum characters in one paragraph or one cell. Default 200,000 (<see cref="ForTrustedInput"/>:
        /// 1,000,000).</summary>
        public int MaxCharactersPerBlock { get; set; } = 200_000;

        /// <summary>Returns a copy.</summary>
        public LayoutLimits Clone() => (LayoutLimits)MemberwiseClone();

        /// <summary>True for an instance made by <see cref="ForTrustedInput"/> (or cloned from one): readers then start
        /// from their plain options instead of their untrusted presets (D-406; before 0.3.1 the untrusted preset was the
        /// opt-in, D-317 U1).</summary>
        internal bool Trusted { get; private init; }

        /// <summary>
        /// The safe limits, the same as <c>new LayoutLimits()</c> since 0.3.1 (D-406): 2,000 pages, 50 MiB of source,
        /// 1,000,000 cells per sheet, 200,000 blocks, nesting depth 8, 16 MiB per image or font, 256 MiB of layout
        /// memory, 200,000 characters per paragraph or cell. The readers start from their own untrusted presets
        /// (Chuvadi.Docs: <c>DocxReaderOptions.ForUntrustedInput</c>, compression ratio 200:1, 10,000 entries, spin count
        /// 500,000), with these limits applied on top. Kept so code written for 0.2 and 0.3.0 reads the same.
        /// </summary>
        public static LayoutLimits ForUntrustedInput() => new LayoutLimits();

        /// <summary>
        /// Wider limits for very large files you made or trust (D-406): 10,000 pages, 256 MiB of source, 5,000,000 cells
        /// per sheet, 2,000,000 blocks, nesting depth 32, 64 MiB per image or font, 512 MiB of layout memory, 1,000,000
        /// characters per paragraph or cell; the readers start from their plain options (compression ratio 1,000:1).
        /// These were the defaults before 0.3.1. Do not use them for files from other people: a small crafted file can
        /// then use gigabytes of memory before a limit stops it.
        /// The choice is remembered by this instance and its <see cref="Clone"/>, also after values are changed.
        /// </summary>
        public static LayoutLimits ForTrustedInput() => new LayoutLimits
        {
            Trusted = true,
            MaxPages = 10_000,
            MaxSourceBytes = 256L * 1024 * 1024,
            MaxCellsPerSheet = 5_000_000,
            MaxBlocks = 2_000_000,
            MaxNestingDepth = 32,
            MaxResourceBytes = 64L * 1024 * 1024,
            MaxLayoutBytes = 512L * 1024 * 1024,
            MaxCharactersPerBlock = 1_000_000,
        };

        /// <summary>Throws when <paramref name="value"/> exceeds <paramref name="limit"/>.</summary>
        internal static void Check(string name, long limit, long value)
        {
            if (value > limit)
            {
                throw new LayoutLimitExceededException(name, limit, value);
            }
        }
    }

    /// <summary>Options for laying out a document.</summary>
    public sealed class LayoutOptions
    {
        /// <summary>
        /// Font sources, searched in order after any fonts embedded in the document and (unless
        /// <see cref="PreferBundledFonts"/>) installed fonts. Typically the
        /// Chuvadi.Layout.Fonts pack, the metric-compatible substitutes:
        /// <c>FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance))</c> (see <see cref="FontPackSource"/>). Nothing is
        /// read from disk or the network unless a source does so itself.
        /// </summary>
        public IList<IFontSource> FontSources { get; } = new List<IFontSource>();

        /// <summary>
        /// Also search the operating system's font directories. Off by default so output is identical on every
        /// machine; turn on in desktop viewers to use real Office fonts when present. Installed fonts are searched
        /// before <see cref="FontSources"/> and the built-in fonts, so an installed family wins over a packed copy of
        /// the same family (D-116), unless <see cref="PreferBundledFonts"/> is set. The folders are scanned once per process
        /// (<see cref="SystemFontSource.Shared"/>); call <see cref="DirectoryFontSource.Refresh"/> on it to see fonts
        /// installed while the application runs.
        /// </summary>
        public bool AllowSystemFonts { get; set; }

        /// <summary>
        /// Search <see cref="FontSources"/> and the built-in fonts before installed fonts, so the same family gives the
        /// same output on every machine (servers, tests) even with <see cref="AllowSystemFonts"/> on. Default false:
        /// an installed font wins over the pack (D-116). Has no effect while <see cref="AllowSystemFonts"/> is off.
        /// </summary>
        public bool PreferBundledFonts { get; set; }

        /// <summary>Use fonts embedded in the source document. Default true.</summary>
        public bool UseEmbeddedFonts { get; set; } = true;

        /// <summary>
        /// Use the built-in fonts: the LiPi Sans faces of the Chuvadi.Fonts package (Latin and nine Indic scripts),
        /// searched after <see cref="FontSources"/>. They are the default script fallback and last resort, so text is
        /// never left without a font. Default true; false lays out with only the document's, the app's and (if
        /// allowed) the installed fonts.
        /// </summary>
        public bool UseBuiltInFonts { get; set; } = true;

        /// <summary>Substitution and fallback rules. Default <see cref="FontSubstitutionTable.CreateDefault"/>.</summary>
        public FontSubstitutionTable FontSubstitutions { get; set; } = FontSubstitutionTable.CreateDefault();

        /// <summary>
        /// Shaper for complex scripts. Every run whose script and face this shaper
        /// <see cref="IShaper.Supports"/> goes through it; all other runs use <see cref="SimpleShaper"/>.
        /// Null (the default) means only the simple shaper: Indic and other complex scripts are then laid out with
        /// nominal glyphs and a ComplexScriptNotShaped warning. Chuvadi.Layout.Pdf supplies PdfShaper.
        /// </summary>
        public IShaper? Shaper { get; set; }

        /// <summary>Limits for untrusted input.</summary>
        public LayoutLimits Limits { get; set; } = new LayoutLimits();

        /// <summary>Culture for number and date formatting and field results. Default invariant.</summary>
        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

        /// <summary>Value for date/time fields (DATE, TIME, Excel &amp;D/&amp;T). Null means the current time (non-deterministic).</summary>
        public DateTimeOffset? Now { get; set; }

        /// <summary>File name for FILENAME fields and Excel &amp;F.</summary>
        public string? DocumentName { get; set; }

        /// <summary>Password for encrypted documents.</summary>
        public string? Password { get; set; }

        /// <summary>Number of laid-out pages (with display lists) kept in memory. Default 8.</summary>
        public int PageCacheSize { get; set; } = 8;

        /// <summary>
        /// Builds the resolver for these options. Search order (D-116): <paramref name="embedded"/> (if
        /// <see cref="UseEmbeddedFonts"/>), then installed fonts (if <see cref="AllowSystemFonts"/>), then
        /// <see cref="FontSources"/>, then the built-in fonts (if <see cref="UseBuiltInFonts"/>). With
        /// <see cref="PreferBundledFonts"/>, installed fonts come last instead.
        /// </summary>
        public FontResolver CreateFontResolver(IFontSource? embedded = null)
        {
            List<IFontSource> sources = new List<IFontSource>();
            if (embedded is not null && UseEmbeddedFonts)
            {
                sources.Add(embedded);
            }
            if (AllowSystemFonts && !PreferBundledFonts)
            {
                sources.Add(SystemFontSource.Shared);
            }
            sources.AddRange(FontSources);
            if (UseBuiltInFonts)
            {
                sources.Add(FontPackSource.For(Chuvadi.Fonts.FontPack.Instance));
            }
            if (AllowSystemFonts && PreferBundledFonts)
            {
                sources.Add(SystemFontSource.Shared);
            }
            return new FontResolver(sources, FontSubstitutions);
        }
    }

    /// <summary>Severity of a <see cref="LayoutWarning"/>.</summary>
    public enum LayoutWarningSeverity
    {
        /// <summary>Informational: output is faithful.</summary>
        Info,

        /// <summary>Output differs from the source application in a known, bounded way.</summary>
        Fidelity,

        /// <summary>Content could not be rendered and was replaced or omitted.</summary>
        ContentLoss,
    }

    /// <summary>A non-fatal problem found while laying out. <see cref="Code"/> is stable and documented.</summary>
    public sealed record LayoutWarning(string Code, LayoutWarningSeverity Severity, string Message, string? Location = null)
    {
        public override string ToString() => Location is null ? $"{Code}: {Message}" : $"{Code} at {Location}: {Message}";
    }
}
