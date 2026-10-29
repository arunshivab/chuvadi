using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Phase 5 — lists: the numbering model (definitions, instances, overrides), numbering.xml in
/// schema order with exact round-trip, Word-compatible label computation (formats, restarts,
/// start overrides, legal numbering, style-linked numbering), and hostile numbering parts.
/// </summary>
public static class ListsTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly string[] LvlOrder =
    {
        "start", "numFmt", "lvlRestart", "pStyle", "isLgl", "suff", "lvlText", "lvlPicBulletId", "legacy", "lvlJc", "pPr", "rPr",
    };

    public static void Run(string outDir)
    {
        LegacyListsUnchanged(outDir);
        CustomListsWritten(outDir);
        Labels();
        Formats();
        StyleLinkedNumbering(outDir);
        RoundTrip(outDir);
        Effective();
        Hostile(outDir);
        ReviewRegressions(outDir);
    }

    private static string Label(Document doc, Paragraph p) => doc.GetListLabel(p)?.Text ?? "<none>";

    private static void LegacyListsUnchanged(string outDir)
    {
        var path = Path.Combine(outDir, "lists-legacy.docx");
        var doc = new Document();
        doc.AddBullet("a");
        doc.AddBullet("b", 1);
        doc.AddNumbered("one");
        doc.AddNumbered("two");
        doc.SaveTo(path);

        Check("AddBullet/AddNumbered: numbering.xml as before (two definitions, numIds 1 and 2, no multiLevelType)", () =>
        {
            var n = XElement.Parse(ReadPartXml(path, "word/numbering.xml"));
            var abs = n.Elements(Wn + "abstractNum").ToList();
            AssertTrue(abs.Count == 2 && abs.All(a => a.Element(Wn + "multiLevelType") is null), "two plain definitions");
            AssertTrue(abs[0].Elements(Wn + "lvl").First().Element(Wn + "numFmt")!.Attribute(Wn + "val")!.Value == "bullet", "bullets first");
            AssertTrue(abs[1].Elements(Wn + "lvl").ElementAt(2).Element(Wn + "lvlText")!.Attribute(Wn + "val")!.Value == "%3.", "decimal level text");
            var nums = n.Elements(Wn + "num").Select(e => e.Attribute(Wn + "numId")!.Value + ">" + e.Element(Wn + "abstractNumId")!.Attribute(Wn + "val")!.Value);
            AssertTrue(string.Join(",", nums) == "1>0,2>1", string.Join(",", nums));
            var numIds = XElement.Parse(ReadPartXml(path, "word/document.xml")).Descendants(Wn + "numId").Select(e => e.Attribute(Wn + "val")!.Value);
            AssertTrue(string.Join(",", numIds) == "1,1,2,2", string.Join(",", numIds));
        });
        Check("legacy labels: •, –, 1., 2.; ListKind reads back after load", () =>
        {
            var labels = doc.GetListLabels();
            var texts = doc.Blocks.OfType<Paragraph>().Select(p => labels[p].Text);
            AssertTrue(string.Join("|", texts) == "•|–|1.|2.", string.Join("|", texts));
            var back = Document.Load(path);
            var kinds = back.Blocks.OfType<Paragraph>().Select(p => p.List.ToString());
            AssertTrue(string.Join(",", kinds) == "Bullet,Bullet,Number,Number", string.Join(",", kinds));
            AssertTrue(back.Numbering.Lists.Count == 2 && back.Blocks.OfType<Paragraph>().First().ListInstance is not null, "loaded as real lists");
        });
        Check("no lists → no numbering part", () =>
        {
            var p2 = Path.Combine(outDir, "lists-none.docx");
            var d = new Document();
            d.AddParagraph("plain");
            d.SaveTo(p2);
            using var z = ZipFile.OpenRead(p2);
            AssertTrue(z.GetEntry("word/numbering.xml") is null, "numbering part written");
        });
    }

    private static void CustomListsWritten(string outDir)
    {
        var path = Path.Combine(outDir, "lists-custom.docx");
        var doc = new Document();
        var roman = doc.Numbering.AddNumberedList(NumberFormat.LowerRoman, "({0})");
        var squares = doc.Numbering.AddBulletList("■");
        squares.Definition.Level(0).RunFormat = new TextFormat { ColorHex = "C00000" };
        var legal = doc.Numbering.AddList(NumberingDefinition.PerLevel((NumberFormat.UpperRoman, "%1."), (NumberFormat.Decimal, "%1.%2")));
        legal.Definition.Level(1).IsLegal = true;
        legal.Definition.Level(1).Suffix = ListSuffix.Space;
        legal.Definition.Level(1).RestartAfterLevel = 0;
        legal.Definition.Name = "Legal";
        doc.AddListItem("first", roman);
        doc.AddListItem("second", roman);
        doc.AddListItem("square", squares);
        doc.AddListItem("Part", legal);
        doc.AddListItem("clause", legal, 1);
        doc.SaveTo(path);

        Check("custom lists: abstractNum and lvl children in schema order, overrides and attributes written", () =>
        {
            var n = XElement.Parse(ReadPartXml(path, "word/numbering.xml"));
            AssertTrue(n.Elements(Wn + "abstractNum").Count() == 3 && n.Elements(Wn + "num").Count() == 3, "3 + 3");
            foreach (var lvl in n.Descendants(Wn + "lvl")) AssertOrder(lvl, LvlOrder);
            var children = n.Elements().Select(e => e.Name.LocalName).ToList();
            AssertTrue(children.LastIndexOf("abstractNum") < children.IndexOf("num"), "abstractNums before nums");
            var leg = n.Elements(Wn + "abstractNum").ElementAt(2);
            var abstractChildren = leg.Elements().Select(e => e.Name.LocalName).TakeWhile(x => x != "lvl").ToList();
            AssertTrue(string.Join(",", abstractChildren) == "multiLevelType,name", string.Join(",", abstractChildren));
            var l1 = leg.Elements(Wn + "lvl").ElementAt(1);
            AssertTrue(l1.Element(Wn + "isLgl") is not null && l1.Element(Wn + "suff")!.Attribute(Wn + "val")!.Value == "space"
                && l1.Element(Wn + "lvlRestart")!.Attribute(Wn + "val")!.Value == "0", "isLgl/suff/lvlRestart");
            var sq = n.Elements(Wn + "abstractNum").ElementAt(1).Elements(Wn + "lvl").First();
            AssertTrue(sq.Element(Wn + "rPr")!.Element(Wn + "color")!.Attribute(Wn + "val")!.Value == "C00000", "bullet colour");
        });
        Check("custom list labels: (i) (ii) ■ I. 1.1 (legal shows decimals)", () =>
        {
            var texts = doc.Blocks.OfType<Paragraph>().Select(p => Label(doc, p));
            AssertTrue(string.Join("|", texts) == "(i)|(ii)|■|I.|1.1", string.Join("|", texts));
            var sqLabel = doc.GetListLabel(doc.Blocks.OfType<Paragraph>().ElementAt(2))!;
            AssertTrue(sqLabel.Format.ColorHex == "C00000", "label format from the level");
            AssertTrue(doc.GetListLabel(doc.Blocks.OfType<Paragraph>().Last())!.Suffix == ListSuffix.Space, "suffix");
        });
    }

    private static void Labels()
    {
        Check("counting: nested levels restart after a shallower item; a level skipped over counts once at its start (as Word/LibreOffice)", () =>
        {
            var doc = new Document();
            var l = doc.Numbering.AddList(NumberingDefinition.PerLevel((NumberFormat.Decimal, "%1."), (NumberFormat.LowerLetter, "%1.%2"), (NumberFormat.LowerRoman, "%3)")));
            var ps = new[]
            {
                doc.AddListItem("x", l, 1), // level 2 first: level 1 shows its start
                doc.AddListItem("a", l), doc.AddListItem("b", l, 1), doc.AddListItem("c", l, 1), doc.AddListItem("d", l, 2),
                doc.AddListItem("e", l), doc.AddListItem("f", l, 1), doc.AddListItem("g", l, 2), doc.AddListItem("h", l, 2),
            };
            var texts = string.Join("|", ps.Select(p => Label(doc, p)));
            AssertTrue(texts == "1.a|2.|2.a|2.b|i)|3.|3.a|i)|ii)", texts);
        });
        Check("instances of one definition continue; Restart() starts again; startOverride; RestartAfterLevel = 0 never restarts", () =>
        {
            var doc = new Document();
            var a = doc.Numbering.AddNumberedList();
            var b = doc.Numbering.AddList(a.Definition);
            var p1 = doc.AddListItem("1", a);
            var p2 = doc.AddListItem("2", b);
            var r = doc.Numbering.Restart(a);
            var p3 = doc.AddListItem("1 again", r);
            var p4 = doc.AddListItem("2 again", a);
            var s = doc.Numbering.Restart(a, startAt: 10);
            var p5 = doc.AddListItem("10", s);
            AssertTrue(string.Join("|", new[] { p1, p2, p3, p4, p5 }.Select(p => Label(doc, p))) == "1.|2.|1.|2.|10.", "sequence");
            AssertTrue(r.Overrides[0].StartOverride == 1 && s.Overrides[0].StartOverride == 10, "override values");

            var never = doc.Numbering.AddNumberedList();
            never.Definition.Level(1).RestartAfterLevel = 0;
            var q = new[] { doc.AddListItem("", never, 1), doc.AddListItem("", never), doc.AddListItem("", never, 1) };
            AssertTrue(string.Join("|", q.Select(p => Label(doc, p))) == "1.|2.|2.", string.Join("|", q.Select(p => Label(doc, p))));
        });
        Check("lists in tables and nested tables are numbered in document order and get a numbering part", () =>
        {
            var doc = new Document();
            var l = doc.Numbering.AddNumberedList();
            doc.AddListItem("before", l);
            var t = doc.AddTable(1);
            t.AddRow().Cell(0).Paragraphs.Add(new Paragraph("in cell").SetList(l));
            var inner = t.Rows[0].Cells[0].AddTable(1);
            var nestedItem = new Paragraph("nested").SetList(l);
            inner.AddRow().Cell(0).Paragraphs.Add(nestedItem);
            doc.AddListItem("after", l);
            var labels = doc.GetListLabels();
            AssertTrue(labels.Count == 4 && labels[nestedItem].Text == "3." && labels[(Paragraph)doc.Blocks[^1]].Text == "4.", "order");

            var legacyOnlyNested = new Document();
            var t2 = legacyOnlyNested.AddTable(1);
            var nt = t2.AddRow().Cell(0).AddTable(1);
            nt.AddRow().Cell(0).Paragraphs.Add(new Paragraph("deep") { List = ListKind.Bullet });
            var ms = new MemoryStream();
            legacyOnlyNested.SaveTo(ms);
            using var z = new ZipArchive(new MemoryStream(ms.ToArray()));
            AssertTrue(z.GetEntry("word/numbering.xml") is not null, "numbering part for a list only inside a nested table");
        });
        Check("NumberingSuppressed writes numId 0 and switches style numbering off", () =>
        {
            var doc = new Document();
            var l = doc.Numbering.AddNumberedList();
            var st = doc.Styles.AddParagraphStyle("Step", "Step");
            st.ListInstance = l;
            var a = doc.AddParagraph("a"); a.StyleId = "Step";
            var b = doc.AddParagraph("b"); b.StyleId = "Step"; b.NumberingSuppressed = true;
            var c = doc.AddParagraph("c"); c.StyleId = "Step";
            AssertTrue(Label(doc, a) == "1." && Label(doc, b) == "<none>" && Label(doc, c) == "2.", "suppressed");
            var ms = new MemoryStream();
            doc.SaveTo(ms);
            var xml = ReadPart(ms, "word/document.xml");
            AssertContains(xml, "<w:numPr><w:numId w:val=\"0\" /></w:numPr>", "numId 0");
        });
        Check("setting List = None removes a paragraph from its list; SetList validates the level", () =>
        {
            var doc = new Document();
            var l = doc.Numbering.AddNumberedList();
            var p = doc.AddListItem("x", l);
            p.List = ListKind.None;
            AssertTrue(p.ListInstance is null && doc.GetListLabel(p) is null, "removed");
            AssertThrows<ArgumentOutOfRangeException>(() => p.SetList(l, 9), "level 9");
            AssertThrows<ArgumentException>(() => new Document().Numbering.Add(l.Definition), "definition of another document");
        });
    }

    private static void Formats()
    {
        Check("number formats render as Word does", () =>
        {
            (NumberFormat F, int N, string Expected, string? Custom)[] cases =
            {
                (NumberFormat.Decimal, 42, "42", null), (NumberFormat.DecimalZero, 7, "07", null), (NumberFormat.DecimalZero, 12, "12", null),
                (NumberFormat.UpperRoman, 1994, "MCMXCIV", null), (NumberFormat.LowerRoman, 4, "iv", null),
                (NumberFormat.UpperLetter, 1, "A", null), (NumberFormat.LowerLetter, 26, "z", null), (NumberFormat.LowerLetter, 27, "aa", null),
                (NumberFormat.UpperLetter, 53, "AAA", null),
                (NumberFormat.Ordinal, 1, "1st", null), (NumberFormat.Ordinal, 12, "12th", null), (NumberFormat.Ordinal, 23, "23rd", null),
                (NumberFormat.CardinalText, 21, "Twenty-one", null), (NumberFormat.CardinalText, 100, "One hundred", null),
                (NumberFormat.OrdinalText, 3, "Third", null), (NumberFormat.OrdinalText, 20, "Twentieth", null), (NumberFormat.OrdinalText, 42, "Forty-second", null),
                (NumberFormat.Hex, 255, "FF", null), (NumberFormat.Chicago, 1, "*", null), (NumberFormat.Chicago, 6, "††", null),
                (NumberFormat.DecimalFullWidth, 12, "１２", null), (NumberFormat.DecimalEnclosedCircle, 3, "③", null),
                (NumberFormat.DecimalEnclosedCircle, 21, "21", null), (NumberFormat.DecimalEnclosedParen, 1, "⑴", null),
                (NumberFormat.NumberInDash, 5, "- 5 -", null), (NumberFormat.HindiNumbers, 2026, "२०२६", null),
                (NumberFormat.ThaiNumbers, 9, "๙", null), (NumberFormat.IdeographDigital, 10, "一〇", null),
                (NumberFormat.Custom, 7, "007", "001, 002, 003, ..."),
                (NumberFormat.Custom, 12, "௧௨", "௧, ௨, ௩, ..."),
                (NumberFormat.None, 3, "", null), (NumberFormat.Bullet, 3, "", null),
                (NumberFormat.KoreanCounting, 4, "4", null), // no rendering: decimal fallback
            };
            foreach (var (f, n, expected, custom) in cases)
            {
                var doc = new Document();
                var def = NumberingDefinition.Numbered(f, "{0}");
                def.Level(0).Start = n;
                def.Level(0).CustomFormat = custom;
                var p = doc.AddListItem("x", doc.Numbering.AddList(def));
                var got = Label(doc, p);
                AssertTrue(got == expected, $"{f} {n}: got '{got}', expected '{expected}'");
            }
        });
    }

    private static void StyleLinkedNumbering(string outDir)
    {
        var path = Path.Combine(outDir, "lists-headings.docx");
        var doc = new Document();
        var outline = doc.Numbering.AddOutlineList();
        outline.Definition.Level(0).ParagraphStyleId = "Heading1";
        outline.Definition.Level(1).ParagraphStyleId = "Heading2";
        doc.Styles["Heading1"].ListInstance = outline;
        doc.Styles["Heading2"].ListInstance = outline;
        doc.Styles["Heading2"].ListLevel = 1;
        var h = new[]
        {
            doc.AddHeading("Intro", 1), doc.AddHeading("Scope", 2), doc.AddHeading("Terms", 2), doc.AddHeading("Method", 1), doc.AddHeading("Data", 2),
        };
        doc.SaveTo(path);

        Check("numbered headings through paragraph styles: 1. 1.1. 1.2. 2. 2.1.", () =>
        {
            var texts = string.Join("|", h.Select(p => Label(doc, p)));
            AssertTrue(texts == "1.|1.1.|1.2.|2.|2.1.", texts);
            var st = XElement.Parse(ReadPartXml(path, "word/styles.xml"));
            var h1 = st.Elements(Wn + "style").Single(e => e.Attribute(Wn + "styleId")!.Value == "Heading1");
            var np = h1.Element(Wn + "pPr")!.Element(Wn + "numPr")!;
            AssertTrue(np.Element(Wn + "ilvl") is null && np.Element(Wn + "numId")!.Attribute(Wn + "val")!.Value == outline.Id.ToString(), "Heading1 numPr: numId only (level from pStyle)");
        });
        Check("style-linked numbering survives load and labels the same", () =>
        {
            var back = Document.Load(path);
            var texts = string.Join("|", back.Blocks.OfType<Paragraph>().Select(p => Label(back, p)));
            AssertTrue(texts == "1.|1.1.|1.2.|2.|2.1.", texts);
            AssertTrue(back.Styles["Heading2"].ListInstance is not null && back.Styles["Heading2"].ListLevel == 1, "style link");
        });
        Check("numStyleLink: a definition that defers to a numbering style uses that style's levels and counter", () =>
        {
            var d = new Document();
            var real = d.Numbering.AddNumberedList(NumberFormat.UpperLetter, "{0})");
            real.Definition.StyleLink = "MyList";
            var ns = new StyleDefinition("MyList", StyleType.Numbering) { Name = "My List" };
            ns.ListInstance = real;
            d.Styles.AddOrReplace(ns);
            var linkDef = new NumberingDefinition { NumStyleLink = "MyList" };
            var viaLink = d.Numbering.AddList(linkDef);
            var a = d.AddListItem("a", real);
            var b = d.AddListItem("b", viaLink);
            AssertTrue(Label(d, a) == "A)" && Label(d, b) == "B)", $"{Label(d, a)} {Label(d, b)}");
        });
    }

    private static void RoundTrip(string outDir)
    {
        var a = Path.Combine(outDir, "lists-rt-a.docx");
        var doc = new Document();
        var l = doc.Numbering.AddList(NumberingDefinition.PerLevel((NumberFormat.Decimal, "%1."), (NumberFormat.LowerLetter, "%2)")));
        l.Definition.Nsid = "1A2B3C4D";
        l.Definition.TemplateCode = "0409000F";
        l.Definition.Level(0).TemplateCode = "04090011";
        l.Definition.Level(3).Tentative = true;
        l.Definition.Level(0).ParagraphFormat.TabStops.Add(new TabStop(36, TabAlignment.Number));
        var r = doc.Numbering.Restart(l, 1, 5);
        r.Override(2).Level = new NumberingLevel { Start = 1, Format = NumberFormat.UpperRoman, Text = "%3." };
        doc.AddListItem("one", l);
        doc.AddListItem("sub", r, 1);
        doc.AddBullet("legacy bullet");
        doc.SaveTo(a);

        Check("numbering.xml and document.xml round-trip exactly (overrides, nsid, tplc, tentative, num tab, legacy lists)", () =>
        {
            var b = Path.Combine(outDir, "lists-rt-b.docx");
            Document.Load(a).SaveTo(b);
            AssertTrue(ReadPartXml(a, "word/numbering.xml") == ReadPartXml(b, "word/numbering.xml"), "numbering.xml differs");
            AssertTrue(ReadPartXml(a, "word/document.xml") == ReadPartXml(b, "word/document.xml"), "document.xml differs");
        });
        Check("foreign numbering.xml: picture bullets, legacy, unknown format token and w15 extras survive; labels computed", () =>
        {
            var path = WithNumbering(outDir, "lists-foreign.docx",
                "<w:numPicBullet w:numPicBulletId=\"0\"><w:pict><v:shape xmlns:v=\"urn:schemas-microsoft-com:vml\" id=\"_x0000_i1025\"/></w:pict></w:numPicBullet>" +
                "<w:abstractNum w:abstractNumId=\"7\"><w:nsid w:val=\"0F0E0D0C\"/><w:multiLevelType w:val=\"hybridMultilevel\"/>" +
                "<w:lvl w:ilvl=\"0\" w:tplc=\"04090001\"><w:start w:val=\"1\"/><w:numFmt w:val=\"futureFormat\"/><w:lvlText w:val=\"%1.\"/><w:lvlPicBulletId w:val=\"0\"/>" +
                "<w:legacy w:legacy=\"1\" w:legacySpace=\"0\" w:legacyIndent=\"360\"/><w:lvlJc w:val=\"start\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>" +
                "<w:num w:numId=\"4\"><w:abstractNumId w:val=\"7\"/></w:num><w:numIdMacAtCleanup w:val=\"2\"/>",
                "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"4\"/></w:numPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p>" +
                "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"4\"/></w:numPr></w:pPr><w:r><w:t>y</w:t></w:r></w:p>");
            var doc2 = Document.Load(path);
            var ps = doc2.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(Label(doc2, ps[0]) == "1." && Label(doc2, ps[1]) == "2.", "unknown format counts as decimal");
            var lvl = doc2.Numbering.Lists[0].Definition.Levels[0];
            AssertTrue(lvl.Format is null && lvl.Alignment == ListLabelAlignment.Start && lvl.PictureBulletId == 0 && lvl.TemplateCode == "04090001", "level model");
            var outPath = Path.Combine(outDir, "lists-foreign-out.docx");
            doc2.SaveTo(outPath);
            var n = ReadPartXml(outPath, "word/numbering.xml");
            AssertContains(n, "numPicBullet", "picture bullet kept");
            AssertContains(n, "w:val=\"futureFormat\"", "unknown numFmt token kept");
            AssertContains(n, "w:legacyIndent=\"360\"", "legacy kept");
            AssertContains(n, "<w:numIdMacAtCleanup w:val=\"2\" />", "numIdMacAtCleanup kept");
            AssertContains(ReadPartXml(outPath, "word/document.xml"), "<w:numId w:val=\"4\" />", "numId kept");
        });
    }

    private static void Effective()
    {
        Check("effective paragraph format of a list item includes the level's indents (style and direct override them)", () =>
        {
            var doc = new Document();
            var l = doc.Numbering.AddNumberedList();
            var p = doc.AddListItem("x", l, 2);
            var f = doc.GetEffectiveParagraphFormat(p);
            AssertTrue(f.IndentLeftPt == 108 && f.HangingIndentPt == 18, $"level 3 indent {f.IndentLeftPt}/{f.HangingIndentPt}");
            p.Format.IndentLeftPt = 50;
            AssertTrue(doc.GetEffectiveParagraphFormat(p).IndentLeftPt == 50, "direct wins");
        });
    }

    private static void Hostile(string outDir)
    {
        Check("hostile numbering: numStyleLink cycle, duplicate/huge ids, bad levels, int.MaxValue start, dangling ids — loads, labels, saves", () =>
        {
            var sb = new StringBuilder();
            sb.Append("<w:abstractNum w:abstractNumId=\"1\"><w:numStyleLink w:val=\"LoopA\"/></w:abstractNum>");
            sb.Append("<w:abstractNum w:abstractNumId=\"2\"><w:styleLink w:val=\"LoopA\"/><w:numStyleLink w:val=\"LoopB\"/></w:abstractNum>");
            sb.Append("<w:abstractNum w:abstractNumId=\"1\"><w:lvl w:ilvl=\"0\"><w:numFmt w:val=\"upperRoman\"/></w:lvl></w:abstractNum>"); // duplicate id: first wins
            sb.Append("<w:abstractNum w:abstractNumId=\"2147483647\"><w:lvl w:ilvl=\"99\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"2147483647\"/><w:lvlText w:val=\"%1%9%9%9%9%9%9%9%9%9\"/></w:lvl></w:abstractNum>");
            sb.Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"1\"/></w:num>");
            sb.Append("<w:num w:numId=\"2\"><w:abstractNumId w:val=\"2\"/></w:num>");
            sb.Append("<w:num w:numId=\"3\"><w:abstractNumId w:val=\"2147483647\"/></w:num>");
            sb.Append("<w:num w:numId=\"9\"><w:abstractNumId w:val=\"555\"/></w:num>"); // dangling definition
            sb.Append("<w:num w:numId=\"-4\"><w:abstractNumId w:val=\"1\"/></w:num>");
            var body = new StringBuilder();
            foreach (var id in new[] { 1, 2, 3, 3, 9, 77 })
                body.Append($"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{id}\"/></w:numPr></w:pPr><w:r><w:t>p{id}</w:t></w:r></w:p>");
            var path = WithNumbering(outDir, "lists-hostile.docx", sb.ToString(), body.ToString(),
                "<w:style w:type=\"numbering\" w:styleId=\"LoopA\"><w:name w:val=\"LoopA\"/><w:pPr><w:numPr><w:numId w:val=\"2\"/></w:numPr></w:pPr></w:style>" +
                "<w:style w:type=\"numbering\" w:styleId=\"LoopB\"><w:name w:val=\"LoopB\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>");
            var doc = Document.Load(path);
            var labels = doc.GetListLabels();
            var ps = doc.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(ps[4].ListInstance is null && ps[5].ListInstance is null, "dangling numIds number nothing");
            AssertTrue(labels[ps[2]].Number == int.MaxValue && labels[ps[3]].Number == int.MaxValue, "no overflow");
            AssertTrue(doc.Numbering.Definitions.Count == 3 && doc.Numbering.Lists.Count == 3, $"defs {doc.Numbering.Definitions.Count} lists {doc.Numbering.Lists.Count}");
            doc.SaveTo(new MemoryStream());
        });
        Check("hostile numbering: 40,000 definitions are capped, load stays fast", () =>
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 40_000; i++) sb.Append("<w:abstractNum w:abstractNumId=\"").Append(i).Append("\"/>");
            var path = WithNumbering(outDir, "lists-many.docx", sb.ToString(), "<w:p/>");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = Document.Load(path);
            AssertTrue(doc.Numbering.Definitions.Count == 32_768 && sw.Elapsed.TotalSeconds < 20, $"{doc.Numbering.Definitions.Count} in {sw.Elapsed}");
        });
    }

    private const string RootNs =
        "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\" " +
        "xmlns:w16cid=\"http://schemas.microsoft.com/office/word/2016/wordml/cid\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" mc:Ignorable=\"w14 w15 w16cid\"";

    // 1×1 transparent PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static void ReviewRegressions(string outDir)
    {
        Check("Word's custom format (mc:AlternateContent, Choice requires w14) is read, labelled and written back in Word's form", () =>
        {
            var path = WithNumbering(outDir, "lists-word-custom.docx",
                "<w:abstractNum w:abstractNumId=\"0\" w15:restartNumberingAfterBreak=\"0\"><w:multiLevelType w:val=\"hybridMultilevel\"/>" +
                "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><mc:AlternateContent><mc:Choice Requires=\"w14\"><w:numFmt w:val=\"custom\" w:format=\"001, 002, 003, ...\"/></mc:Choice>" +
                "<mc:Fallback><w:numFmt w:val=\"decimal\"/></mc:Fallback></mc:AlternateContent><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/></w:lvl></w:abstractNum>" +
                "<w:num w:numId=\"1\" w16cid:durableId=\"123456789\"><w:abstractNumId w:val=\"0\"/></w:num>",
                "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>a</w:t></w:r></w:p>", rootAttributes: RootNs);
            var doc = Document.Load(path);
            var p = doc.Blocks.OfType<Paragraph>().First();
            AssertTrue(Label(doc, p) == "001.", Label(doc, p));
            var outPath = Path.Combine(outDir, "lists-word-custom-out.docx");
            doc.SaveTo(outPath);
            var n = ReadPartXml(outPath, "word/numbering.xml");
            AssertContains(n, "<mc:Choice xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" Requires=\"w14\"><w:numFmt w:val=\"custom\" w:format=\"001, 002, 003, ...\" /></mc:Choice><mc:Fallback><w:numFmt w:val=\"decimal\" /></mc:Fallback>", "AlternateContent form");
            AssertContains(n, "w15:restartNumberingAfterBreak=\"0\"", "w15 attribute kept");
            AssertContains(n, "w16cid:durableId=\"123456789\"", "w16cid attribute kept");
            AssertContains(n, "mc:Ignorable=\"w15 w16cid\"", "ignorable declared");
            var again = Document.Load(outPath);
            AssertTrue(Label(again, again.Blocks.OfType<Paragraph>().First()) == "001.", "after round-trip");
        });
        Check("a numPr with only ilvl (style and paragraph) selects the level of style numbering and is written back", () =>
        {
            var path = WithNumbering(outDir, "lists-ilvl-only.docx",
                "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>" +
                "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>" +
                "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2.\"/></w:lvl></w:abstractNum>" +
                "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>",
                "<w:p><w:pPr><w:pStyle w:val=\"H1\"/></w:pPr><w:r><w:t>a</w:t></w:r></w:p>" +
                "<w:p><w:pPr><w:pStyle w:val=\"H2\"/></w:pPr><w:r><w:t>b</w:t></w:r></w:p>" +
                "<w:p><w:pPr><w:pStyle w:val=\"H1\"/><w:numPr><w:ilvl w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>c</w:t></w:r></w:p>",
                "<w:style w:type=\"paragraph\" w:styleId=\"H1\"><w:name w:val=\"H1\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>" +
                "<w:style w:type=\"paragraph\" w:styleId=\"H2\"><w:name w:val=\"H2\"/><w:basedOn w:val=\"H1\"/><w:pPr><w:numPr><w:ilvl w:val=\"1\"/></w:numPr></w:pPr></w:style>");
            var doc = Document.Load(path);
            var texts = string.Join("|", doc.Blocks.OfType<Paragraph>().Select(p => Label(doc, p)));
            AssertTrue(texts == "1.|1.1.|1.2.", texts);
            var outPath = Path.Combine(outDir, "lists-ilvl-only-out.docx");
            doc.SaveTo(outPath);
            var st = ReadPartXml(outPath, "word/styles.xml");
            AssertContains(st, "<w:numPr><w:ilvl w:val=\"1\" /></w:numPr>", "style ilvl-only kept");
            AssertContains(ReadPartXml(outPath, "word/document.xml"), "<w:numPr><w:ilvl w:val=\"1\" /></w:numPr>", "paragraph ilvl-only kept");
        });
        Check("picture bullets keep their image: numbering.xml relationships and media are written", () =>
        {
            var path = WithNumbering(outDir, "lists-picbullet.docx",
                "<w:numPicBullet w:numPicBulletId=\"0\"><w:pict><v:shape id=\"_x0000_i1025\" style=\"width:9pt;height:9pt\" o:bullet=\"t\"><v:imagedata r:id=\"rId1\" o:title=\"\"/></v:shape></w:pict></w:numPicBullet>" +
                "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"\uF0B7\"/><w:lvlPicBulletId w:val=\"0\"/></w:lvl></w:abstractNum>" +
                "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>",
                "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>a</w:t></w:r></w:p>", rootAttributes: RootNs, pictureBulletPng: Png);
            var outPath = Path.Combine(outDir, "lists-picbullet-out.docx");
            Document.Load(path).SaveTo(outPath);
            var rels = ReadPartXml(outPath, "word/_rels/numbering.xml.rels");
            AssertContains(rels, "Id=\"rId1\"", "relationship id kept");
            using var z = ZipFile.OpenRead(outPath);
            var media = z.Entries.Single(e => e.FullName.StartsWith("word/media/numbering-picture", StringComparison.Ordinal));
            AssertTrue(media.Length == Png.Length, "image bytes");
        });
        Check("lists outside the collection number nothing: another document's list is refused, a removed list loses label and numPr", () =>
        {
            var a = new Document();
            var la = a.Numbering.AddNumberedList();
            var b = new Document();
            AssertThrows<ArgumentException>(() => b.AddListItem("x", la), "foreign list");
            var p = new Paragraph("y").SetList(la);
            b.AddParagraph(p);
            AssertTrue(b.GetListLabel(p) is null && b.GetEffectiveParagraphFormat(p).IndentLeftPt is null, "foreign list: no label, no indent");
            var own = b.Numbering.AddNumberedList();
            var q = b.AddListItem("z", own);
            b.Numbering.Remove(own);
            var ms = new MemoryStream();
            b.SaveTo(ms);
            AssertTrue(b.GetListLabel(q) is null && !ReadPart(ms, "word/document.xml").Contains("numPr"), "removed list");
            var legacy = new Document();
            var bullet = legacy.AddBullet("b");
            AssertThrows<ArgumentException>(() => legacy.Numbering.Restart(legacy.GetListLabel(bullet)!.List), "default list cannot be restarted");
        });
        Check("ids: definitions can be re-added after Remove; ids next to int.MaxValue reuse free low ids; Clone drops nsid; TryGetOverride has no side effect", () =>
        {
            var d = new Document();
            var def = NumberingDefinition.Numbered();
            d.Numbering.Add(def);
            d.Numbering.Remove(def);
            AssertTrue(def.Id == -1, "id reset");
            d.Numbering.Add(def);
            var path = WithNumbering(outDir, "lists-maxid.docx",
                "<w:abstractNum w:abstractNumId=\"2147483647\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum>" +
                "<w:num w:numId=\"2147483647\"><w:abstractNumId w:val=\"2147483647\"/></w:num>", "<w:p/>");
            var m = Document.Load(path);
            var l1 = m.Numbering.AddNumberedList();
            var l2 = m.Numbering.AddNumberedList();
            AssertTrue(l1.Id == 1 && l2.Id == 2 && l1.Definition.Id == 0 && l2.Definition.Id == 1, $"{l1.Id} {l2.Id} {l1.Definition.Id} {l2.Definition.Id}");
            m.AddBullet("legacy");
            var mp = Path.Combine(outDir, "lists-maxid-out.docx");
            m.SaveTo(mp);
            var ids = XElement.Parse(ReadPartXml(mp, "word/numbering.xml")).Elements(Wn + "num").Select(e => e.Attribute(Wn + "numId")!.Value).ToList();
            AssertTrue(ids.Distinct().Count() == ids.Count && ids.All(i => int.Parse(i) > 0), string.Join(",", ids));
            var src = NumberingDefinition.Numbered();
            src.Nsid = "01020304";
            AssertTrue(src.Clone().Nsid is null, "nsid not cloned");
            var lst = d.Numbering.AddList(def);
            AssertTrue(!lst.TryGetOverride(0, out _) && lst.Overrides.Count == 0, "no side effect");
        });
        Check("clearing ListInstance does not bring back an earlier bullet; a level's character style (rStyle) round-trips", () =>
        {
            var d = new Document();
            var p = d.AddBullet("x");
            p.ListInstance = d.Numbering.AddNumberedList();
            p.ListInstance = null;
            AssertTrue(p.List == ListKind.None && d.GetListLabel(p) is null, "not a list item");
            var l = d.Numbering.AddBulletList();
            l.Definition.Level(0).RunStyleId = "Strong";
            d.AddListItem("y", l);
            var path = Path.Combine(outDir, "lists-rstyle.docx");
            d.SaveTo(path);
            AssertContains(ReadPartXml(path, "word/numbering.xml"), "<w:rStyle w:val=\"Strong\" />", "rStyle written");
            AssertTrue(Document.Load(path).Numbering.Lists[1].Definition.Levels[0].RunStyleId == "Strong", "rStyle read");
        });
        Check("a malformed numbering.xml throws XmlException (as styles.xml and document.xml do) instead of loading partly", () =>
        {
            var path = WithNumbering(outDir, "lists-malformed.docx", "<w:abstractNum w:abstractNumId=\"0\"><w:lvl></w:abstractNum>", "<w:p/>");
            AssertThrows<System.Xml.XmlException>(() => Document.Load(path), "malformed");
        });
    }

    // ---- helpers ------------------------------------------------------------------------

    /// <summary>A docx whose body, numbering part (and optionally extra styles) are given as XML.</summary>
    private static string WithNumbering(string outDir, string name, string numberingInner, string bodyInner, string? extraStyles = null,
        string? rootAttributes = null, byte[]? pictureBulletPng = null)
    {
        var path = Path.Combine(outDir, name);
        var d = new Document();
        d.AddParagraph("base");
        d.SaveTo(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        void Edit(string entry, Func<string, string> change)
        {
            var e = zip.GetEntry(entry)!;
            string xml;
            using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
            e.Delete();
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(change(xml));
        }
        void Create(string entry, string xml)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        Create("word/numbering.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"" + Wn.NamespaceName + "\" " + (rootAttributes ?? "") + ">" + numberingInner + "</w:numbering>");
        if (pictureBulletPng is not null)
        {
            using (var img = zip.CreateEntry("word/media/bullet1.png").Open()) img.Write(pictureBulletPng);
            Create("word/_rels/numbering.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/bullet1.png\"/></Relationships>");
            Edit("[Content_Types].xml", x => x.Contains("Extension=\"png\"") ? x : x.Replace("</Types>", "<Default Extension=\"png\" ContentType=\"image/png\"/></Types>"));
        }
        Edit("word/_rels/document.xml.rels", x => x.Replace("</Relationships>",
            "<Relationship Id=\"rIdNumX\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/></Relationships>"));
        Edit("[Content_Types].xml", x => x.Replace("</Types>",
            "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/></Types>"));
        Edit("word/document.xml", x =>
        {
            int start = x.IndexOf("<w:body>", StringComparison.Ordinal) + "<w:body>".Length;
            int sect = x.IndexOf("<w:sectPr", StringComparison.Ordinal);
            return x[..start] + bodyInner + x[sect..];
        });
        if (extraStyles is not null) Edit("word/styles.xml", x => x.Replace("</w:styles>", extraStyles + "</w:styles>"));
        return path;
    }

    private static string ReadPart(MemoryStream ms, string part)
    {
        using var z = new ZipArchive(new MemoryStream(ms.ToArray()));
        using var r = new StreamReader(z.GetEntry(part)!.Open());
        return r.ReadToEnd();
    }

    private static void AssertOrder(XElement parent, string[] order)
    {
        var names = parent.Elements().Select(e => e.Name.LocalName).ToList();
        var idx = names.Select(n => Array.IndexOf(order, n)).ToList();
        AssertTrue(idx.All(i => i >= 0), $"unknown child in {parent.Name.LocalName}: [{string.Join(",", names)}]");
        for (int i = 1; i < idx.Count; i++)
            AssertTrue(idx[i] > idx[i - 1], $"{parent.Name.LocalName} out of order: [{string.Join(",", names)}]");
    }
}
