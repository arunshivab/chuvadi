using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>The 'maxp' (maximum profile) table. Version 0.5 has only the glyph count; version 1.0 adds the TrueType limits.</summary>
public sealed class MaxpTable
{
    // TRACE R-OT-0007
    internal MaxpTable(ReadOnlySpan<byte> s)
    {
        VersionRaw = BigEndian.U32(s, 0);
        NumGlyphs = BigEndian.U16(s, 4);
        HasVersion1Fields = VersionRaw == 0x00010000u && s.Length >= 32;
        if (HasVersion1Fields)
        {
            MaxPoints = BigEndian.U16(s, 6);
            MaxContours = BigEndian.U16(s, 8);
            MaxCompositePoints = BigEndian.U16(s, 10);
            MaxCompositeContours = BigEndian.U16(s, 12);
            MaxZones = BigEndian.U16(s, 14);
            MaxTwilightPoints = BigEndian.U16(s, 16);
            MaxStorage = BigEndian.U16(s, 18);
            MaxFunctionDefs = BigEndian.U16(s, 20);
            MaxInstructionDefs = BigEndian.U16(s, 22);
            MaxStackElements = BigEndian.U16(s, 24);
            MaxSizeOfInstructions = BigEndian.U16(s, 26);
            MaxComponentElements = BigEndian.U16(s, 28);
            MaxComponentDepth = BigEndian.U16(s, 30);
        }
    }

    /// <summary>Gets the version as stored (0x00005000 or 0x00010000).</summary>
    public uint VersionRaw { get; }

    /// <summary>Gets numGlyphs.</summary>
    public int NumGlyphs { get; }

    /// <summary>Gets whether the version 1.0 fields below were present.</summary>
    public bool HasVersion1Fields { get; }

    /// <summary>Gets maxPoints (version 1.0; 0 otherwise).</summary>
    public int MaxPoints { get; }

    /// <summary>Gets maxContours (version 1.0; 0 otherwise).</summary>
    public int MaxContours { get; }

    /// <summary>Gets maxCompositePoints (version 1.0; 0 otherwise).</summary>
    public int MaxCompositePoints { get; }

    /// <summary>Gets maxCompositeContours (version 1.0; 0 otherwise).</summary>
    public int MaxCompositeContours { get; }

    /// <summary>Gets maxZones (version 1.0; 0 otherwise).</summary>
    public int MaxZones { get; }

    /// <summary>Gets maxTwilightPoints (version 1.0; 0 otherwise).</summary>
    public int MaxTwilightPoints { get; }

    /// <summary>Gets maxStorage (version 1.0; 0 otherwise).</summary>
    public int MaxStorage { get; }

    /// <summary>Gets maxFunctionDefs (version 1.0; 0 otherwise).</summary>
    public int MaxFunctionDefs { get; }

    /// <summary>Gets maxInstructionDefs (version 1.0; 0 otherwise).</summary>
    public int MaxInstructionDefs { get; }

    /// <summary>Gets maxStackElements (version 1.0; 0 otherwise).</summary>
    public int MaxStackElements { get; }

    /// <summary>Gets maxSizeOfInstructions (version 1.0; 0 otherwise).</summary>
    public int MaxSizeOfInstructions { get; }

    /// <summary>Gets maxComponentElements (version 1.0; 0 otherwise).</summary>
    public int MaxComponentElements { get; }

    /// <summary>Gets maxComponentDepth (version 1.0; 0 otherwise).</summary>
    public int MaxComponentDepth { get; }
}

/// <summary>The 'hhea' (horizontal header) table.</summary>
public sealed class HheaTable
{
    // TRACE R-OT-0010
    internal HheaTable(ReadOnlySpan<byte> s)
    {
        MajorVersion = BigEndian.U16(s, 0);
        MinorVersion = BigEndian.U16(s, 2);
        Ascender = BigEndian.I16(s, 4);
        Descender = BigEndian.I16(s, 6);
        LineGap = BigEndian.I16(s, 8);
        AdvanceWidthMax = BigEndian.U16(s, 10);
        MinLeftSideBearing = BigEndian.I16(s, 12);
        MinRightSideBearing = BigEndian.I16(s, 14);
        XMaxExtent = BigEndian.I16(s, 16);
        CaretSlopeRise = BigEndian.I16(s, 18);
        CaretSlopeRun = BigEndian.I16(s, 20);
        CaretOffset = BigEndian.I16(s, 22);
        MetricDataFormat = BigEndian.I16(s, 32);
        NumberOfHMetrics = BigEndian.U16(s, 34);
    }

    /// <summary>Gets the major version (1).</summary>
    public int MajorVersion { get; }

    /// <summary>Gets the minor version (0).</summary>
    public int MinorVersion { get; }

    /// <summary>Gets ascender.</summary>
    public int Ascender { get; }

    /// <summary>Gets descender.</summary>
    public int Descender { get; }

    /// <summary>Gets lineGap.</summary>
    public int LineGap { get; }

    /// <summary>Gets advanceWidthMax.</summary>
    public int AdvanceWidthMax { get; }

    /// <summary>Gets minLeftSideBearing.</summary>
    public int MinLeftSideBearing { get; }

    /// <summary>Gets minRightSideBearing.</summary>
    public int MinRightSideBearing { get; }

    /// <summary>Gets xMaxExtent.</summary>
    public int XMaxExtent { get; }

    /// <summary>Gets caretSlopeRise.</summary>
    public int CaretSlopeRise { get; }

    /// <summary>Gets caretSlopeRun.</summary>
    public int CaretSlopeRun { get; }

    /// <summary>Gets caretOffset.</summary>
    public int CaretOffset { get; }

    /// <summary>Gets metricDataFormat (0).</summary>
    public int MetricDataFormat { get; }

    /// <summary>Gets numberOfHMetrics.</summary>
    public int NumberOfHMetrics { get; }
}
