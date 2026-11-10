namespace Chuvadi.Text.Shaping;

/// <summary>What a shaping diagnostic reports (D-246). Each kind is reported at most once per shaping call.</summary>
public enum ShapingDiagnosticKind
{
    /// <summary>A character has no glyph in the font; glyph 0 (.notdef) was used.</summary>
    MissingGlyph = 1,

    /// <summary>
    /// The font has layout tables but none for the requested script, so its default script system (or none) was used.
    /// </summary>
    ScriptNotInFont = 2,
}

/// <summary>A note about a shaping call that the caller may want to show or log (D-246).</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Message">A readable description, naming the first character or script concerned.</param>
public readonly record struct ShapingDiagnostic(ShapingDiagnosticKind Kind, string Message);
