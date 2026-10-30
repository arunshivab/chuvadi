using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>Vertical placement of a text box's content (a:bodyPr/@anchor).</summary>
public enum TextBoxVerticalAnchor
{
    Top,
    Center,
    Bottom,
}

/// <summary>
/// A text box (a DrawingML shape, wps:wsp, holding paragraphs and tables): inline in the text or
/// floating with the same geometry as a floating picture (<see cref="FloatingPosition"/>). It sits
/// in a paragraph as a run (<see cref="Run.TextBox"/>). Fill and outline are kept when they are a
/// plain colour or none; other shape effects are not modelled.
/// </summary>
public sealed class TextBox
{
    private readonly List<(Paragraph? After, DocTable Table)> _tables = new();

    internal bool RemoveTableInternal(DocTable t) => _tables.RemoveAll(e => ReferenceEquals(e.Table, t)) > 0;

    /// <summary>Inline (in the line of text) or floating (anchored to the paragraph).</summary>
    public ImagePlacement Placement { get; set; } = ImagePlacement.Floating;

    /// <summary>Position, wrapping and stacking when floating (null = at the anchor paragraph, square wrap).</summary>
    public FloatingPosition? Position { get; set; }

    /// <summary>Width in points.</summary>
    public double WidthPt { get; set; } = 144;

    /// <summary>Height in points (with <see cref="AutoFit"/>, the height Word last computed).</summary>
    public double HeightPt { get; set; } = 72;

    /// <summary>Width relative to a page frame (Word 2010+); wins over <see cref="WidthPt"/> when set.</summary>
    public RelativeSize? RelativeWidth { get; set; }

    /// <summary>Height relative to a page frame (Word 2010+); wins over <see cref="HeightPt"/> when set.</summary>
    public RelativeSize? RelativeHeight { get; set; }

    /// <summary>Clockwise rotation in degrees.</summary>
    public double RotationDegrees { get; set; }

    /// <summary>The shape's name (wp:docPr/@name); null = automatic.</summary>
    public string? Name { get; set; }

    /// <summary>Alternative text (wp:docPr/@descr).</summary>
    public string? AltText { get; set; }

    /// <summary>Inner margins between the box edge and its text, in points (left, top, right, bottom); null = Word's 7.2 / 3.6 pt.</summary>
    public ImageEffectExtent? Insets { get; set; }

    /// <summary>Vertical placement of the text inside the box; null = top.</summary>
    public TextBoxVerticalAnchor? VerticalAnchor { get; set; }

    /// <summary>The box grows to fit its text (a:spAutoFit).</summary>
    public bool AutoFit { get; set; }

    /// <summary>Fill colour (RRGGBB), or null for no fill.</summary>
    public string? FillHex { get; set; }

    /// <summary>Outline colour (RRGGBB), or null for no outline.</summary>
    public string? LineHex { get; set; } = "000000";

    /// <summary>Outline width in points (null = Word's 0.75pt).</summary>
    public double? LineWidthPt { get; set; }

    /// <summary>The box's paragraphs.</summary>
    public List<Paragraph> Paragraphs { get; } = new();

    /// <summary>Adds a table after the current last paragraph.</summary>
    public DocTable AddTable(int columns)
    {
        var t = new DocTable(columns);
        _tables.Add((Paragraphs.Count > 0 ? Paragraphs[^1] : null, t));
        return t;
    }

    /// <summary>Paragraphs and tables in content order.</summary>
    public IReadOnlyList<object> Blocks
    {
        get
        {
            var list = new List<object>(Paragraphs.Count + _tables.Count);
            if (_tables.Count == 0) { foreach (var p in Paragraphs) list.Add(p); return list; }
            var after = new Dictionary<Paragraph, List<DocTable>>(ReferenceEqualityComparer.Instance);
            foreach (var (a, t) in _tables)
            {
                if (a is null) { list.Add(t); continue; }
                if (!after.TryGetValue(a, out var l)) after[a] = l = new List<DocTable>();
                l.Add(t);
            }
            foreach (var p in Paragraphs)
            {
                list.Add(p);
                if (after.Remove(p, out var ts)) list.AddRange(ts);
            }
            foreach (var ts in after.Values) list.AddRange(ts);
            return list;
        }
    }

    /// <summary>All text (paragraphs joined with newlines).</summary>
    public string GetText() => string.Join("\n", Paragraphs.ConvertAll(p => p.GetText()));

    internal void AppendTable(DocTable t) => _tables.Add((Paragraphs.Count > 0 ? Paragraphs[^1] : null, t));

    /// <summary>Shape properties, style and body properties as loaded (written back while the modelled values are unchanged).</summary>
    internal string? RawSpPr { get; set; }
    internal string? RawStyle { get; set; }
    internal string? RawBodyPr { get; set; }
    private (double, double, double, string?, string?, double?)? _spPrSnapshot;
    private (ImageEffectExtent?, TextBoxVerticalAnchor?, bool)? _bodyPrSnapshot;

    internal void CaptureRawSnapshot()
    {
        _spPrSnapshot = (WidthPt, HeightPt, RotationDegrees, FillHex, LineHex, LineWidthPt);
        _bodyPrSnapshot = (Insets, VerticalAnchor, AutoFit);
    }

    /// <summary>The loaded spPr still describes the box (size, rotation, fill, outline unchanged).</summary>
    internal bool RawSpPrValid => RawSpPr is not null && _spPrSnapshot == (WidthPt, HeightPt, RotationDegrees, FillHex, LineHex, LineWidthPt);

    /// <summary>The loaded bodyPr still describes the box (insets, anchor, auto-fit unchanged).</summary>
    internal bool RawBodyPrValid => RawBodyPr is not null && _bodyPrSnapshot == (Insets, VerticalAnchor, AutoFit);
}
