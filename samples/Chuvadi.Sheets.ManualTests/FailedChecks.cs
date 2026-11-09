using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Collects the checks that failed in one manual-test group, so the group's summary exception
/// names them. xUnit does not show console output, so without this a CI log only says how many
/// checks failed, not which.
/// </summary>
internal sealed class FailedChecks
{
    /// <summary>How many failed checks the summary lists by name.</summary>
    public const int MaxListed = 5;

    /// <summary>The longest message kept per failed check, in characters.</summary>
    public const int MaxMessageLength = 300;

    private readonly List<string> _items = new();
    private int _count;

    /// <summary>Records a check that threw.</summary>
    public void Add(string label, Exception ex) => Add(label, $"{ex.GetType().Name}: {ex.Message}");

    /// <summary>Records a check that failed with a message.</summary>
    public void Add(string label, string message)
    {
        _count++;
        if (_items.Count >= MaxListed) return;
        var text = message.Replace('\r', ' ').Replace('\n', ' ');
        if (text.Length > MaxMessageLength) text = text.Substring(0, MaxMessageLength) + "…";
        _items.Add($"[{label}] {text}");
    }

    /// <summary>
    /// The group's summary message: <paramref name="summary"/> as before, followed by the failed
    /// checks. With nothing recorded it is returned unchanged.
    /// </summary>
    public string Summary(string summary)
    {
        if (_items.Count == 0) return summary;
        var sb = new StringBuilder(summary.TrimEnd('.'));
        sb.Append(": ").Append(string.Join("; ", _items));
        if (_count > _items.Count) sb.Append($"; and {_count - _items.Count} more");
        return sb.Append('.').ToString();
    }
}
