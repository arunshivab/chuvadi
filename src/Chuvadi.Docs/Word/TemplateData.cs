using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Chuvadi.Docs.Word;

/// <summary>
/// The data a <see cref="DocxTemplate"/> is filled with: text values, formatted values, images,
/// flags, nested objects and repeating collections. Lookups fall back to the enclosing scope,
/// so inside a repeated row both the item's keys and the outer keys resolve.
///
/// <code>
/// var data = new TemplateData()
///     .Set("Patient", "R. Kumar")
///     .SetFlag("Urgent", true);
/// foreach (var t in tests)
///     data.AddItem("Tests").Set("Name", t.Name).Set("Result", t.Value);
/// DocxTemplate.Fill("report-template.docx", "report.docx", data);
/// </code>
///
/// Template syntax: <c>{{Name}}</c> a value; <c>{{#Items}}</c> … <c>{{/Items}}</c> repeats its
/// content per item (table rows when the markers are in table rows, paragraphs and tables when
/// the markers stand in their own paragraphs, or run text within one paragraph);
/// <c>{{?Flag}}</c> … <c>{{/Flag}}</c> keeps its content only when the value is "truthy" and
/// <c>{{^Flag}}</c> … <c>{{/Flag}}</c> only when it is not; <c>{{Customer.Name}}</c> reads a nested
/// object; <c>{{.}}</c> is the current item of a list of plain values.
/// </summary>
public sealed class TemplateData
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>The enclosing scope (the data an item belongs to), or null at the root.</summary>
    public TemplateData? Parent { get; private set; }

    /// <summary>The keys set in this scope (not the enclosing ones).</summary>
    public IReadOnlyCollection<string> Keys => _values.Keys;

    /// <summary>Sets a text value. Line feeds become line breaks and tabs become tabs in the output.</summary>
    [OverloadResolutionPriority(1)] // Set(key, null) clears to an empty text value instead of being ambiguous with Set(string, TemplateValue).
    public TemplateData Set(string key, string? value)
    {
        _values[CheckKey(key)] = value;
        return this;
    }

    /// <summary>Sets a formatted value: runs whose formatting is layered over the placeholder's own.</summary>
    public TemplateData Set(string key, TemplateValue value)
    {
        _values[CheckKey(key)] = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    /// <summary>Sets a flag for <c>{{?Key}}</c> / <c>{{^Key}}</c> blocks.</summary>
    public TemplateData SetFlag(string key, bool value)
    {
        _values[CheckKey(key)] = value;
        return this;
    }

    /// <summary>
    /// Sets an image. Two ways to use it in a template:
    /// <list type="bullet">
    /// <item><c>{{Key}}</c> in text is replaced by the image (inline, or floating per the
    /// <see cref="ImageSpec"/>), keeping nothing of the placeholder's text.</item>
    /// <item>A picture already in the template whose alt text (Word: Picture Format → Alt Text,
    /// stored as wp:docPr/@descr) equals Key gets its picture bytes swapped, keeping the template
    /// picture's size, position, crop and wrap. Only root-level images do this, and the new image
    /// should be the same format as the original. Use <see cref="ImageSpec.AltText"/> to give an
    /// inserted image its own alt text.</item>
    /// </list>
    /// </summary>
    public TemplateData SetImage(string key, ImageSpec image)
    {
        _values[CheckKey(key)] = image ?? throw new ArgumentNullException(nameof(image));
        return this;
    }

    /// <summary>Sets a nested object, read as <c>{{Key.Field}}</c> or entered with <c>{{#Key}}</c> … <c>{{/Key}}</c>.</summary>
    public TemplateData SetObject(string key, TemplateData value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        CheckKey(key);
        CheckNoCycle(value);
        value.Parent = this;
        _values[key] = value;
        return this;
    }

    /// <summary>Appends a new item to the collection <paramref name="collection"/> and returns it (its scope falls back to this one).</summary>
    public TemplateData AddItem(string collection)
    {
        var list = ListOf(collection);
        var item = new TemplateData { Parent = this };
        list.Add(item);
        return item;
    }

    /// <summary>Sets the items of a collection (replacing any). Each item's scope falls back to this one.</summary>
    [OverloadResolutionPriority(1)] // SetItems(name, null) binds here (ArgumentNullException) instead of being ambiguous.
    public TemplateData SetItems(string collection, IEnumerable<TemplateData> items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        CheckKey(collection);
        var list = new List<TemplateData>();
        foreach (var item in items)
        {
            if (item is null) throw new ArgumentException("Items cannot be null.", nameof(items));
            CheckNoCycle(item);
            list.Add(item);
        }
        foreach (var item in list) item.Parent = this;
        _values[collection] = list;
        return this;
    }

    /// <summary>Sets a collection of plain values; inside the block each value is <c>{{.}}</c>.</summary>
    public TemplateData SetItems(string collection, IEnumerable<string?> values)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        return SetItems(collection, values.Select(v => new TemplateData().Set(".", v)));
    }

    /// <summary>Removes a key from this scope.</summary>
    public bool Remove(string key) => _values.Remove(key);

    /// <summary>The value of <paramref name="key"/> in this scope only (string, <see cref="TemplateValue"/>, bool, <see cref="ImageSpec"/>, <see cref="TemplateData"/> or a list of items), or null.</summary>
    public object? this[string key] => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>Builds data from the flat dictionaries of the classic <see cref="DocxTemplate.Fill(string, string, IReadOnlyDictionary{string, string}?, IReadOnlyDictionary{string, ImageSpec}?, string?, EncryptionOptions?)"/> overloads.</summary>
    public static TemplateData From(IReadOnlyDictionary<string, string>? textValues, IReadOnlyDictionary<string, ImageSpec>? imageValues = null)
    {
        var d = new TemplateData();
        // Images first, so a key in both dictionaries is filled with its text (the classic behaviour).
        if (imageValues is not null) foreach (var (k, v) in imageValues) if (v is not null) d._values[k] = v;
        if (textValues is not null) foreach (var (k, v) in textValues) d._values[k] = v;
        return d;
    }

    private List<TemplateData> ListOf(string collection)
    {
        if (_values.TryGetValue(CheckKey(collection), out var v) && v is List<TemplateData> existing) return existing;
        var list = new List<TemplateData>();
        _values[collection] = list;
        return list;
    }

    private static string CheckKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key required.", nameof(key));
        if (key.IndexOfAny(new[] { '{', '}' }) >= 0) throw new ArgumentException("Keys cannot contain braces.", nameof(key));
        return key;
    }

    /// <summary>Deepest chain of enclosing scopes walked (guards against a corrupted parent chain).</summary>
    internal const int MaxScopeDepth = 10_000;

    /// <summary>Rejects a value that is this data or one of its enclosing scopes (it would make the scopes circular).</summary>
    private void CheckNoCycle(TemplateData value)
    {
        int guard = 0;
        for (var s = this; s is not null; s = s.Parent)
        {
            if (ReferenceEquals(s, value))
                throw new ArgumentException("A template data cannot contain itself or one of its enclosing scopes.", nameof(value));
            if (++guard > MaxScopeDepth) throw new InvalidOperationException("Template data nested too deeply.");
        }
    }

    /// <summary>
    /// Resolves a name in this scope only: the exact key first (keys may contain dots), then
    /// "A.B.C" as A in this scope and B, C inside the nested objects.
    /// </summary>
    internal bool TryResolveOwn(string path, out object? value)
    {
        if (_values.TryGetValue(path, out value)) return true;
        value = null;
        if (path == "." || path.IndexOf('.') <= 0) return false;
        var parts = path.Split('.');
        if (!_values.TryGetValue(parts[0], out var current)) return false;
        for (int i = 1; i < parts.Length; i++)
            if (current is not TemplateData child || !child._values.TryGetValue(parts[i], out current)) return false;
        value = current;
        return true;
    }

    /// <summary>
    /// Resolves a placeholder name: "." is this item's own value; "A.B.C" looks A up in this scope
    /// or an enclosing one, then B and C inside the nested objects. Null when not found.
    /// </summary>
    internal bool TryResolve(string path, out object? value)
    {
        // The exact key first (keys may themselves contain dots), innermost scope outwards.
        int guard = 0;
        for (var s = this; s is not null && guard++ < MaxScopeDepth; s = s.Parent)
            if (s.TryResolveOwn(path, out value)) return true;
        value = null;
        return false;
    }
}

/// <summary>
/// A formatted template value: one or more runs of text, each with formatting layered over the
/// placeholder's own (e.g. a red bold "HIGH" in a result cell keeps the cell's font and size).
/// </summary>
public sealed class TemplateValue
{
    private readonly List<(string Text, TextFormat Format)> _runs = new();

    /// <summary>The runs in order.</summary>
    public IReadOnlyList<(string Text, TextFormat Format)> Runs => _runs;

    /// <summary>A value of one formatted run.</summary>
    public static TemplateValue Formatted(string text, TextFormat format) => new TemplateValue().Add(text, format);

    /// <summary>Appends a run; null format = the placeholder's formatting unchanged.</summary>
    public TemplateValue Add(string text, TextFormat? format = null)
    {
        _runs.Add((text ?? throw new ArgumentNullException(nameof(text)), format ?? TextFormat.None));
        return this;
    }

    /// <summary>The plain text of all runs.</summary>
    public override string ToString() => string.Concat(_runs.Select(r => r.Text));
}

/// <summary>What a placeholder with no value becomes.</summary>
public enum TemplateMissingValue
{
    /// <summary>Left as <c>{{Name}}</c>, visible during review (the classic behaviour).</summary>
    LeaveTag,
    /// <summary>Removed (empty text).</summary>
    Empty,
}

/// <summary>Options for <see cref="DocxTemplate.Fill(Stream, Stream, TemplateData, TemplateOptions?, System.Threading.CancellationToken)"/>.</summary>
public sealed class TemplateOptions
{
    /// <summary>Password of an encrypted template.</summary>
    public string? Password { get; init; }

    /// <summary>When set, the output is saved encrypted.</summary>
    public EncryptionOptions? OutputEncryption { get; init; }

    /// <summary>Resource limits for reading the template; null = <see cref="DocxReaderOptions"/> defaults.</summary>
    public DocxReaderOptions? ReadOptions { get; init; }

    /// <summary>
    /// Strict mode: a placeholder with no value, or a block marker without its partner or in a
    /// place where no block can be formed, throws <see cref="DocxTemplateException"/> listing
    /// every problem. Off by default (such markers are left as text).
    /// </summary>
    public bool Strict { get; init; }

    /// <summary>What a value placeholder with no value becomes when not strict.</summary>
    public TemplateMissingValue MissingValue { get; init; } = TemplateMissingValue.LeaveTag;

    /// <summary>Deepest nesting of blocks expanded (guards against runaway templates). Default 32.</summary>
    public int MaxBlockDepth { get; init; } = 32;

    /// <summary>Most repeated items generated in one fill, all blocks together. Default 1,000,000.</summary>
    public int MaxGeneratedItems { get; init; } = 1_000_000;

    /// <summary>Most XML nodes generated by repetition in one fill, all blocks together. Default 50,000,000.</summary>
    public long MaxGeneratedElements { get; init; } = 50_000_000;

    internal void Validate()
    {
        if (MaxBlockDepth < 1) throw new ArgumentOutOfRangeException(nameof(MaxBlockDepth), MaxBlockDepth, "Must be at least 1.");
        if (MaxGeneratedItems < 0) throw new ArgumentOutOfRangeException(nameof(MaxGeneratedItems), MaxGeneratedItems, "Cannot be negative.");
        if (MaxGeneratedElements < 0) throw new ArgumentOutOfRangeException(nameof(MaxGeneratedElements), MaxGeneratedElements, "Cannot be negative.");
        if (!Enum.IsDefined(typeof(TemplateMissingValue), MissingValue))
            throw new ArgumentOutOfRangeException(nameof(MissingValue), MissingValue, "Unknown value.");
    }
}

/// <summary>Kind of a template placeholder.</summary>
public enum TemplatePlaceholderKind
{
    /// <summary><c>{{Name}}</c></summary>
    Value,
    /// <summary><c>{{#Name}}</c> … <c>{{/Name}}</c></summary>
    Repeat,
    /// <summary><c>{{?Name}}</c> … <c>{{/Name}}</c></summary>
    Condition,
    /// <summary><c>{{^Name}}</c> … <c>{{/Name}}</c></summary>
    InvertedCondition,
}

/// <summary>Where a block expands (decided by the positions of its markers).</summary>
public enum TemplateBlockScope
{
    /// <summary>Not a block (a value placeholder), or the markers cannot form one.</summary>
    None,
    /// <summary>Run text inside one paragraph.</summary>
    Inline,
    /// <summary>Paragraphs and tables between marker paragraphs.</summary>
    Blocks,
    /// <summary>The table rows from the opening marker's row to the closing marker's row.</summary>
    TableRows,
}

/// <summary>A placeholder found in a template (see <see cref="DocxTemplate.GetPlaceholders(string, string?, DocxReaderOptions?)"/>).</summary>
/// <param name="Name">The name as written (e.g. "Customer.Name", "Items", ".").</param>
/// <param name="Kind">Value, repeat or condition.</param>
/// <param name="Path">Enclosing block names, outermost first (empty at the top level).</param>
/// <param name="Part">The package part, e.g. "word/document.xml" or "word/header1.xml".</param>
/// <param name="Scope">For blocks: inline, paragraphs/tables or table rows.</param>
public sealed record TemplatePlaceholder(string Name, TemplatePlaceholderKind Kind, IReadOnlyList<string> Path, string Part,
    TemplateBlockScope Scope);

/// <summary>Thrown by strict template filling (and for runaway templates) with every problem found.</summary>
public sealed class DocxTemplateException : Exception
{
    public DocxTemplateException(IReadOnlyList<string> problems)
        : base("Template problems:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Take(50))
               + (problems.Count > 50 ? Environment.NewLine + $"… and {problems.Count - 50} more" : ""))
    {
        Problems = problems;
    }

    /// <summary>Every problem, e.g. "word/document.xml: no value for {{Total}}".</summary>
    public IReadOnlyList<string> Problems { get; }
}
