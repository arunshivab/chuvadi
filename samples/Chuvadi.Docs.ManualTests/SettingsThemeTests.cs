using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.1.0 settings model (D-296 R1, DOC-13; D-299 S-c): <see cref="Document.Compatibility"/> with the
/// compatibility mode, the 65 CT_Compat flags and the other compatSetting entries, the theme-font languages and
/// the layout/field settings, all read by Load and written by Save in CT_Settings order; a new document is in
/// compatibility mode 15.
/// </summary>
public static class SettingsTests
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>ECMA-376 CT_Compat, in schema order (written out here, independent of the library's own table).</summary>
    internal static readonly string[] CompatOrder =
    {
        "useSingleBorderforContiguousCells", "wpJustification", "noTabHangInd", "noLeading", "spaceForUL", "noColumnBalance",
        "balanceSingleByteDoubleByteWidth", "noExtraLineSpacing", "doNotLeaveBackslashAlone", "ulTrailSpace", "doNotExpandShiftReturn",
        "spacingInWholePoints", "lineWrapLikeWord6", "printBodyTextBeforeHeader", "printColBlack", "wpSpaceWidth", "showBreaksInFrames",
        "subFontBySize", "suppressBottomSpacing", "suppressTopSpacing", "suppressSpacingAtTopOfPage", "suppressTopSpacingWP",
        "suppressSpBfAfterPgBrk", "swapBordersFacingPages", "convMailMergeEsc", "truncateFontHeightsLikeWP6", "mwSmallCaps",
        "usePrinterMetrics", "doNotSuppressParagraphBorders", "wrapTrailSpaces", "footnoteLayoutLikeWW8", "shapeLayoutLikeWW8",
        "alignTablesRowByRow", "forgetLastTabAlignment", "adjustLineHeightInTable", "autoSpaceLikeWord95", "noSpaceRaiseLower",
        "doNotUseHTMLParagraphAutoSpacing", "layoutRawTableWidth", "layoutTableRowsApart", "useWord97LineBreakRules",
        "doNotBreakWrappedTables", "doNotSnapToGridInCell", "selectFldWithFirstOrLastChar", "applyBreakingRules",
        "doNotWrapTextWithPunct", "doNotUseEastAsianBreakRules", "useWord2002TableStyleRules", "growAutofit", "useFELayout",
        "useNormalStyleForList", "doNotUseIndentAsNumberingTabStop", "useAltKinsokuLineBreakRules", "allowSpaceOfSameStyleInTable",
        "doNotSuppressIndentation", "doNotAutofitConstrainedTables", "autofitToFirstFixedWidthCell", "underlineTabInNumList",
        "displayHangulFixedWidth", "splitPgBreakAndParaMark", "doNotVertAlignCellWithSp", "doNotBreakConstrainedForcedTable",
        "doNotVertAlignInTxbx", "useAnsiKerningPairs", "cachedColBalance",
    };

    /// <summary>The CT_Settings elements this library writes, in schema order.</summary>
    private static readonly string[] SettingsOrder =
    {
        "displayBackgroundShape", "embedTrueTypeFonts", "saveSubsetFonts", "mirrorMargins", "gutterAtTop", "trackRevisions",
        "documentProtection", "defaultTabStop", "autoHyphenation", "consecutiveHyphenLimit", "hyphenationZone", "doNotHyphenateCaps",
        "evenAndOddHeaders", "bookFoldRevPrinting", "bookFoldPrinting", "bookFoldPrintingSheets", "noPunctuationKerning",
        "characterSpacingControl", "updateFields", "footnotePr", "endnotePr", "compat", "themeFontLang", "clrSchemeMapping",
        "decimalSymbol", "listSeparator",
    };

    private const string WordUri = "http://schemas.microsoft.com/office/word";

    public static void Run(string outDir)
    {
        var dir = Path.Combine(outDir, "settings");
        Directory.CreateDirectory(dir);

        Check("a new document is in compatibility mode 15: settings.xml holds exactly one compatSetting compatibilityMode=15 with Word's uri (D-299)", () =>
        {
            var d = new Document();
            AssertTrue(d.Compatibility.Mode == 15, "Compatibility.Mode " + d.Compatibility.Mode);
            d.AddParagraph("mode 15");
            var bytes = Save(d);
            File.WriteAllBytes(Path.Combine(dir, "new-mode15.docx"), bytes);
            var compat = SettingsRoot(bytes).Element(W + "compat");
            AssertTrue(compat is not null && compat.Elements().Count() == 1, "one child in w:compat: " + compat);
            var cs = compat!.Element(W + "compatSetting")!;
            AssertTrue((string?)cs.Attribute(W + "name") == "compatibilityMode" && (string?)cs.Attribute(W + "uri") == WordUri && (string?)cs.Attribute(W + "val") == "15", cs.ToString());
            AssertTrue(Document.Load(new MemoryStream(bytes)).Compatibility.Mode == 15, "reloads as 15");
        });

        Check("Compatibility.Mode = null writes no w:compat (the 2.0.0 output)", () =>
        {
            var d = new Document();
            d.Compatibility.Mode = null;
            AssertTrue(SettingsRoot(Save(d)).Element(W + "compat") is null, "no w:compat");
        });

        Check("a loaded file without w:compat keeps none: Mode is null and a save writes none", () =>
        {
            var bytes = WithSettings(BaseDocx(), "<w:settings xmlns:w=\"" + W.NamespaceName + "\"><w:defaultTabStop w:val=\"720\"/></w:settings>");
            var d = Document.Load(new MemoryStream(bytes));
            AssertTrue(d.Compatibility.Mode is null && d.Compatibility.OtherSettings.Count == 0, "Mode " + d.Compatibility.Mode);
            AssertTrue(SettingsRoot(Save(d)).Element(W + "compat") is null, "no w:compat after save");
        });

        Check("all 65 CT_Compat flags round-trip and are written in schema order, before the compatSetting entries", () =>
        {
            var d = new Document();
            var props = FlagProperties();
            AssertTrue(props.Count == 65, "65 bool properties: " + props.Count);
            foreach (var p in props.Values) p.SetValue(d.Compatibility, true);
            d.Compatibility.OtherSettings.Add(new CompatibilitySetting("enableOpenTypeFeatures", WordUri, "1"));
            var bytes = Save(d);
            var names = SettingsRoot(bytes).Element(W + "compat")!.Elements().Select(e => e.Name.LocalName).ToList();
            var expected = CompatOrder.Concat(new[] { "compatSetting", "compatSetting" }).ToList();
            AssertTrue(names.SequenceEqual(expected), "order: " + string.Join(",", names));
            var back = Document.Load(new MemoryStream(bytes)).Compatibility;
            AssertTrue(props.Values.All(p => (bool)p.GetValue(back)!), "every flag reads back true");
            AssertTrue(back.Mode == 15 && back.OtherSettings.Count == 1 && back.OtherSettings[0].Name == "enableOpenTypeFeatures", "mode and other setting");
        });

        Check("each flag maps to its own element: setting one property writes only that element (all 65, one at a time)", () =>
        {
            var props = FlagProperties();
            var wrong = new List<string>();
            foreach (var element in CompatOrder)
            {
                var d = new Document();
                d.Compatibility.Mode = null;
                props[element].SetValue(d.Compatibility, true);
                var kids = SettingsRoot(Save(d)).Element(W + "compat")!.Elements().Select(e => e.Name.LocalName).ToList();
                if (kids.Count != 1 || kids[0] != element) wrong.Add(element + "→" + string.Join(",", kids));
            }
            AssertTrue(wrong.Count == 0, string.Join("; ", wrong));
        });

        Check("a Word file's w:compat round-trips: mode 14, three other settings in order, useFELayout and noColumnBalance; w:val=\"0\" reads as off", () =>
        {
            var xml = "<w:settings xmlns:w=\"" + W.NamespaceName + "\"><w:compat><w:noColumnBalance/><w:useFELayout/><w:growAutofit w:val=\"0\"/>"
                + "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"" + WordUri + "\" w:val=\"14\"/>"
                + "<w:compatSetting w:name=\"overrideTableStyleFontSizeAndJustification\" w:uri=\"" + WordUri + "\" w:val=\"1\"/>"
                + "<w:compatSetting w:name=\"enableOpenTypeFeatures\" w:uri=\"" + WordUri + "\" w:val=\"1\"/>"
                + "<w:compatSetting w:name=\"doNotFlipMirrorIndents\" w:uri=\"" + WordUri + "\" w:val=\"1\"/></w:compat></w:settings>";
            var d = Document.Load(new MemoryStream(WithSettings(BaseDocx(), xml)));
            var c = d.Compatibility;
            AssertTrue(c.Mode == 14 && c.NoColumnBalance && c.UseFELayout && !c.GrowAutofit, $"mode {c.Mode}, flags {c.NoColumnBalance}/{c.UseFELayout}/{c.GrowAutofit}");
            AssertTrue(string.Join(",", c.OtherSettings.Select(s => s.Name + "=" + s.Value + "@" + s.Uri)) ==
                $"overrideTableStyleFontSizeAndJustification=1@{WordUri},enableOpenTypeFeatures=1@{WordUri},doNotFlipMirrorIndents=1@{WordUri}", string.Join(",", c.OtherSettings));
            var saved = SettingsRoot(Save(d)).Element(W + "compat")!;
            var got = string.Join(",", saved.Elements().Select(e => e.Name.LocalName + ((string?)e.Attribute(W + "name") is string n ? ":" + n + "=" + (string?)e.Attribute(W + "val") : "")));
            AssertTrue(got == "noColumnBalance,useFELayout,compatSetting:compatibilityMode=14,compatSetting:overrideTableStyleFontSizeAndJustification=1,compatSetting:enableOpenTypeFeatures=1,compatSetting:doNotFlipMirrorIndents=1", got);
        });

        Check("a compatibilityMode that is not a positive number, or under another uri, is kept as found in OtherSettings", () =>
        {
            var xml = "<w:settings xmlns:w=\"" + W.NamespaceName + "\"><w:compat>"
                + "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"" + WordUri + "\" w:val=\"abc\"/>"
                + "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"urn:other\" w:val=\"7\"/></w:compat></w:settings>";
            var c = Document.Load(new MemoryStream(WithSettings(BaseDocx(), xml))).Compatibility;
            AssertTrue(c.Mode is null && c.OtherSettings.Count == 2 && c.OtherSettings[0].Value == "abc" && c.OtherSettings[1].Uri == "urn:other", string.Join(",", c.OtherSettings));
        });

        Check("theme-font languages, colour mapping and the layout/field settings round-trip, written in CT_Settings order", () =>
        {
            var d = new Document();
            d.DisplayBackgroundShape = true;
            d.AutoHyphenation = true;
            d.ConsecutiveHyphenLimit = 2;
            d.HyphenationZonePt = 21.25;
            d.DoNotHyphenateCaps = true;
            d.BookFoldPrinting = true;
            d.BookFoldReversePrinting = true;
            d.BookFoldPrintingSheets = 8;
            d.NoPunctuationKerning = true;
            d.CharacterSpacingControl = CharacterSpacingControl.CompressPunctuationAndJapaneseKana;
            d.DecimalSymbol = ",";
            d.ListSeparator = ";";
            d.ThemeFontLanguages.Latin = "en-IN";
            d.ThemeFontLanguages.EastAsia = "ja-JP";
            d.ThemeFontLanguages.ComplexScript = "ta-IN";
            d.ColorSchemeMapping.Text1 = ColorSchemeIndex.Light1;
            d.ColorSchemeMapping.Background1 = ColorSchemeIndex.Dark1;
            d.DefaultTabStopPt = 36;
            d.MirrorMargins = true;
            d.AddParagraph("settings");
            var bytes = Save(d);
            File.WriteAllBytes(Path.Combine(dir, "all-settings.docx"), bytes);
            var root = SettingsRoot(bytes);
            var names = root.Elements().Select(e => e.Name.LocalName).ToList();
            var positions = names.Select(n => Array.IndexOf(SettingsOrder, n)).ToList();
            AssertTrue(positions.All(p => p >= 0) && positions.Zip(positions.Skip(1)).All(t => t.First < t.Second), "order: " + string.Join(",", names));
            var lang = root.Element(W + "themeFontLang")!;
            AssertTrue((string?)lang.Attribute(W + "val") == "en-IN" && (string?)lang.Attribute(W + "eastAsia") == "ja-JP" && (string?)lang.Attribute(W + "bidi") == "ta-IN", lang.ToString());
            AssertTrue((string?)root.Element(W + "hyphenationZone")!.Attribute(W + "val") == "425", "hyphenationZone in twips");
            var map = root.Element(W + "clrSchemeMapping")!;
            AssertTrue(map.Attributes().Count() == 2 && (string?)map.Attribute(W + "t1") == "light1" && (string?)map.Attribute(W + "bg1") == "dark1", map.ToString());

            var b = Document.Load(new MemoryStream(bytes));
            AssertTrue(b.DisplayBackgroundShape && b.AutoHyphenation && b.ConsecutiveHyphenLimit == 2 && b.HyphenationZonePt == 21.25 && b.DoNotHyphenateCaps
                && b.BookFoldPrinting && b.BookFoldReversePrinting && b.BookFoldPrintingSheets == 8 && b.NoPunctuationKerning
                && b.CharacterSpacingControl == CharacterSpacingControl.CompressPunctuationAndJapaneseKana && b.DecimalSymbol == "," && b.ListSeparator == ";",
                "the layout/field settings read back");
            AssertTrue(b.ThemeFontLanguages.Latin == "en-IN" && b.ThemeFontLanguages.EastAsia == "ja-JP" && b.ThemeFontLanguages.ComplexScript == "ta-IN", "languages read back");
            AssertTrue(b.ColorSchemeMapping.Text1 == ColorSchemeIndex.Light1 && b.ColorSchemeMapping.Background1 == ColorSchemeIndex.Dark1 && b.ColorSchemeMapping.Accent1 is null, "mapping reads back");
            var again = Save(b);
            AssertTrue(XNode.DeepEquals(SettingsRoot(again), SettingsRoot(bytes)), "a second load → save writes the same settings.xml");
        });

        Check("a default new document writes none of the new settings except w:compat", () =>
        {
            var names = SettingsRoot(Save(new Document())).Elements().Select(e => e.Name.LocalName).ToList();
            AssertTrue(names.SequenceEqual(new[] { "compat" }), string.Join(",", names));
        });

        Check("a malformed settings part still loads, with no compatibility settings", () =>
        {
            var bytes = WithSettings(BaseDocx(), "<w:settings xmlns:w=\"" + W.NamespaceName + "\"><w:compat><w:noColumnBalance/>");
            var d = Document.Load(new MemoryStream(bytes));
            AssertTrue(d.GetText().Contains("base"), "loads");
            AssertTrue(d.Compatibility.Mode is null, "Mode " + d.Compatibility.Mode);
        });

        Check("argument checks: Mode 0 or negative, a negative hyphen limit or booklet size, a hyphenation zone over 1584pt, a null CompatibilitySetting part", () =>
        {
            var d = new Document();
            AssertThrows<ArgumentOutOfRangeException>(() => d.Compatibility.Mode = 0, "Mode 0");
            AssertThrows<ArgumentOutOfRangeException>(() => d.Compatibility.Mode = -1, "Mode -1");
            AssertThrows<ArgumentOutOfRangeException>(() => d.ConsecutiveHyphenLimit = -1, "hyphen limit");
            AssertThrows<ArgumentOutOfRangeException>(() => d.BookFoldPrintingSheets = -1, "booklet");
            AssertThrows<ArgumentOutOfRangeException>(() => d.HyphenationZonePt = 1600, "zone");
            AssertThrows<ArgumentOutOfRangeException>(() => d.HyphenationZonePt = double.NaN, "zone NaN");
            AssertThrows<ArgumentNullException>(() => new CompatibilitySetting(null!, WordUri, "1"), "name");
            AssertThrows<ArgumentNullException>(() => new CompatibilitySetting("x", null!, "1"), "uri");
            AssertThrows<ArgumentNullException>(() => new CompatibilitySetting("x", WordUri, null!), "value");
            AssertTrue(d.Compatibility.Mode == 15, "unchanged after the failed sets");
        });
    }

    internal static Dictionary<string, System.Reflection.PropertyInfo> FlagProperties()
    {
        var map = new Dictionary<string, System.Reflection.PropertyInfo>(StringComparer.Ordinal);
        foreach (var p in typeof(CompatibilityOptions).GetProperties().Where(p => p.PropertyType == typeof(bool)))
            map[char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)] = p;
        return map;
    }

    internal static byte[] Save(Document d)
    {
        var ms = new MemoryStream();
        d.SaveTo(ms);
        return ms.ToArray();
    }

    internal static byte[] BaseDocx()
    {
        var d = new Document();
        d.AddParagraph("base");
        return Save(d);
    }

    internal static XElement SettingsRoot(byte[] docx)
    {
        using var z = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var s = z.GetEntry("word/settings.xml")!.Open();
        return XDocument.Load(s).Root!;
    }

    internal static byte[] WithSettings(byte[] docx, string settingsXml) => WithPart(docx, "word/settings.xml", settingsXml);

    internal static byte[] WithPart(byte[] docx, string name, string xml)
    {
        var ms = new MemoryStream();
        ms.Write(docx);
        using (var z = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            z.GetEntry(name)?.Delete();
            using var w = z.CreateEntry(name).Open();
            w.Write(new UTF8Encoding(false).GetBytes(xml));
        }
        return ms.ToArray();
    }
}

/// <summary>
/// Docs 2.1.0 theme model (D-296 R2, P5): <see cref="Document.Theme"/>, <see cref="Document.GetThemeFontName(ThemeFont)"/>
/// with the theme-font languages, and <see cref="Document.GetThemeColorHex(ThemeColor, byte?, byte?)"/> with the colour
/// mapping, tint and shade.
/// </summary>
public static class ThemeTests
{
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    internal static string ThemeXml(string majorEa = "", string minorEa = "", string minorCs = "", string extraMinorScripts = "",
        string accent1 = "<a:srgbClr val=\"4472C4\"/>", string objectDefaults = "") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
        + "<a:theme xmlns:a=\"" + A + "\" name=\"Test Theme\"><a:themeElements>"
        + "<a:clrScheme name=\"T\"><a:dk1><a:sysClr val=\"windowText\" lastClr=\"101010\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FEFEFE\"/></a:lt1>"
        + "<a:dk2><a:srgbClr val=\"222222\"/></a:dk2><a:lt2><a:srgbClr val=\"EEEEEE\"/></a:lt2>"
        + "<a:accent1>" + accent1 + "</a:accent1><a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2><a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3>"
        + "<a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4><a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6>"
        + "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme>"
        + "<a:fontScheme name=\"T\"><a:majorFont><a:latin typeface=\"Cambria\"/><a:ea typeface=\"" + majorEa + "\"/><a:cs typeface=\"\"/>"
        + "<a:font script=\"Jpan\" typeface=\"MS Gothic\"/><a:font script=\"Hans\" typeface=\"SimHei\"/></a:majorFont>"
        + "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"" + minorEa + "\"/><a:cs typeface=\"" + minorCs + "\"/>"
        + "<a:font script=\"Jpan\" typeface=\"MS Mincho\"/><a:font script=\"Hans\" typeface=\"SimSun\"/><a:font script=\"Hant\" typeface=\"PMingLiU\"/>"
        + "<a:font script=\"Arab\" typeface=\"Times New Roman\"/><a:font script=\"Taml\" typeface=\"Latha\"/><a:font script=\"Mlym\" typeface=\"Kartika\"/>"
        + "<a:font script=\"Uigh\" typeface=\"Microsoft Uighur\"/>" + extraMinorScripts + "</a:minorFont></a:fontScheme>"
        + "<a:fmtScheme name=\"T\"/></a:themeElements>" + objectDefaults + "</a:theme>";

    private static Document WithTheme(string xml)
    {
        var d = new Document();
        d.ThemeXml = Encoding.UTF8.GetBytes(xml);
        return d;
    }

    public static void Run(string outDir)
    {
        Check("no theme part: the Office 2013–2022 defaults (Calibri Light / Calibri, no East Asian or complex-script font), IsBuiltInDefault", () =>
        {
            var d = new Document();
            var t = d.Theme;
            AssertTrue(t.IsBuiltInDefault && t.Name is null && t.MajorFonts.Latin == "Calibri Light" && t.MinorFonts.Latin == "Calibri", "defaults");
            var names = Enum.GetValues<ThemeFont>().Select(f => f + "=" + (d.GetThemeFontName(f) ?? "null"));
            AssertTrue(string.Join(",", names) == "MajorEastAsia=null,MajorBidi=null,MajorAscii=Calibri Light,MajorHAnsi=Calibri Light,MinorEastAsia=null,MinorBidi=null,MinorAscii=Calibri,MinorHAnsi=Calibri", string.Join(",", names));
            AssertTrue(string.Join(",", t.Colors.OrderBy(c => c.Key).Select(c => c.Value)) == "000000,FFFFFF,44546A,E7E6E6,4472C4,ED7D31,A5A5A5,FFC000,5B9BD5,70AD47,0563C1,954F72", "default colours");
            AssertTrue(d.GetThemeColorHex(ThemeColor.Text1) == "000000" && d.GetThemeColorHex(ThemeColor.Background1) == "FFFFFF", "text/background");
        });

        Check("a theme part: name, Latin/East Asian/complex-script typefaces (empty = null), and the script fonts per scheme", () =>
        {
            var d = WithTheme(ThemeXml(minorEa: "Yu Mincho", minorCs: "Arial"));
            var t = d.Theme;
            AssertTrue(!t.IsBuiltInDefault && t.Name == "Test Theme", "name " + t.Name);
            AssertTrue(t.MajorFonts.Latin == "Cambria" && t.MajorFonts.EastAsia is null && t.MajorFonts.ComplexScript is null, "major");
            AssertTrue(t.MinorFonts.Latin == "Calibri" && t.MinorFonts.EastAsia == "Yu Mincho" && t.MinorFonts.ComplexScript == "Arial", "minor");
            AssertTrue(t.MajorFonts.ScriptFonts.Count == 2 && t.MinorFonts.ScriptFonts.Count == 7 && t.MinorFonts.ScriptFonts["Taml"] == "Latha", "script fonts");
            var names = string.Join(",", Enum.GetValues<ThemeFont>().Select(f => f + "=" + (d.GetThemeFontName(f) ?? "null")));
            AssertTrue(names == "MajorEastAsia=null,MajorBidi=null,MajorAscii=Cambria,MajorHAnsi=Cambria,MinorEastAsia=Yu Mincho,MinorBidi=Arial,MinorAscii=Calibri,MinorHAnsi=Calibri", names);
        });

        Check("theme-font languages pick the script font (ECMA-376 §17.15.1.88): ja-JP, zh-CN, zh-TW, ta-IN, ml-IN, ar-SA, ug-CN, pa-Arab; Latin and Cyrillic languages keep the scheme fonts", () =>
        {
            var d = WithTheme(ThemeXml(minorEa: "Yu Mincho", minorCs: "Arial"));
            string Ea(string lang) { d.ThemeFontLanguages.EastAsia = lang; return d.GetThemeFontName(ThemeFont.MinorEastAsia) ?? "null"; }
            string Cs(string lang) { d.ThemeFontLanguages.ComplexScript = lang; return d.GetThemeFontName(ThemeFont.MinorBidi) ?? "null"; }
            string Latin(string lang) { d.ThemeFontLanguages.Latin = lang; return d.GetThemeFontName(ThemeFont.MinorAscii) ?? "null"; }
            var got = string.Join(",", Ea("ja-JP"), Ea("zh-CN"), Ea("zh-TW"), Ea("zh-HK"), Ea("ko-KR"), Cs("ta-IN"), Cs("ml-IN"), Cs("ar-SA"), Cs("ug-CN"), Cs("pa-Arab-PK"),
                Cs("he-IL"), Latin("en-US"), Latin("ru-RU"), Latin("sr-Cyrl-RS"), Latin("ja-JP"));
            AssertTrue(got == "MS Mincho,SimSun,PMingLiU,PMingLiU,Yu Mincho,Latha,Kartika,Times New Roman,Microsoft Uighur,Times New Roman,Arial,Calibri,Calibri,Calibri,MS Mincho", got);
            d.ThemeFontLanguages.EastAsia = "ja-JP";
            AssertTrue(d.GetThemeFontName(ThemeFont.MajorEastAsia) == "MS Gothic", "major scheme uses the major script fonts");
        });

        Check("an empty East Asian typeface with no matching script font stays null (the caller falls back to the run's own font)", () =>
        {
            var d = WithTheme(ThemeXml());
            d.ThemeFontLanguages.EastAsia = "ko-KR";
            AssertTrue(d.GetThemeFontName(ThemeFont.MinorEastAsia) is null, "ko-KR without a Hang font");
            d.ThemeFontLanguages.ComplexScript = "ta-IN";
            AssertTrue(d.GetThemeFontName(ThemeFont.MajorBidi) is null, "major has no Taml font");
        });

        Check("colours: system colours give lastClr; Text1/Background1 go through the colour mapping; tint and shade on the HSL lightness", () =>
        {
            var d = WithTheme(ThemeXml());
            AssertTrue(d.Theme.Colors[ThemeColor.Dark1] == "101010" && d.Theme.Colors[ThemeColor.Light1] == "FEFEFE", "sysClr lastClr");
            AssertTrue(d.GetThemeColorHex(ThemeColor.Text1) == "101010" && d.GetThemeColorHex(ThemeColor.Text2) == "222222" && d.GetThemeColorHex(ThemeColor.Background2) == "EEEEEE", "default mapping");
            AssertTrue(d.GetThemeColorHex(ThemeColor.Accent1, tint: 0x99) == "8FAADC" && d.GetThemeColorHex(ThemeColor.Accent1, shade: 0xBF) == "2F5496", "tint/shade");
            AssertTrue(d.GetThemeColorHex(ThemeColor.Accent2, tint: 0x66, shade: 0xBF) == "EF8E4C" && d.GetThemeColorHex(ThemeColor.Accent1, tint: 255, shade: 255) == "4472C4", "both, and 255 = unchanged");
            d.ColorSchemeMapping.Text1 = ColorSchemeIndex.Light1;
            d.ColorSchemeMapping.Accent1 = ColorSchemeIndex.Accent2;
            AssertTrue(d.GetThemeColorHex(ThemeColor.Text1) == "FEFEFE" && d.GetThemeColorHex(ThemeColor.Accent1) == "ED7D31" && d.GetThemeColorHex(ThemeColor.Dark1) == "101010", "mapped");
        });

        Check("setting ThemeXml re-parses; a malformed theme gives the defaults; a:latin outside the font scheme is ignored", () =>
        {
            var d = WithTheme(ThemeXml());
            AssertTrue(d.Theme.MajorFonts.Latin == "Cambria", "first");
            d.ThemeXml = Encoding.UTF8.GetBytes(ThemeXml().Replace("Cambria", "Georgia"));
            AssertTrue(d.Theme.MajorFonts.Latin == "Georgia" && d.GetThemeFontName(ThemeFont.MajorAscii) == "Georgia", "re-parsed");
            d.ThemeXml = Encoding.UTF8.GetBytes("<a:theme xmlns:a=\"" + A + "\"><a:themeElements><a:fontScheme><a:majorFont><a:latin typeface=\"X\"/>");
            AssertTrue(d.Theme.IsBuiltInDefault && d.Theme.MajorFonts.Latin == "Calibri Light", "malformed → defaults");
            d.ThemeXml = Encoding.UTF8.GetBytes(ThemeXml(objectDefaults: "<a:objectDefaults><a:txDef><a:spPr/><a:bodyPr/><a:lstStyle><a:defPPr><a:defRPr><a:latin typeface=\"Comic Sans MS\"/></a:defRPr></a:defPPr></a:lstStyle></a:txDef></a:objectDefaults>"));
            AssertTrue(d.Theme.MinorFonts.Latin == "Calibri", "object defaults ignored: " + d.Theme.MinorFonts.Latin);
            d.ThemeXml = null;
            AssertTrue(d.Theme.IsBuiltInDefault, "null → defaults");
        });

        Check("a loaded document's theme and languages resolve after Load; undefined enum values are rejected", () =>
        {
            var d = WithTheme(ThemeXml(minorCs: "Arial"));
            d.ThemeFontLanguages.ComplexScript = "ta-IN";
            d.AddParagraph("theme");
            var back = Document.Load(new MemoryStream(SettingsTests.Save(d)));
            AssertTrue(back.GetThemeFontName(ThemeFont.MinorBidi) == "Latha" && back.Theme.Name == "Test Theme", back.GetThemeFontName(ThemeFont.MinorBidi) ?? "null");
            AssertThrows<ArgumentOutOfRangeException>(() => back.GetThemeFontName((ThemeFont)99), "font");
            AssertThrows<ArgumentOutOfRangeException>(() => back.GetThemeColorHex((ThemeColor)99), "colour");
        });
    }
}
