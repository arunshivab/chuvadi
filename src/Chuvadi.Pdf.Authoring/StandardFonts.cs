// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 — Standard Type 1 Fonts (Standard 14)
// PHASE: Phase 1.3 — Authoring module

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// The PDF Standard 14 fonts. These are guaranteed available in every
/// conforming PDF reader without embedding.
/// </summary>
public static class StandardFonts
{
    /// <summary>Helvetica (sans-serif, regular).</summary>
    public const string Helvetica = "Helvetica";

    /// <summary>Helvetica Bold.</summary>
    public const string HelveticaBold = "Helvetica-Bold";

    /// <summary>Helvetica Oblique (italic).</summary>
    public const string HelveticaOblique = "Helvetica-Oblique";

    /// <summary>Helvetica Bold Oblique.</summary>
    public const string HelveticaBoldOblique = "Helvetica-BoldOblique";

    /// <summary>Times Roman (serif, regular).</summary>
    public const string TimesRoman = "Times-Roman";

    /// <summary>Times Bold.</summary>
    public const string TimesBold = "Times-Bold";

    /// <summary>Times Italic.</summary>
    public const string TimesItalic = "Times-Italic";

    /// <summary>Times Bold Italic.</summary>
    public const string TimesBoldItalic = "Times-BoldItalic";

    /// <summary>Courier (monospace, regular).</summary>
    public const string Courier = "Courier";

    /// <summary>Courier Bold.</summary>
    public const string CourierBold = "Courier-Bold";

    /// <summary>Courier Oblique.</summary>
    public const string CourierOblique = "Courier-Oblique";

    /// <summary>Courier Bold Oblique.</summary>
    public const string CourierBoldOblique = "Courier-BoldOblique";

    /// <summary>Symbol font.</summary>
    public const string Symbol = "Symbol";

    /// <summary>ZapfDingbats font.</summary>
    public const string ZapfDingbats = "ZapfDingbats";
}
