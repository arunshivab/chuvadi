using System;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>Glyph outline technology of a face.</summary>
    public enum FontOutlineFormat
    {
        /// <summary>TrueType quadratic outlines ('glyf'/'loca').</summary>
        TrueType,

        /// <summary>PostScript/CFF cubic outlines ('CFF ' or 'CFF2'), file signature 'OTTO'.</summary>
        Cff,
    }

    /// <summary>Where a face came from. Reported in substitutions and warnings.</summary>
    public enum FontOrigin
    {
        /// <summary>Bytes supplied by the caller (for example via <see cref="MemoryFontSource"/>).</summary>
        Supplied,

        /// <summary>A bundled font pack (for example Chuvadi.Layout.Fonts).</summary>
        Bundled,

        /// <summary>An operating-system font directory (only when system fonts are allowed).</summary>
        System,

        /// <summary>A font embedded in the source document (for example an obfuscated .odttf part in a .docx).</summary>
        Embedded,
    }

    /// <summary>OS/2 fsType embedding permissions.</summary>
    [Flags]
    public enum FontEmbeddingRights
    {
        /// <summary>Installable embedding: no restrictions (fsType = 0).</summary>
        Installable = 0,

        /// <summary>Restricted license embedding (bit 1).</summary>
        Restricted = 0x0002,

        /// <summary>Preview and print embedding (bit 2).</summary>
        PreviewAndPrint = 0x0004,

        /// <summary>Editable embedding (bit 3).</summary>
        Editable = 0x0008,

        /// <summary>The font must not be subset before embedding (bit 8).</summary>
        NoSubsetting = 0x0100,

        /// <summary>Only bitmaps may be embedded (bit 9).</summary>
        BitmapOnly = 0x0200,
    }

    /// <summary>One set of vertical metrics, in font units. <see cref="Descender"/> is negative below the baseline.</summary>
    public readonly record struct FontLineMetrics(int Ascender, int Descender, int LineGap)
    {
        /// <summary>Ascender − Descender + LineGap.</summary>
        public int LineHeight => Ascender - Descender + LineGap;
    }

    /// <summary>
    /// Which vertical metrics a consumer uses to compute line height. Word and Excel on Windows use
    /// the OS/2 Windows metrics (usWinAscent/usWinDescent); browsers and most other engines use hhea.
    /// </summary>
    public enum VerticalMetricsSource
    {
        /// <summary>OS/2 usWinAscent and usWinDescent, line gap 0. What Word and Excel use.</summary>
        Windows,

        /// <summary>hhea ascender, descender and line gap.</summary>
        Hhea,

        /// <summary>OS/2 sTypoAscender, sTypoDescender and sTypoLineGap.</summary>
        Typographic,

        /// <summary>Typographic when OS/2 fsSelection USE_TYPO_METRICS (bit 7) is set, otherwise hhea.</summary>
        Auto,
    }
}
