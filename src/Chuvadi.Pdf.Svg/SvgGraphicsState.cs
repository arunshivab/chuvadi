// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.0 — SVG export

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Svg;

/// <summary>2x3 affine transformation matrix in column-major form (PDF convention).</summary>
internal readonly record struct Mat2x3(double A, double B, double C, double D, double E, double F)
{
    internal static Mat2x3 Identity { get; } = new(1, 0, 0, 1, 0, 0);

    internal Mat2x3 Multiply(Mat2x3 other) => new(
        A: A * other.A + B * other.C,
        B: A * other.B + B * other.D,
        C: C * other.A + D * other.C,
        D: C * other.B + D * other.D,
        E: E * other.A + F * other.C + other.E,
        F: E * other.B + F * other.D + other.F);

    internal string ToSvgMatrix(string fmt) => string.Format(
        CultureInfo.InvariantCulture, "matrix({0} {1} {2} {3} {4} {5})",
        A.ToString(fmt, CultureInfo.InvariantCulture),
        B.ToString(fmt, CultureInfo.InvariantCulture),
        C.ToString(fmt, CultureInfo.InvariantCulture),
        D.ToString(fmt, CultureInfo.InvariantCulture),
        E.ToString(fmt, CultureInfo.InvariantCulture),
        F.ToString(fmt, CultureInfo.InvariantCulture));
}

/// <summary>Internal graphics state tracked during content stream dispatch.</summary>
internal sealed class SvgGraphicsState
{
    internal Mat2x3 Ctm { get; set; } = Mat2x3.Identity;

    internal string FillColor { get; set; } = "rgb(0,0,0)";
    internal string StrokeColor { get; set; } = "rgb(0,0,0)";

    internal double LineWidth { get; set; } = 1.0;
    internal int LineCap { get; set; }
    internal int LineJoin { get; set; }
    internal double MiterLimit { get; set; } = 10.0;
    internal string? DashArray { get; set; }
    internal double DashPhase { get; set; }

    // Text state
    internal Mat2x3 TextMatrix { get; set; } = Mat2x3.Identity;
    internal Mat2x3 TextLineMatrix { get; set; } = Mat2x3.Identity;
    internal string? FontName { get; set; }
    internal double FontSize { get; set; } = 12.0;
    internal double CharSpacing { get; set; }
    internal double WordSpacing { get; set; }
    internal double HorizontalScaling { get; set; } = 100.0;
    internal double Leading { get; set; }
    internal int RenderingMode { get; set; }
    internal double TextRise { get; set; }

    // Path under construction
    internal StringBuilder CurrentPath { get; } = new();
    internal bool HasCurrentPath { get; set; }

    internal void AppendPath(string segment)
    {
        if (CurrentPath.Length > 0) { CurrentPath.Append(' '); }
        CurrentPath.Append(segment);
        HasCurrentPath = true;
    }

    internal string TakePath()
    {
        string p = CurrentPath.ToString();
        CurrentPath.Clear();
        HasCurrentPath = false;
        return p;
    }

    internal SvgGraphicsState Clone() => new()
    {
        Ctm = Ctm,
        FillColor = FillColor,
        StrokeColor = StrokeColor,
        LineWidth = LineWidth,
        LineCap = LineCap,
        LineJoin = LineJoin,
        MiterLimit = MiterLimit,
        DashArray = DashArray,
        DashPhase = DashPhase,
        TextMatrix = TextMatrix,
        TextLineMatrix = TextLineMatrix,
        FontName = FontName,
        FontSize = FontSize,
        CharSpacing = CharSpacing,
        WordSpacing = WordSpacing,
        HorizontalScaling = HorizontalScaling,
        Leading = Leading,
        RenderingMode = RenderingMode,
        TextRise = TextRise,
    };
}

/// <summary>Stack of graphics states for q/Q operators.</summary>
internal sealed class SvgStateStack
{
    private readonly Stack<SvgGraphicsState> _stack = new();
    internal SvgGraphicsState Current { get; private set; } = new();

    internal void Push()
    {
        _stack.Push(Current.Clone());
    }

    internal void Pop()
    {
        if (_stack.Count > 0) { Current = _stack.Pop(); }
    }
}
