using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// numbering.xml ⇄ <see cref="NumberingCollection"/>: w:numPicBullet (verbatim), w:abstractNum
/// (CT_AbstractNum: nsid, multiLevelType, tmpl, name, styleLink, numStyleLink, lvl*), w:num
/// (abstractNumId, lvlOverride*) and w:numIdMacAtCleanup, in schema order.
/// </summary>
internal static class NumberingXml
{
    private const string W = DocumentSerializer.W;

    /// <summary>Hard cap on definitions / instances read from one file (hostile input).</summary>
    internal const int MaxItems = 32_768;

    // =====================================================================================
    // Plan: which lists the saved document numbers with, including the legacy defaults
    // =====================================================================================

    /// <summary>
    /// The numbering written for a save: the document's own lists plus, when paragraphs use
    /// <see cref="ListKind"/> without a list instance, the two built-in default lists (bullets
    /// and decimal numbers) at the next free ids. Built without changing the document.
    /// </summary>
    internal sealed class Plan
    {
        private readonly HashSet<ListInstance> _known = new(ReferenceEqualityComparer.Instance);
        public Document Doc { get; }
        public NumberingDefinition? DefaultBullets { get; }
        public NumberingDefinition? DefaultNumbers { get; }
        public int DefaultBulletNumId { get; }
        public int DefaultNumberNumId { get; }

        public Plan(Document doc)
        {
            Doc = doc;
            foreach (var l in doc.Numbering.Lists) _known.Add(l);
            bool legacy = false;
            foreach (var p in DocumentSerializer.AllParagraphs(doc))
                if (p.ListInstance is null && p.LegacyListKind != ListKind.None) { legacy = true; break; }
            if (!legacy) return;
            var abs = doc.Numbering.FreeDefinitionIds(2);
            var num = doc.Numbering.FreeListIds(2);
            DefaultBullets = WithId(doc.LegacyDefaults.Bullets.Definition.Clone(), abs[0]);
            DefaultNumbers = WithId(doc.LegacyDefaults.Numbers.Definition.Clone(), abs[1]);
            DefaultBulletNumId = num[0];
            DefaultNumberNumId = num[1];
        }

        private static NumberingDefinition WithId(NumberingDefinition d, int id)
        {
            d.Id = id;
            return d;
        }

        public bool HasAny => DefaultBullets is not null || !Doc.Numbering.IsEmpty;

        /// <summary>The numId for a list instance, or null when it is not part of the document.</summary>
        public int? IdOf(ListInstance? list) => list is not null && _known.Contains(list) ? list.Id : null;

        /// <summary>
        /// The (numId, ilvl) a paragraph writes, or null for no w:numPr. numId 0 = explicitly none;
        /// a null numId = only a level for the style's numbering.
        /// </summary>
        public (int? NumId, int? Level)? For(Paragraph p)
        {
            if (p.ListInstance is { } li)
                return IdOf(li) is int id ? (id, Math.Clamp(p.ListLevel, 0, 8)) : null;
            switch (p.LegacyListKind)
            {
                case ListKind.Bullet: return (DefaultBulletNumId, Math.Clamp(p.ListLevel, 0, 8));
                case ListKind.Number: return (DefaultNumberNumId, Math.Clamp(p.ListLevel, 0, 8));
            }
            if (p.NumberingSuppressed) return (0, null);
            return p.StyleListLevel is int sl ? (null, Math.Clamp(sl, 0, 8)) : null;
        }

        /// <summary>The (numId, ilvl) a style writes, or null.</summary>
        public (int? NumId, int? Level)? For(StyleDefinition s)
        {
            int? level = s.ListLevel is int l ? Math.Clamp(l, 0, 8) : null;
            if (s.ListInstance is { } li && IdOf(li) is int id) return (id, level);
            if (s.NumberingSuppressed) return (0, level);
            return level is not null ? (null, level) : null;
        }
    }

    // =====================================================================================
    // Writer
    // =====================================================================================

    public static void Write(XmlWriter x, Plan plan)
    {
        var n = plan.Doc.Numbering;
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", "numbering", W);
        WriteRootNamespaces(x, n);
        foreach (var raw in n.RawPictureBullets) StylesXml.WriteFragment(x, raw);
        foreach (var d in n.Definitions) WriteDefinition(x, d);
        if (plan.DefaultBullets is { } db) WriteDefinition(x, db);
        if (plan.DefaultNumbers is { } dn) WriteDefinition(x, dn);
        foreach (var l in n.Lists)
        {
            if (!n.Contains(l.Definition)) continue; // never write a dangling abstractNumId
            WriteList(x, l.Id, l.Definition.Id, l.Overrides, l.RawAttributes);
        }
        if (plan.DefaultBullets is { } b) WriteList(x, plan.DefaultBulletNumId, b.Id, null);
        if (plan.DefaultNumbers is { } nn) WriteList(x, plan.DefaultNumberNumId, nn.Id, null);
        if (n.NumIdMacAtCleanup is int mac) Val(x, "numIdMacAtCleanup", Int(mac));
        x.WriteEndElement();
        x.WriteEndDocument();
    }

    private static void WriteDefinition(XmlWriter x, NumberingDefinition d)
    {
        x.WriteStartElement("abstractNum", W);
        x.WriteAttributeString("abstractNumId", W, Int(d.Id));
        WriteRawAttributes(x, d.RawAttributes);
        if (d.Nsid is not null) Val(x, "nsid", d.Nsid);
        if (d.MultiLevelType is ListMultiLevelType mt) Val(x, "multiLevelType", mt switch
        {
            ListMultiLevelType.SingleLevel => "singleLevel",
            ListMultiLevelType.Multilevel => "multilevel",
            ListMultiLevelType.HybridMultilevel => "hybridMultilevel",
            _ => throw new ArgumentOutOfRangeException(nameof(d), mt, "Undefined ListMultiLevelType value."),
        });
        if (d.TemplateCode is not null) Val(x, "tmpl", d.TemplateCode);
        if (d.Name is not null) Val(x, "name", d.Name);
        if (d.StyleLink is not null) Val(x, "styleLink", d.StyleLink);
        if (d.NumStyleLink is not null) Val(x, "numStyleLink", d.NumStyleLink);
        for (int i = 0; i < 9; i++)
        {
            var l = d.Levels[i];
            if (l.Defined || !l.IsDefault) WriteLevel(x, l, i);
        }
        x.WriteEndElement();
    }

    private static void WriteList(XmlWriter x, int numId, int abstractId, IReadOnlyDictionary<int, LevelOverride>? overrides,
        List<RawAttribute>? raw = null)
    {
        x.WriteStartElement("num", W);
        x.WriteAttributeString("numId", W, Int(numId));
        if (raw is not null) WriteRawAttributes(x, raw);
        Val(x, "abstractNumId", Int(abstractId));
        if (overrides is not null)
            foreach (var (lvl, o) in overrides)
            {
                x.WriteStartElement("lvlOverride", W);
                x.WriteAttributeString("ilvl", W, Int(lvl));
                if (o.StartOverride is int so) Val(x, "startOverride", Int(so));
                if (o.Level is { } level) WriteLevel(x, level, lvl);
                x.WriteEndElement();
            }
        x.WriteEndElement();
    }

    /// <summary>CT_Lvl: start, numFmt, lvlRestart, pStyle, isLgl, suff, lvlText, lvlPicBulletId, legacy, lvlJc, pPr, rPr.</summary>
    private static void WriteLevel(XmlWriter x, NumberingLevel l, int ilvl)
    {
        x.WriteStartElement("lvl", W);
        x.WriteAttributeString("ilvl", W, Int(ilvl));
        if (l.TemplateCode is not null) x.WriteAttributeString("tplc", W, l.TemplateCode);
        if (l.Tentative is bool t) x.WriteAttributeString("tentative", W, t ? "1" : "0");
        if (l.Start is int start) Val(x, "start", Int(start));
        if (l.Format == NumberFormat.Custom)
        {
            // Word's form: custom formats need Word 2010+ (w14); older readers fall back to decimal.
            x.WriteStartElement("mc", "AlternateContent", Mc);
            x.WriteStartElement("mc", "Choice", Mc);
            x.WriteAttributeString("xmlns", "w14", null, W14);
            x.WriteAttributeString("Requires", "w14");
            x.WriteStartElement("numFmt", W);
            x.WriteAttributeString("val", W, "custom");
            if (l.CustomFormat is not null) x.WriteAttributeString("format", W, l.CustomFormat);
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteStartElement("mc", "Fallback", Mc);
            Val(x, "numFmt", "decimal");
            x.WriteEndElement();
            x.WriteEndElement();
        }
        else if (l.Format is not null || l.RawFormatToken is not null)
        {
            x.WriteStartElement("numFmt", W);
            x.WriteAttributeString("val", W, l.Format is NumberFormat nf ? FormatToken(nf) : l.RawFormatToken!);
            if (l.CustomFormat is not null) x.WriteAttributeString("format", W, l.CustomFormat);
            x.WriteEndElement();
        }
        if (l.RestartAfterLevel is int rs) Val(x, "lvlRestart", Int(rs));
        if (l.ParagraphStyleId is not null) Val(x, "pStyle", l.ParagraphStyleId);
        if (l.IsLegal is bool lg) { x.WriteStartElement("isLgl", W); if (!lg) x.WriteAttributeString("val", W, "0"); x.WriteEndElement(); }
        if (l.Suffix is ListSuffix suff) Val(x, "suff", suff switch
        {
            ListSuffix.Tab => "tab",
            ListSuffix.Space => "space",
            ListSuffix.Nothing => "nothing",
            _ => throw new ArgumentOutOfRangeException(nameof(l), suff, "Undefined ListSuffix value."),
        });
        if (l.Text is not null) Val(x, "lvlText", l.Text);
        if (l.PictureBulletId is int pb) Val(x, "lvlPicBulletId", Int(pb));
        if (l.RawLegacyXml is not null) StylesXml.WriteFragment(x, l.RawLegacyXml);
        if (l.Alignment is ListLabelAlignment a) Val(x, "lvlJc", a switch
        {
            ListLabelAlignment.Left => "left",
            ListLabelAlignment.Center => "center",
            ListLabelAlignment.Right => "right",
            ListLabelAlignment.Start => "start",
            ListLabelAlignment.End => "end",
            _ => throw new ArgumentOutOfRangeException(nameof(l), a, "Undefined ListLabelAlignment value."),
        });
        if (!l.ParagraphFormat.IsDefault)
        {
            x.WriteStartElement("pPr", W);
            ParagraphProperties.WriteBody(x, l.ParagraphFormat);
            x.WriteEndElement();
        }
        if (l.RunFormat is not null || l.RunStyleId is not null) RunProperties.Write(x, l.RunFormat ?? TextFormat.None, l.RunStyleId);
        x.WriteEndElement();
    }

    /// <summary>
    /// Declares the namespaces carried attributes use on the root and marks them ignorable
    /// (mc:Ignorable), as Word does, so readers that do not know them still accept the part.
    /// </summary>
    private static void WriteRootNamespaces(XmlWriter x, NumberingCollection n)
    {
        var used = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Collect(List<RawAttribute> attrs)
        {
            foreach (var a in attrs)
                if (a.Prefix.Length > 0 && a.Prefix != "w" && a.Prefix != "xml" && a.Prefix != "mc" && !used.ContainsKey(a.Prefix)) used[a.Prefix] = a.Namespace;
        }
        foreach (var d in n.Definitions) Collect(d.RawAttributes);
        foreach (var l in n.Lists) Collect(l.RawAttributes);
        if (used.Count == 0) return;
        x.WriteAttributeString("xmlns", "mc", null, Mc);
        foreach (var (prefix, ns) in used) x.WriteAttributeString("xmlns", prefix, null, ns);
        x.WriteAttributeString("mc", "Ignorable", Mc, string.Join(" ", used.Keys));
    }

    private static void WriteRawAttributes(XmlWriter x, List<RawAttribute> attrs)
    {
        foreach (var a in attrs)
        {
            if (a.Namespace == "http://www.w3.org/XML/1998/namespace") x.WriteAttributeString("xml", a.LocalName, a.Namespace, a.Value);
            else x.WriteAttributeString(a.Prefix.Length > 0 ? a.Prefix : null, a.LocalName, a.Namespace, a.Value);
        }
    }

    // =====================================================================================
    // Reader
    // =====================================================================================

    public static void Read(XmlReader r, NumberingCollection target)
    {
        var definitions = new Dictionary<int, NumberingDefinition>();
        var pending = new List<(int NumId, int AbstractId, Dictionary<int, LevelOverride> Overrides, List<RawAttribute> Foreign)>();
        var seenNum = new HashSet<int>();
        if (!r.Read()) return;
        r.MoveToContent();
        if (r.NodeType != XmlNodeType.Element || r.IsEmptyElement) return;
        // Subtree readers do not see namespaces declared above them; keep the root's for mc:Choice/@Requires.
        var rootNs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (r.MoveToFirstAttribute())
        {
            do { if (r.Prefix == "xmlns") rootNs[r.LocalName] = r.Value; } while (r.MoveToNextAttribute());
            r.MoveToElement();
        }
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth) continue;
            if (r.NamespaceURI != W) { if (!r.IsEmptyElement) using (r.ReadSubtree()) { } continue; }
            switch (r.LocalName)
            {
                case "numPicBullet":
                {
                    string xml;
                    using (var sub = r.ReadSubtree()) { sub.Read(); xml = sub.ReadOuterXml(); }
                    if (target.RawPictureBullets.Count < MaxItems) target.RawPictureBullets.Add(xml);
                    break;
                }
                case "abstractNum":
                {
                    var id = ParseInt(r.GetAttribute("abstractNumId", W));
                    var foreign = ForeignAttributes(r);
                    using var sub = r.ReadSubtree();
                    var d = ReadDefinition(sub, rootNs);
                    d.RawAttributes.AddRange(foreign);
                    // First definition with an id wins (hostile duplicates); cap the count.
                    if (id is int i && i >= 0 && !definitions.ContainsKey(i) && definitions.Count < MaxItems) definitions[i] = d;
                    break;
                }
                case "num":
                {
                    var id = ParseInt(r.GetAttribute("numId", W));
                    var foreign = ForeignAttributes(r);
                    using var sub = r.ReadSubtree();
                    var (abs, overrides) = ReadNum(sub, rootNs);
                    if (id is int i && i > 0 && abs is int a && seenNum.Add(i) && pending.Count < MaxItems) pending.Add((i, a, overrides, foreign));
                    break;
                }
                case "numIdMacAtCleanup":
                    target.NumIdMacAtCleanup = ParseInt(r.GetAttribute("val", W));
                    break;
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        foreach (var (id, d) in definitions) target.AddLoaded(d, id);
        foreach (var (numId, absId, overrides, foreign) in pending)
        {
            if (!definitions.TryGetValue(absId, out var d)) continue; // dangling abstractNumId: Word shows no numbering
            var list = target.AddLoadedList(numId, d);
            list.RawAttributes.AddRange(foreign);
            foreach (var (lvl, o) in overrides)
            {
                var lo = list.Override(lvl);
                lo.StartOverride = o.StartOverride;
                lo.Level = o.Level;
            }
        }
    }

    private static NumberingDefinition ReadDefinition(XmlReader r, Dictionary<string, string> ns)
    {
        var d = new NumberingDefinition();
        if (!r.Read()) return d;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "nsid": d.Nsid = Attr(r); break;
                case "multiLevelType":
                    d.MultiLevelType = Attr(r) switch
                    {
                        "singleLevel" => ListMultiLevelType.SingleLevel,
                        "multilevel" => ListMultiLevelType.Multilevel,
                        "hybridMultilevel" => ListMultiLevelType.HybridMultilevel,
                        _ => null,
                    };
                    break;
                case "tmpl": d.TemplateCode = Attr(r); break;
                case "name": d.Name = Attr(r); break;
                case "styleLink": d.StyleLink = Attr(r); break;
                case "numStyleLink": d.NumStyleLink = Attr(r); break;
                case "lvl":
                {
                    var ilvl = ParseInt(r.GetAttribute("ilvl", W));
                    var tplc = r.GetAttribute("tplc", W);
                    var tentative = r.GetAttribute("tentative", W);
                    using var sub = r.ReadSubtree();
                    if (ilvl is int i && i >= 0 && i <= 8 && !d.Levels[i].Defined)
                    {
                        var level = d.Levels[i];
                        ReadLevel(sub, level, ns);
                        level.TemplateCode = tplc;
                        level.Tentative = tentative is null ? null : tentative is "1" or "true" or "on";
                        level.Defined = true;
                    }
                    break;
                }
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        return d;
    }

    private static (int? AbstractId, Dictionary<int, LevelOverride> Overrides) ReadNum(XmlReader r, Dictionary<string, string> ns)
    {
        int? abs = null;
        var overrides = new Dictionary<int, LevelOverride>();
        if (!r.Read()) return (abs, overrides);
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "abstractNumId": abs = ParseInt(Attr(r)); break;
                case "lvlOverride":
                {
                    var ilvl = ParseInt(r.GetAttribute("ilvl", W));
                    using var sub = r.ReadSubtree();
                    var o = ReadOverride(sub, ns);
                    if (ilvl is int i && i >= 0 && i <= 8 && !overrides.ContainsKey(i)) overrides[i] = o;
                    break;
                }
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        return (abs, overrides);
    }

    private static LevelOverride ReadOverride(XmlReader r, Dictionary<string, string> ns)
    {
        var o = new LevelOverride();
        if (!r.Read()) return o;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "startOverride": o.StartOverride = ParseInt(Attr(r)); break;
                case "lvl":
                {
                    var tplc = r.GetAttribute("tplc", W);
                    var tentative = r.GetAttribute("tentative", W);
                    using var sub = r.ReadSubtree();
                    var level = new NumberingLevel();
                    ReadLevel(sub, level, ns);
                    level.TemplateCode = tplc;
                    level.Tentative = tentative is null ? null : tentative is "1" or "true" or "on";
                    level.Defined = true;
                    o.Level = level;
                    break;
                }
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        return o;
    }

    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    private static void ReadLevel(XmlReader r, NumberingLevel l, Dictionary<string, string> ns)
    {
        if (!r.Read()) return;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth) continue;
            ReadLevelChild(r, l, ns);
        }
    }

    /// <summary>One child of w:lvl (reader on it). Word wraps custom number formats in mc:AlternateContent (Choice requires w14).</summary>
    private static void ReadLevelChild(XmlReader r, NumberingLevel l, Dictionary<string, string> ns)
    {
        if (r.NamespaceURI == Mc && r.LocalName == "AlternateContent")
        {
            using var ac = r.ReadSubtree();
            ReadAlternateContent(ac, l, ns);
            return;
        }
        if (r.NamespaceURI != W) { if (!r.IsEmptyElement) using (r.ReadSubtree()) { } return; }
        switch (r.LocalName)
        {
            case "start": l.Start = ParseInt(Attr(r)); break;
            case "numFmt":
            {
                var token = Attr(r);
                var parsed = ParseFormat(token);
                l.Format = parsed;
                l.RawFormatToken = parsed is null && token is { Length: > 0 and <= 64 } ? token : null;
                l.CustomFormat = r.GetAttribute("format", W);
                break;
            }
            case "lvlRestart": l.RestartAfterLevel = ParseInt(Attr(r)) is int rs ? Math.Clamp(rs, 0, 9) : null; break;
            case "pStyle": l.ParagraphStyleId = Attr(r); break;
            case "isLgl": l.IsLegal = RunProperties.OnOff(r); break;
            case "suff":
                l.Suffix = Attr(r) switch { "tab" => ListSuffix.Tab, "space" => ListSuffix.Space, "nothing" => ListSuffix.Nothing, _ => null };
                break;
            case "lvlText": l.Text = Attr(r); break;
            case "lvlPicBulletId": l.PictureBulletId = ParseInt(Attr(r)); break;
            case "legacy":
                using (var sub = r.ReadSubtree()) { sub.Read(); l.RawLegacyXml = sub.ReadOuterXml(); }
                break;
            case "lvlJc":
                l.Alignment = Attr(r) switch
                {
                    "left" => ListLabelAlignment.Left,
                    "center" => ListLabelAlignment.Center,
                    "right" => ListLabelAlignment.Right,
                    "start" => ListLabelAlignment.Start,
                    "end" => ListLabelAlignment.End,
                    _ => null,
                };
                break;
            case "pPr":
            {
                using var sub = r.ReadSubtree();
                var scratch = new Paragraph();
                ParagraphProperties.Read(sub, scratch);
                l.ParagraphFormat.ApplyFrom(scratch.Format);
                break;
            }
            case "rPr":
            {
                using var sub = r.ReadSubtree();
                l.RunFormat = RunProperties.Read(sub, out var styleId);
                l.RunStyleId = styleId;
                break;
            }
            default:
                if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                break;
        }
    }

    /// <summary>mc:AlternateContent inside w:lvl: the first Choice whose required namespaces are understood (w14), else the Fallback.</summary>
    private static void ReadAlternateContent(XmlReader ac, NumberingLevel l, Dictionary<string, string> ns)
    {
        if (!ac.Read()) return;
        int branchDepth = ac.Depth + 1;
        bool taken = false;
        while (ac.Read())
        {
            if (ac.NodeType != XmlNodeType.Element || ac.Depth != branchDepth || ac.NamespaceURI != Mc) continue;
            bool isChoice = ac.LocalName == "Choice";
            bool usable = !taken && (isChoice ? Understood(ac, ac.GetAttribute("Requires"), ns) : ac.LocalName == "Fallback");
            using var branch = ac.ReadSubtree();
            if (!usable) continue;
            taken = true;
            if (!branch.Read()) continue;
            int childDepth = branch.Depth + 1;
            while (branch.Read())
                if (branch.NodeType == XmlNodeType.Element && branch.Depth == childDepth) ReadLevelChild(branch, l, ns);
        }
    }

    private static bool Understood(XmlReader r, string? requires, Dictionary<string, string> rootNs)
    {
        if (string.IsNullOrWhiteSpace(requires)) return false;
        foreach (var prefix in requires.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var ns = r.LookupNamespace(prefix) ?? (rootNs.TryGetValue(prefix, out var rn) ? rn : null);
            if (ns != W14 && ns != W) return false;
        }
        return true;
    }

    /// <summary>Attributes of the current element outside the w namespace (and not namespace declarations).</summary>
    private static List<RawAttribute> ForeignAttributes(XmlReader r)
    {
        var list = new List<RawAttribute>();
        if (r.MoveToFirstAttribute())
        {
            do
            {
                if (r.NamespaceURI is "" or W or "http://www.w3.org/2000/xmlns/") continue;
                if (list.Count < 32) list.Add(new RawAttribute(r.Prefix, r.NamespaceURI, r.LocalName, r.Value));
            } while (r.MoveToNextAttribute());
            r.MoveToElement();
        }
        return list;
    }

    // =====================================================================================
    // Tokens and helpers
    // =====================================================================================

    internal static string FormatToken(NumberFormat f)
    {
        if (!Enum.IsDefined(f)) throw new ArgumentOutOfRangeException(nameof(f), f, "Undefined NumberFormat value.");
        var name = f.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static readonly Dictionary<string, NumberFormat> FormatByToken = BuildTokens();

    private static Dictionary<string, NumberFormat> BuildTokens()
    {
        var d = new Dictionary<string, NumberFormat>(StringComparer.Ordinal);
        foreach (var f in Enum.GetValues<NumberFormat>()) d[FormatToken(f)] = f;
        return d;
    }

    internal static NumberFormat? ParseFormat(string? token)
        => token is not null && FormatByToken.TryGetValue(token, out var f) ? f : null;

    private static int? ParseInt(string? v)
        => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static string? Attr(XmlReader r) => r.GetAttribute("val", W);

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement(name, W);
        x.WriteAttributeString("val", W, value);
        x.WriteEndElement();
    }
}
