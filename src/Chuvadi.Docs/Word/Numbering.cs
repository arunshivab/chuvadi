using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Docs.Word;

/// <summary>Number format of a list level (ST_NumberFormat, every value of the schema).</summary>
public enum NumberFormat
{
    /// <summary>1, 2, 3</summary>
    Decimal,
    /// <summary>I, II, III</summary>
    UpperRoman,
    /// <summary>i, ii, iii</summary>
    LowerRoman,
    /// <summary>A, B, C … Z, AA, BB</summary>
    UpperLetter,
    /// <summary>a, b, c … z, aa, bb</summary>
    LowerLetter,
    /// <summary>1st, 2nd, 3rd</summary>
    Ordinal,
    /// <summary>One, Two, Three</summary>
    CardinalText,
    /// <summary>First, Second, Third</summary>
    OrdinalText,
    /// <summary>1, 2 … 9, A, B … F, 10 (hexadecimal)</summary>
    Hex,
    /// <summary>*, †, ‡, §, then doubled: **, ††</summary>
    Chicago,
    IdeographDigital,
    JapaneseCounting,
    Aiueo,
    Iroha,
    /// <summary>Full-width digits: １, ２, ３</summary>
    DecimalFullWidth,
    /// <summary>Half-width digits.</summary>
    DecimalHalfWidth,
    JapaneseLegal,
    JapaneseDigitalTenThousand,
    /// <summary>①, ②, ③ … ⑳, then plain digits.</summary>
    DecimalEnclosedCircle,
    /// <summary>Full-width digits (alternate).</summary>
    DecimalFullWidth2,
    AiueoFullWidth,
    IrohaFullWidth,
    /// <summary>01, 02 … 09, 10</summary>
    DecimalZero,
    /// <summary>A bullet: the level text is shown as-is.</summary>
    Bullet,
    Ganada,
    Chosung,
    /// <summary>⒈, ⒉ … ⒛, then plain digits with a full stop.</summary>
    DecimalEnclosedFullstop,
    /// <summary>⑴, ⑵ … ⒇, then plain digits in parentheses.</summary>
    DecimalEnclosedParen,
    DecimalEnclosedCircleChinese,
    IdeographEnclosedCircle,
    IdeographTraditional,
    IdeographZodiac,
    IdeographZodiacTraditional,
    TaiwaneseCounting,
    IdeographLegalTraditional,
    TaiwaneseCountingThousand,
    TaiwaneseDigital,
    ChineseCounting,
    ChineseLegalSimplified,
    ChineseCountingThousand,
    KoreanDigital,
    KoreanCounting,
    KoreanLegal,
    KoreanDigital2,
    VietnameseCounting,
    RussianLower,
    RussianUpper,
    /// <summary>No number (the level text without the number).</summary>
    None,
    /// <summary>- 1 -, - 2 -</summary>
    NumberInDash,
    Hebrew1,
    Hebrew2,
    ArabicAlpha,
    ArabicAbjad,
    HindiVowels,
    HindiConsonants,
    /// <summary>Devanagari digits: १, २, ३</summary>
    HindiNumbers,
    HindiCounting,
    ThaiLetters,
    /// <summary>Thai digits: ๑, ๒, ๓</summary>
    ThaiNumbers,
    ThaiCounting,
    BahtText,
    DollarText,
    /// <summary>A custom format described by <see cref="NumberingLevel.CustomFormat"/>, e.g. "001, 002, 003, …" or Tamil digits "௧, ௨, ௩, …".</summary>
    Custom,
}

/// <summary>Alignment of a list label within its space (w:lvlJc).</summary>
public enum ListLabelAlignment
{
    Left,
    Center,
    Right,
    /// <summary>Leading edge (right in right-to-left paragraphs).</summary>
    Start,
    /// <summary>Trailing edge.</summary>
    End,
}

/// <summary>What follows a list label before the text (w:suff).</summary>
public enum ListSuffix
{
    /// <summary>A tab to the next tab stop (Word's default).</summary>
    Tab,
    /// <summary>A single space.</summary>
    Space,
    /// <summary>Nothing: the text follows the label directly.</summary>
    Nothing,
}

/// <summary>How many levels a list definition is meant to use (w:multiLevelType).</summary>
public enum ListMultiLevelType
{
    /// <summary>One level only.</summary>
    SingleLevel,
    /// <summary>Nine related levels (outline numbering such as 1, 1.1, 1.1.1).</summary>
    Multilevel,
    /// <summary>Nine independent levels (Word's default for new bullet and number lists).</summary>
    HybridMultilevel,
}

/// <summary>
/// One level of a list definition (w:lvl): start value, number format, label text, alignment,
/// restart rule, suffix, the indent/tab formatting of paragraphs at the level, and the run
/// formatting of the label (e.g. a bullet font). Every member is optional.
/// </summary>
public sealed class NumberingLevel
{
    /// <summary>First number of the level (w:start). Null = 0 (the schema default); Word always writes it.</summary>
    public int? Start { get; set; }

    /// <summary>Number format (w:numFmt). Null = decimal.</summary>
    public NumberFormat? Format { get; set; }

    /// <summary>
    /// For <see cref="NumberFormat.Custom"/>: the sample sequence that defines the format
    /// (w:numFmt/@w:format), e.g. "001, 002, 003, ..." (zero-padded to three digits) or
    /// "௧, ௨, ௩, ..." (Tamil digits). The digits of the first item set the digit set and the width.
    /// </summary>
    public string? CustomFormat { get; set; }

    /// <summary>
    /// Label text (w:lvlText): literal text with %1 … %9 standing for the current numbers of
    /// levels 1–9, e.g. "%1." or "%1.%2." or "(%3)"; for bullets, the bullet character. Null = no label.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>Label alignment (w:lvlJc). Null = left.</summary>
    public ListLabelAlignment? Alignment { get; set; }

    /// <summary>
    /// Restart rule (w:lvlRestart): the level restarts after an item at a level up to this
    /// 1-based level number; 0 = never restart. Null = restart after any higher (shallower) level.
    /// </summary>
    public int? RestartAfterLevel { get; set; }

    /// <summary>Legal numbering (w:isLgl): every number in the label is shown as decimal (e.g. "1.1" under a roman level 1).</summary>
    public bool? IsLegal { get; set; }

    /// <summary>What follows the label (w:suff). Null = tab.</summary>
    public ListSuffix? Suffix { get; set; }

    /// <summary>Paragraph style linked to this level (w:pStyle), e.g. "Heading2" for outline-numbered headings.</summary>
    public string? ParagraphStyleId { get; set; }

    /// <summary>Formatting applied to paragraphs at this level (w:pPr): usually the indents and the number tab.</summary>
    public ParagraphFormat ParagraphFormat { get; } = new();

    /// <summary>Formatting of the label (w:rPr), e.g. a symbol font for the bullet.</summary>
    public TextFormat? RunFormat { get; set; }

    /// <summary>Character style of the label (w:rPr/w:rStyle).</summary>
    public string? RunStyleId { get; set; }

    /// <summary>Picture bullet id (w:lvlPicBulletId), referring to a picture bullet carried from a loaded file.</summary>
    public int? PictureBulletId { get; set; }

    /// <summary>Word's "not yet used" hint for the level (w:lvl/@w:tentative).</summary>
    public bool? Tentative { get; set; }

    /// <summary>Template code (w:lvl/@w:tplc), an 8-digit hex value Word uses to recognise the level.</summary>
    public string? TemplateCode { get; set; }

    /// <summary>A number format token this library does not know, kept verbatim from a loaded file.</summary>
    internal string? RawFormatToken { get; set; }

    /// <summary>w:legacy carried verbatim.</summary>
    internal string? RawLegacyXml { get; set; }

    /// <summary>True when a loaded file stated this level (it is written back even if empty).</summary>
    internal bool Defined { get; set; }

    internal bool IsDefault =>
        Start is null && Format is null && CustomFormat is null && Text is null && Alignment is null
        && RestartAfterLevel is null && IsLegal is null && Suffix is null && ParagraphStyleId is null
        && ParagraphFormat.IsDefault && RunFormat is null && RunStyleId is null && PictureBulletId is null && Tentative is null
        && TemplateCode is null && RawFormatToken is null && RawLegacyXml is null;

    /// <summary>A deep copy.</summary>
    public NumberingLevel Clone()
    {
        var c = new NumberingLevel
        {
            Start = Start, Format = Format, CustomFormat = CustomFormat, Text = Text, Alignment = Alignment,
            RestartAfterLevel = RestartAfterLevel, IsLegal = IsLegal, Suffix = Suffix, ParagraphStyleId = ParagraphStyleId,
            RunFormat = RunFormat, RunStyleId = RunStyleId, PictureBulletId = PictureBulletId, Tentative = Tentative, TemplateCode = TemplateCode,
            RawFormatToken = RawFormatToken, RawLegacyXml = RawLegacyXml, Defined = Defined,
        };
        c.ParagraphFormat.ApplyFrom(ParagraphFormat);
        return c;
    }
}

/// <summary>
/// A list definition (w:abstractNum): up to nine <see cref="NumberingLevel"/>s plus naming and
/// style links. Paragraphs do not use a definition directly but a <see cref="ListInstance"/>
/// of it; several instances may share one definition.
/// </summary>
public sealed class NumberingDefinition
{
    private readonly NumberingLevel[] _levels = new NumberingLevel[9];

    public NumberingDefinition()
    {
        for (int i = 0; i < 9; i++) _levels[i] = new NumberingLevel();
    }

    /// <summary>The abstractNumId. Assigned when the definition is added to <see cref="Document.Numbering"/>; -1 before.</summary>
    public int Id { get; internal set; } = -1;

    /// <summary>Levels 0–8.</summary>
    public IReadOnlyList<NumberingLevel> Levels => _levels;

    /// <summary>Level <paramref name="level"/> (0–8).</summary>
    public NumberingLevel Level(int level)
    {
        if (level < 0 || level > 8) throw new ArgumentOutOfRangeException(nameof(level), "List level must be 0–8.");
        return _levels[level];
    }

    /// <summary>Single, multilevel or hybrid (w:multiLevelType).</summary>
    public ListMultiLevelType? MultiLevelType { get; set; }

    /// <summary>Definition name (w:name).</summary>
    public string? Name { get; set; }

    /// <summary>The numbering style this definition defines (w:styleLink).</summary>
    public string? StyleLink { get; set; }

    /// <summary>The numbering style whose definition this one uses instead of its own levels (w:numStyleLink).</summary>
    public string? NumStyleLink { get; set; }

    /// <summary>Unique list identifier (w:nsid, 8 hex digits).</summary>
    public string? Nsid { get; set; }

    /// <summary>Template code (w:tmpl, 8 hex digits).</summary>
    public string? TemplateCode { get; set; }

    /// <summary>Attributes from other namespaces on w:abstractNum (e.g. w15:restartNumberingAfterBreak), carried verbatim.</summary>
    internal List<RawAttribute> RawAttributes { get; } = new();

    // ---- Presets -------------------------------------------------------------------------

    /// <summary>
    /// A bulleted definition. The symbols repeat over the nine levels (default •, –, ▪ — plain
    /// Unicode in the paragraph font, as Word's built-in list does). Indents step 36pt per
    /// level with an 18pt hanging indent.
    /// </summary>
    public static NumberingDefinition Bulleted(params string[] symbols)
    {
        if (symbols is null || symbols.Length == 0) symbols = new[] { "•", "–", "▪" };
        var d = new NumberingDefinition { MultiLevelType = ListMultiLevelType.HybridMultilevel };
        for (int i = 0; i < 9; i++)
        {
            var symbol = symbols[i % symbols.Length] ?? throw new ArgumentException("Bullet symbols cannot be null.", nameof(symbols));
            Preset(d._levels[i], i, NumberFormat.Bullet, symbol);
        }
        return d;
    }

    /// <summary>
    /// A numbered definition: every level uses <paramref name="format"/> and the label
    /// <paramref name="pattern"/> with {0} standing for the level's own number placeholder —
    /// e.g. "{0}." gives "1.", "{0})" gives "1)" and "({0})" gives "(1)".
    /// </summary>
    public static NumberingDefinition Numbered(NumberFormat format = NumberFormat.Decimal, string pattern = "{0}.")
    {
        if (pattern is null) throw new ArgumentNullException(nameof(pattern));
        if (!pattern.Contains("{0}", StringComparison.Ordinal)) throw new ArgumentException("The pattern must contain {0}.", nameof(pattern));
        var d = new NumberingDefinition { MultiLevelType = ListMultiLevelType.HybridMultilevel };
        for (int i = 0; i < 9; i++) Preset(d._levels[i], i, format, pattern.Replace("{0}", "%" + (i + 1), StringComparison.Ordinal));
        return d;
    }

    /// <summary>
    /// Word-style outline numbering: 1., 1.1., 1.1.1. … (decimal at every level, multilevel).
    /// Link levels to heading styles with <see cref="NumberingLevel.ParagraphStyleId"/> for
    /// numbered headings.
    /// </summary>
    public static NumberingDefinition Outline()
    {
        var d = new NumberingDefinition { MultiLevelType = ListMultiLevelType.Multilevel };
        for (int i = 0; i < 9; i++)
        {
            var text = string.Concat(Enumerable.Range(1, i + 1).Select(n => "%" + n + "."));
            Preset(d._levels[i], i, NumberFormat.Decimal, text);
        }
        return d;
    }

    /// <summary>
    /// A definition with one format and label per level, e.g. Word's classic legal outline:
    /// <c>PerLevel((Decimal, "%1."), (LowerLetter, "%2)"), (LowerRoman, "(%3)"))</c>. Unlisted levels
    /// repeat the pattern of the last one given.
    /// </summary>
    public static NumberingDefinition PerLevel(params (NumberFormat Format, string Text)[] levels)
    {
        if (levels is null || levels.Length == 0) throw new ArgumentException("Give at least one level.", nameof(levels));
        if (levels.Length > 9) throw new ArgumentException("A list has at most nine levels.", nameof(levels));
        var d = new NumberingDefinition { MultiLevelType = ListMultiLevelType.Multilevel };
        for (int i = 0; i < 9; i++)
        {
            var (f, t) = levels[Math.Min(i, levels.Length - 1)];
            if (t is null) throw new ArgumentException("Level text cannot be null.", nameof(levels));
            Preset(d._levels[i], i, f, t);
        }
        return d;
    }

    private static void Preset(NumberingLevel l, int i, NumberFormat format, string text)
    {
        l.Start = 1;
        l.Format = format;
        l.Text = text;
        l.Alignment = ListLabelAlignment.Left;
        l.ParagraphFormat.IndentLeftPt = 36 * (i + 1);
        l.ParagraphFormat.HangingIndentPt = 18;
    }

    /// <summary>
    /// A deep copy (not added to any document; <see cref="Id"/> is -1). <see cref="Nsid"/> is not
    /// copied — Word uses it to tell lists apart, so a copy gets none (Word assigns one).
    /// </summary>
    public NumberingDefinition Clone()
    {
        var c = new NumberingDefinition
        {
            MultiLevelType = MultiLevelType, Name = Name, StyleLink = StyleLink, NumStyleLink = NumStyleLink,
            TemplateCode = TemplateCode,
        };
        c.RawAttributes.AddRange(RawAttributes);
        for (int i = 0; i < 9; i++) c._levels[i] = _levels[i].Clone();
        return c;
    }
}

/// <summary>A per-instance change to one level of a list (w:lvlOverride).</summary>
public sealed class LevelOverride
{
    /// <summary>Restart the level at this number where the instance is first used (w:startOverride).</summary>
    public int? StartOverride { get; set; }

    /// <summary>A complete replacement for the level (w:lvl inside w:lvlOverride).</summary>
    public NumberingLevel? Level { get; set; }
}

/// <summary>
/// A list as paragraphs use it (w:num): a <see cref="NumberingDefinition"/> plus optional
/// per-level overrides. Instances of the same definition continue each other's numbering
/// (Word's behaviour) unless an instance restarts a level with <see cref="LevelOverride.StartOverride"/>.
/// </summary>
public sealed class ListInstance
{
    private readonly SortedDictionary<int, LevelOverride> _overrides = new();

    internal ListInstance(int id, NumberingDefinition definition)
    {
        Id = id;
        Definition = definition;
    }

    /// <summary>The numId paragraphs refer to.</summary>
    public int Id { get; internal set; }

    /// <summary>The definition this list uses.</summary>
    public NumberingDefinition Definition { get; }

    /// <summary>Level overrides by level.</summary>
    public IReadOnlyDictionary<int, LevelOverride> Overrides => _overrides;

    /// <summary>The override for <paramref name="level"/> (0–8), created when absent.</summary>
    public LevelOverride Override(int level)
    {
        if (level < 0 || level > 8) throw new ArgumentOutOfRangeException(nameof(level), "List level must be 0–8.");
        if (!_overrides.TryGetValue(level, out var o)) _overrides[level] = o = new LevelOverride();
        return o;
    }

    /// <summary>The override of <paramref name="level"/> when one exists (no side effect).</summary>
    public bool TryGetOverride(int level, out LevelOverride levelOverride)
    {
        if (_overrides.TryGetValue(level, out var o)) { levelOverride = o; return true; }
        levelOverride = null!;
        return false;
    }

    /// <summary>Removes the override of <paramref name="level"/>.</summary>
    public bool RemoveOverride(int level) => _overrides.Remove(level);

    /// <summary>Attributes from other namespaces on w:num (e.g. w16cid:durableId), carried verbatim.</summary>
    internal List<RawAttribute> RawAttributes { get; } = new();

    /// <summary>
    /// The level as this instance shows it: an override's replacement level, else the
    /// definition's level (following <see cref="NumberingDefinition.NumStyleLink"/> is done by
    /// <see cref="Document.GetListLabels"/>).
    /// </summary>
    public NumberingLevel EffectiveLevel(int level)
    {
        if (level < 0 || level > 8) throw new ArgumentOutOfRangeException(nameof(level), "List level must be 0–8.");
        return _overrides.TryGetValue(level, out var o) && o.Level is { } l ? l : Definition.Levels[level];
    }
}

/// <summary>
/// The document's lists (numbering.xml): definitions and the list instances paragraphs use.
/// Only lists from this collection number paragraphs; a list removed from it (or belonging to
/// another document) numbers nothing — no label, no indent, and no w:numPr on save.
/// </summary>
public sealed class NumberingCollection
{
    private readonly List<NumberingDefinition> _definitions = new();
    private readonly List<ListInstance> _lists = new();
    private readonly Dictionary<int, ListInstance> _listById = new();
    private readonly HashSet<NumberingDefinition> _definitionSet = new(ReferenceEqualityComparer.Instance);
    private readonly Document? _owner;

    internal NumberingCollection(Document? owner = null) => _owner = owner;

    /// <summary>All list definitions (w:abstractNum).</summary>
    public IReadOnlyList<NumberingDefinition> Definitions => _definitions;

    /// <summary>All list instances (w:num).</summary>
    public IReadOnlyList<ListInstance> Lists => _lists;

    /// <summary>Picture bullets (w:numPicBullet) carried verbatim from a loaded file.</summary>
    internal List<string> RawPictureBullets { get; } = new();

    /// <summary>The images picture bullets refer to (numbering.xml relationships), carried from a loaded file.</summary>
    internal List<(string RelId, string RelType, byte[] Bytes, string ContentType)> RawPictureParts { get; } = new();

    /// <summary>w:numIdMacAtCleanup carried from a loaded file.</summary>
    internal int? NumIdMacAtCleanup { get; set; }

    /// <summary>True when <paramref name="list"/> belongs to this collection.</summary>
    public bool Contains(ListInstance list) => list is not null && _listById.TryGetValue(list.Id, out var l) && ReferenceEquals(l, list);

    /// <summary>True when <paramref name="definition"/> belongs to this collection.</summary>
    public bool Contains(NumberingDefinition definition) => definition is not null && _definitionSet.Contains(definition);

    /// <summary>Adds a definition and assigns its <see cref="NumberingDefinition.Id"/>. Adding the same definition twice is a no-op.</summary>
    public NumberingDefinition Add(NumberingDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (_definitionSet.Contains(definition)) return definition;
        if (definition.Id >= 0) throw new ArgumentException("The definition already belongs to another document; add a Clone().", nameof(definition));
        definition.Id = FreeIds(_definitions.Select(d => d.Id), 0, 1)[0];
        _definitions.Add(definition);
        _definitionSet.Add(definition);
        return definition;
    }

    /// <summary>Creates a list instance of <paramref name="definition"/> (adding the definition when needed).</summary>
    public ListInstance AddList(NumberingDefinition definition)
    {
        Add(definition);
        var list = new ListInstance(FreeIds(_listById.Keys, 1, 1)[0], definition);
        _lists.Add(list);
        _listById[list.Id] = list;
        return list;
    }

    /// <summary>A new bulleted list (see <see cref="NumberingDefinition.Bulleted"/>).</summary>
    public ListInstance AddBulletList(params string[] symbols) => AddList(NumberingDefinition.Bulleted(symbols));

    /// <summary>A new numbered list (see <see cref="NumberingDefinition.Numbered"/>).</summary>
    public ListInstance AddNumberedList(NumberFormat format = NumberFormat.Decimal, string pattern = "{0}.")
        => AddList(NumberingDefinition.Numbered(format, pattern));

    /// <summary>A new outline-numbered list: 1., 1.1., 1.1.1.</summary>
    public ListInstance AddOutlineList() => AddList(NumberingDefinition.Outline());

    /// <summary>
    /// A new instance of <paramref name="list"/>'s definition that restarts <paramref name="level"/>
    /// at <paramref name="startAt"/> (null = the level's own start, following numStyleLink) where
    /// it is first used — Word's "Restart at 1". The original list is unchanged.
    /// </summary>
    public ListInstance Restart(ListInstance list, int level = 0, int? startAt = null)
    {
        if (list is null) throw new ArgumentNullException(nameof(list));
        if (level < 0 || level > 8) throw new ArgumentOutOfRangeException(nameof(level), "List level must be 0–8.");
        if (!Contains(list))
            throw new ArgumentException("The list is not in this document's Numbering (another document's list, a removed list, or a built-in default list behind AddBullet/AddNumbered).", nameof(list));
        int startValue;
        if (startAt is int given) startValue = given;
        else if (list.TryGetOverride(level, out var lo) && lo.Level is { } ol) startValue = ol.Start ?? 0;
        else
        {
            var def = _owner is null ? list.Definition : Internal.ListNumbering.ResolveDefinition(_owner, list.Definition);
            startValue = def.Levels[level].Start ?? 0;
        }
        var restarted = AddList(list.Definition);
        foreach (var (lvl, o) in list.Overrides)
            if (o.Level is not null) restarted.Override(lvl).Level = o.Level.Clone();
        restarted.Override(level).StartOverride = startValue;
        return restarted;
    }

    /// <summary>The list with numId <paramref name="id"/>.</summary>
    public bool TryGetList(int id, out ListInstance list)
    {
        if (_listById.TryGetValue(id, out var l)) { list = l; return true; }
        list = null!;
        return false;
    }

    /// <summary>The definition with abstractNumId <paramref name="id"/>.</summary>
    public bool TryGetDefinition(int id, out NumberingDefinition definition)
    {
        foreach (var d in _definitions) if (d.Id == id) { definition = d; return true; }
        definition = null!;
        return false;
    }

    /// <summary>
    /// Removes a list instance. Paragraphs and styles still referring to it are no longer
    /// numbered (no label, and no w:numPr on save).
    /// </summary>
    public bool Remove(ListInstance list)
    {
        if (!Contains(list)) return false;
        _listById.Remove(list.Id);
        return _lists.Remove(list);
    }

    /// <summary>Removes a definition (it can be added again later). Throws when a list instance still uses it.</summary>
    public bool Remove(NumberingDefinition definition)
    {
        if (!Contains(definition)) return false;
        if (_lists.Any(l => ReferenceEquals(l.Definition, definition)))
            throw new InvalidOperationException("A list instance still uses this definition; remove the instances first.");
        _definitionSet.Remove(definition);
        _definitions.Remove(definition);
        definition.Id = -1;
        return true;
    }

    /// <summary><paramref name="count"/> unused ids ≥ <paramref name="min"/>: above the highest used one, or the lowest free ones when that would overflow.</summary>
    internal static int[] FreeIds(IEnumerable<int> used, int min, int count)
    {
        var set = new HashSet<int>(used);
        var result = new int[count];
        long max = set.Count == 0 ? min - 1 : Math.Max(set.Max(), min - 1);
        if (max + count <= int.MaxValue)
        {
            for (int i = 0; i < count; i++) result[i] = (int)(max + 1 + i);
            return result;
        }
        int n = 0;
        for (int id = min; n < count; id++) if (!set.Contains(id)) result[n++] = id;
        return result;
    }

    internal int[] FreeDefinitionIds(int count) => FreeIds(_definitions.Select(d => d.Id), 0, count);
    internal int[] FreeListIds(int count) => FreeIds(_listById.Keys, 1, count);

    /// <summary>Loader: adds a definition with its file id.</summary>
    internal void AddLoaded(NumberingDefinition d, int id)
    {
        d.Id = id;
        _definitions.Add(d);
        _definitionSet.Add(d);
    }

    /// <summary>Loader: adds an instance with its file id (ids are unique; the loader skips duplicates).</summary>
    internal ListInstance AddLoadedList(int id, NumberingDefinition d)
    {
        var l = new ListInstance(id, d);
        _lists.Add(l);
        _listById[id] = l;
        return l;
    }

    internal bool IsEmpty => _definitions.Count == 0 && _lists.Count == 0 && RawPictureBullets.Count == 0;

    /// <summary>
    /// Document.Append: moves every definition and list of <paramref name="source"/> (a throwaway
    /// copy) into this collection under new ids; the objects themselves move, so paragraphs and
    /// styles that refer to them keep working.
    /// </summary>
    internal void AdoptAll(NumberingCollection source)
    {
        var defIds = FreeDefinitionIds(source._definitions.Count);
        for (int i = 0; i < source._definitions.Count; i++)
        {
            var d = source._definitions[i];
            d.Id = defIds[i];
            _definitions.Add(d);
            _definitionSet.Add(d);
        }
        var listIds = FreeListIds(source._lists.Count);
        for (int i = 0; i < source._lists.Count; i++)
        {
            var l = source._lists[i];
            l.Id = listIds[i];
            _lists.Add(l);
            _listById[l.Id] = l;
        }
        if (RawPictureBullets.Count == 0 && source.RawPictureBullets.Count > 0)
        {
            RawPictureBullets.AddRange(source.RawPictureBullets);
            RawPictureParts.AddRange(source.RawPictureParts);
        }
        else if (source.RawPictureBullets.Count > 0)
        {
            // Picture-bullet ids would clash: the appended lists fall back to their text bullets.
            foreach (var d in source._definitions)
                foreach (var level in d.Levels) level.PictureBulletId = null;
        }
        source._definitions.Clear();
        source._definitionSet.Clear();
        source._lists.Clear();
        source._listById.Clear();
    }
}

/// <summary>An attribute from a namespace this library does not model, carried verbatim.</summary>
internal readonly record struct RawAttribute(string Prefix, string Namespace, string LocalName, string Value);

/// <summary>The computed label of one list paragraph, as Word would show it.</summary>
/// <param name="Text">The label text, e.g. "1.", "1.2.", "iv)", "•"; empty for <see cref="NumberFormat.None"/> without literal text.</param>
/// <param name="Number">The paragraph's own counter value at its level.</param>
/// <param name="Level">The 0-based list level.</param>
/// <param name="List">The list instance that numbers the paragraph (directly or through its paragraph style). For <see cref="Document.AddBullet"/> / <see cref="Document.AddNumbered"/> items this is a built-in default list (Id -1 / -2) that is not in <see cref="Document.Numbering"/>; its numId is assigned at save.</param>
/// <param name="LevelDefinition">The effective level definition (override, numStyleLink and definition resolved).</param>
/// <param name="Suffix">What follows the label.</param>
/// <param name="Format">The label's effective run formatting (paragraph-mark formatting under the level's run formatting).</param>
public sealed record ListLabel(string Text, int Number, int Level, ListInstance List, NumberingLevel LevelDefinition,
    ListSuffix Suffix, TextFormat Format);
