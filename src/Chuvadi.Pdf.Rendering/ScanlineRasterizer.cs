// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.3.3 — Fill rules (non-zero winding, even-odd)
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering
// Scanline polygon fill algorithm for path rendering.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Fills vector paths into a <see cref="PixelBuffer"/> using a scanline
/// edge-crossing algorithm.
/// </summary>
/// <remarks>
/// Supports both PDF fill rules:
/// <list type="bullet">
///   <item>Non-zero winding number — PDF operators f, F, B, b</item>
///   <item>Even-odd — PDF operators f*, B*, b*</item>
/// </list>
///
/// Input is a list of sub-paths from <see cref="PathFlattener"/>,
/// each being a closed list of <see cref="PointF"/> vertices in device space.
///
/// PDF 32000-1:2008 §8.5.3.3 — Filling.
/// </remarks>
public sealed class ScanlineRasterizer
{
    /// <summary>
    /// Fills the given sub-paths into the pixel buffer with the given colour
    /// and fill rule.
    /// </summary>
    /// <param name="buffer">The pixel buffer to draw into.</param>
    /// <param name="subPaths">Sub-paths from <see cref="PathFlattener.Flatten"/>.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="fillRule">Non-zero winding or even-odd.</param>
    public void Fill(
        PixelBuffer buffer,
        List<List<PointF>> subPaths,
        ColorF color,
        FillRule fillRule)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (subPaths is null)
        {
            throw new ArgumentNullException(nameof(subPaths));
        }

        if (subPaths.Count == 0)
        {
            return;
        }

        // Build edge table from all sub-paths
        List<Edge> edges = BuildEdges(subPaths);

        if (edges.Count == 0)
        {
            return;
        }

        // Find Y range
        int yMin = buffer.Height;
        int yMax = 0;

        foreach (Edge edge in edges)
        {
            int eMin = (int)Math.Floor(Math.Min(edge.Y0, edge.Y1));
            int eMax = (int)Math.Ceiling(Math.Max(edge.Y0, edge.Y1));

            if (eMin < yMin) { yMin = eMin; }
            if (eMax > yMax) { yMax = eMax; }
        }

        yMin = Math.Max(0, yMin);
        yMax = Math.Min(buffer.Height - 1, yMax);

        // Scanline fill
        for (int y = yMin; y <= yMax; y++)
        {
            double scanY = y + 0.5; // Sample at pixel centre

            List<Crossing> crossings = new List<Crossing>();

            foreach (Edge edge in edges)
            {
                double eYMin = Math.Min(edge.Y0, edge.Y1);
                double eYMax = Math.Max(edge.Y0, edge.Y1);

                if (scanY < eYMin || scanY >= eYMax)
                {
                    continue;
                }

                double t = (scanY - edge.Y0) / (edge.Y1 - edge.Y0);
                double x = edge.X0 + t * (edge.X1 - edge.X0);
                crossings.Add(new Crossing(x, edge.Winding));
            }

            if (crossings.Count == 0)
            {
                continue;
            }

            crossings.Sort((a, b) => a.X.CompareTo(b.X));

            FillScanline(buffer, y, crossings, color, fillRule);
        }
    }

    // ── Edge construction ─────────────────────────────────────────────────

    private static List<Edge> BuildEdges(List<List<PointF>> subPaths)
    {
        List<Edge> edges = new List<Edge>();

        foreach (List<PointF> subPath in subPaths)
        {
            if (subPath.Count < 2)
            {
                continue;
            }

            for (int i = 0; i < subPath.Count - 1; i++)
            {
                PointF p0 = subPath[i];
                PointF p1 = subPath[i + 1];

                // Skip horizontal edges (don't contribute to crossing count)
                if (Math.Abs(p0.Y - p1.Y) < 1e-6)
                {
                    continue;
                }

                // Winding: +1 if going upward (Y increases), -1 if downward
                int winding = p1.Y > p0.Y ? 1 : -1;
                edges.Add(new Edge(p0.X, p0.Y, p1.X, p1.Y, winding));
            }
        }

        return edges;
    }

    // ── Scanline fill ─────────────────────────────────────────────────────

    private static void FillScanline(
        PixelBuffer buffer, int y,
        List<Crossing> crossings,
        ColorF color, FillRule fillRule)
    {
        if (fillRule == FillRule.EvenOdd)
        {
            FillEvenOdd(buffer, y, crossings, color);
        }
        else
        {
            FillNonZeroWinding(buffer, y, crossings, color);
        }
    }

    private static void FillEvenOdd(
        PixelBuffer buffer, int y,
        List<Crossing> crossings, ColorF color)
    {
        // Even-odd: fill between pairs (0-1, 2-3, …)
        for (int i = 0; i + 1 < crossings.Count; i += 2)
        {
            int xStart = Math.Max(0, (int)Math.Ceiling(crossings[i].X));
            int xEnd   = Math.Min(buffer.Width - 1, (int)Math.Floor(crossings[i + 1].X));

            for (int x = xStart; x <= xEnd; x++)
            {
                buffer.BlendPixel(x, y, color);
            }
        }
    }

    private static void FillNonZeroWinding(
        PixelBuffer buffer, int y,
        List<Crossing> crossings, ColorF color)
    {
        // Non-zero: track winding number; fill where winding != 0
        int winding = 0;
        int prevX = 0;
        bool inside = false;

        foreach (Crossing crossing in crossings)
        {
            if (inside)
            {
                int xStart = Math.Max(0, prevX);
                int xEnd   = Math.Min(buffer.Width - 1, (int)Math.Floor(crossing.X));

                for (int x = xStart; x <= xEnd; x++)
                {
                    buffer.BlendPixel(x, y, color);
                }
            }

            winding += crossing.Winding;
            inside = winding != 0;
            prevX = (int)Math.Ceiling(crossing.X);
        }
    }

    // ── Data structures ───────────────────────────────────────────────────

    private readonly struct Edge
    {
        internal Edge(double x0, double y0, double x1, double y1, int winding)
        {
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Winding = winding;
        }

        internal double X0 { get; }
        internal double Y0 { get; }
        internal double X1 { get; }
        internal double Y1 { get; }
        internal int Winding { get; }
    }

    private readonly struct Crossing
    {
        internal Crossing(double x, int winding)
        {
            X = x;
            Winding = winding;
        }

        internal double X { get; }
        internal int Winding { get; }
    }
}
