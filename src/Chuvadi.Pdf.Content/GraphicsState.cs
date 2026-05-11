// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4 — Graphics state
//        PDF 32000-1:2008 §9.3  — Text state parameters
// PHASE: Phase 1 — Chuvadi.Pdf.Content
// Tracks current graphics and text state during content stream processing.

using Chuvadi.Pdf.Fonts;

namespace Chuvadi.Pdf.Content;

/// <summary>
/// Represents the graphics and text state at a point in content stream processing.
/// </summary>
/// <remarks>
/// The graphics state is maintained as a stack. The operators q/Q push/pop
/// the state. This class represents one level on that stack.
///
/// For Phase 1 text extraction, only text-relevant state is tracked:
/// font, font size, text matrix, text line matrix, character spacing,
/// word spacing, and horizontal scaling.
///
/// PDF 32000-1:2008 §8.4 — Graphics state.
/// PDF 32000-1:2008 §9.3 — Text state parameters, Table 104.
/// </remarks>
public sealed class GraphicsState
{
    /// <summary>Creates a new <see cref="GraphicsState"/> with default values.</summary>
    public GraphicsState()
    {
        Font = PdfFont.Default();
        FontSize = 12.0;
        CharacterSpacing = 0.0;
        WordSpacing = 0.0;
        HorizontalScaling = 100.0;
        TextRise = 0.0;
        TextMatrix = Matrix3x3.Identity;
        TextLineMatrix = Matrix3x3.Identity;
        CurrentTransformationMatrix = Matrix3x3.Identity;
    }

    // ── Text state ────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the current font.
    /// PDF 32000-1:2008 §9.3.1 — Tf operator.
    /// </summary>
    public PdfFont Font { get; set; }

    /// <summary>Gets or sets the current font size in text space units.</summary>
    public double FontSize { get; set; }

    /// <summary>
    /// Gets or sets the character spacing (Tc).
    /// Added to the horizontal or vertical displacement after each glyph.
    /// PDF 32000-1:2008 §9.3.2.
    /// </summary>
    public double CharacterSpacing { get; set; }

    /// <summary>
    /// Gets or sets the word spacing (Tw).
    /// Added to displacement after space character (code 0x20).
    /// PDF 32000-1:2008 §9.3.3.
    /// </summary>
    public double WordSpacing { get; set; }

    /// <summary>
    /// Gets or sets the horizontal scaling (Tz) as a percentage (default 100).
    /// PDF 32000-1:2008 §9.3.4.
    /// </summary>
    public double HorizontalScaling { get; set; }

    /// <summary>
    /// Gets or sets the text leading (TL) — vertical distance between lines.
    /// PDF 32000-1:2008 §9.3.5.
    /// </summary>
    public double TextLeading { get; set; }

    /// <summary>
    /// Gets or sets the text rise (Ts) — vertical displacement from baseline.
    /// PDF 32000-1:2008 §9.3.6.
    /// </summary>
    public double TextRise { get; set; }

    // ── Matrices ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gets or sets the text matrix (Tm) — transforms text space to user space.
    /// Updated by Tm, Td, TD, T*, and text-showing operators.
    /// PDF 32000-1:2008 §9.4.1.
    /// </summary>
    public Matrix3x3 TextMatrix { get; set; }

    /// <summary>
    /// Gets or sets the text line matrix — tracks the start of the current text line.
    /// Updated by Td, TD, and T*.
    /// PDF 32000-1:2008 §9.4.1.
    /// </summary>
    public Matrix3x3 TextLineMatrix { get; set; }

    /// <summary>
    /// Gets or sets the current transformation matrix (CTM).
    /// Updated by the cm operator.
    /// PDF 32000-1:2008 §8.4.4.
    /// </summary>
    public Matrix3x3 CurrentTransformationMatrix { get; set; }

    // ── Clone ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a deep copy of this state for the graphics state stack.
    /// </summary>
    public GraphicsState Clone()
    {
        return new GraphicsState
        {
            Font = Font,
            FontSize = FontSize,
            CharacterSpacing = CharacterSpacing,
            WordSpacing = WordSpacing,
            HorizontalScaling = HorizontalScaling,
            TextRise = TextRise,
            TextMatrix = TextMatrix,
            TextLineMatrix = TextLineMatrix,
            CurrentTransformationMatrix = CurrentTransformationMatrix,
        };
    }
}

/// <summary>
/// A 3x3 matrix used for 2D affine transformations in PDF user space.
/// PDF uses the form [a b c d e f] representing the matrix:
/// | a b 0 |
/// | c d 0 |
/// | e f 1 |
/// PDF 32000-1:2008 §8.3.3 — Transformation matrices.
/// </summary>
public readonly struct Matrix3x3
{
    /// <summary>Initialises a matrix with the given six components.</summary>
    public Matrix3x3(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    /// <summary>Gets the identity matrix.</summary>
    public static Matrix3x3 Identity { get; } = new Matrix3x3(1, 0, 0, 1, 0, 0);

    /// <summary>Horizontal scaling component.</summary>
    public double A { get; }

    /// <summary>Horizontal shearing component.</summary>
    public double B { get; }

    /// <summary>Vertical shearing component.</summary>
    public double C { get; }

    /// <summary>Vertical scaling component.</summary>
    public double D { get; }

    /// <summary>Horizontal translation component.</summary>
    public double E { get; }

    /// <summary>Vertical translation component.</summary>
    public double F { get; }

    /// <summary>
    /// Multiplies two transformation matrices (this × other).
    /// PDF 32000-1:2008 §8.3.3.
    /// </summary>
    public Matrix3x3 Multiply(Matrix3x3 other)
    {
        return new Matrix3x3(
            a: A * other.A + B * other.C,
            b: A * other.B + B * other.D,
            c: C * other.A + D * other.C,
            d: C * other.B + D * other.D,
            e: E * other.A + F * other.C + other.E,
            f: E * other.B + F * other.D + other.F);
    }

    /// <summary>Translates by (tx, ty) in the current coordinate system.</summary>
    public Matrix3x3 Translate(double tx, double ty)
    {
        return Multiply(new Matrix3x3(1, 0, 0, 1, tx, ty));
    }

    /// <summary>Gets the X translation (horizontal position).</summary>
    public double TranslationX => E;

    /// <summary>Gets the Y translation (vertical position).</summary>
    public double TranslationY => F;

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{A:G4} {B:G4} {C:G4} {D:G4} {E:G4} {F:G4}]";
}
