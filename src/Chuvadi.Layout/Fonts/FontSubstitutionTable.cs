using System;
using System.Collections.Generic;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>Broad font classification used when a family has no explicit substitute (OOXML w:family / generic CSS families).</summary>
    public enum GenericFontFamily
    {
        Unknown,
        SansSerif,
        Serif,
        Monospace,
    }

    /// <summary>One substitute candidate for a requested family.</summary>
    public readonly record struct FontSubstitute(string Family, bool MetricCompatible);

    /// <summary>
    /// Maps requested families that may be unavailable to substitutes, and generic classes and scripts
    /// to fallback families. <see cref="CreateDefault"/> returns the table matching the Chuvadi.Layout.Fonts
    /// pack; every entry can be replaced or extended.
    /// </summary>
    public sealed class FontSubstitutionTable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, List<FontSubstitute>> _families = new Dictionary<string, List<FontSubstitute>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<GenericFontFamily, List<string>> _generic = new Dictionary<GenericFontFamily, List<string>>();
        private readonly Dictionary<UnicodeScript, List<string>> _scripts = new Dictionary<UnicodeScript, List<string>>();
        private readonly List<string> _lastResort = new List<string>();

        /// <summary>An empty table: no substitutes, no fallbacks.</summary>
        public FontSubstitutionTable()
        {
        }

        /// <summary>
        /// The default table. Metric-compatible: Arial/Helvetica → Liberation Sans, Times New Roman/Times →
        /// Liberation Serif, Courier New/Courier → Liberation Mono, Calibri → Carlito, Cambria → Caladea.
        /// Common Office families without a metric-compatible OFL equivalent map to the nearest design
        /// (not metric-compatible, reported as such). Indic scripts fall back to the matching LiPi Sans face.
        /// </summary>
        public static FontSubstitutionTable CreateDefault()
        {
            FontSubstitutionTable t = new FontSubstitutionTable();
            const string Sans = "Liberation Sans";
            const string Serif = "Liberation Serif";
            const string Mono = "Liberation Mono";

            foreach (string f in new[] { "Arial", "Helvetica", "Arial MT", "Helvetica Neue", "Arimo" })
            {
                t.SetSubstitutes(f, new FontSubstitute(Sans, true));
            }
            foreach (string f in new[] { "Times New Roman", "Times", "Times New Roman PS", "Tinos" })
            {
                t.SetSubstitutes(f, new FontSubstitute(Serif, true));
            }
            foreach (string f in new[] { "Courier New", "Courier", "Cousine" })
            {
                t.SetSubstitutes(f, new FontSubstitute(Mono, true));
            }
            t.SetSubstitutes("Calibri", new FontSubstitute("Carlito", true));
            t.SetSubstitutes("Cambria", new FontSubstitute("Caladea", true));

            foreach (string f in new[] { "Calibri Light", "Aptos", "Aptos Display", "Aptos Narrow", "Candara", "Corbel" })
            {
                t.SetSubstitutes(f, new FontSubstitute("Carlito", false));
            }
            foreach (string f in new[] { "Tahoma", "Verdana", "Trebuchet MS", "Century Gothic", "Gill Sans MT", "Franklin Gothic Book", "Lucida Sans Unicode", "Microsoft Sans Serif", "Arial Narrow", "Arial Unicode MS" })
            {
                t.SetSubstitutes(f, new FontSubstitute(Sans, false));
            }
            foreach (string f in new[] { "Cambria Math", "Constantia" })
            {
                t.SetSubstitutes(f, new FontSubstitute("Caladea", false));
            }
            foreach (string f in new[] { "Garamond", "Book Antiqua", "Palatino Linotype", "Century", "Bookman Old Style", "Baskerville Old Face" })
            {
                t.SetSubstitutes(f, new FontSubstitute(Serif, false));
            }
            foreach (string f in new[] { "Consolas", "Lucida Console", "Cascadia Code", "Cascadia Mono" })
            {
                t.SetSubstitutes(f, new FontSubstitute(Mono, false));
            }
            // D-116 A5. Georgia → Gelasio and Segoe UI → Selawik (Chuvadi.Layout.Fonts): closer designs than Liberation;
            // entered as non-metric until measured against the real fonts (ManualTests --compare-metrics). Segoe UI's
            // Light, Semilight and Semibold families map to the Selawik faces of the same weight, mapped by file.
            t.SetSubstitutes("Georgia", new FontSubstitute("Gelasio", false), new FontSubstitute(Serif, false));
            t.SetSubstitutes("Segoe UI", new FontSubstitute("Selawik", false), new FontSubstitute(Sans, false));
            t.SetSubstitutes("Segoe UI Light", new FontSubstitute("Selawik Light", false), new FontSubstitute(Sans, false));
            t.SetSubstitutes("Segoe UI Semilight", new FontSubstitute("Selawik Semilight", false), new FontSubstitute(Sans, false));
            t.SetSubstitutes("Segoe UI Semibold", new FontSubstitute("Selawik Semibold", false), new FontSubstitute(Sans, false));

            // D-116 A5. LiPi Sans Tamil and Devanagari are Noto Sans Tamil 2.004 and Devanagari 2.006 under another
            // name: identical advance widths for every Noto codepoint and identical vertical metrics, Regular and Bold
            // (three Devanagari Bold widths differ by 1/1000 em). An installed Noto font is still used first.
            t.SetSubstitutes("Noto Sans Tamil", new FontSubstitute("LiPi Sans Tamil", true));
            t.SetSubstitutes("Noto Sans Devanagari", new FontSubstitute("LiPi Sans Devanagari", true));

            foreach (string f in new[] { "Nirmala UI", "Nirmala Text" })
            {
                t.SetSubstitutes(f, new FontSubstitute("LiPi Sans", false));
            }

            t.SetGenericFallback(GenericFontFamily.SansSerif, Sans);
            t.SetGenericFallback(GenericFontFamily.Serif, Serif);
            t.SetGenericFallback(GenericFontFamily.Monospace, Mono);
            t.SetGenericFallback(GenericFontFamily.Unknown, Sans);

            t.SetScriptFallback(UnicodeScript.Latin, Sans, "LiPi Sans");
            t.SetScriptFallback(UnicodeScript.Greek, Sans);
            t.SetScriptFallback(UnicodeScript.Cyrillic, Sans);
            t.SetScriptFallback(UnicodeScript.Devanagari, "LiPi Sans Devanagari");
            t.SetScriptFallback(UnicodeScript.Bengali, "LiPi Sans Bengali");
            t.SetScriptFallback(UnicodeScript.Gurmukhi, "LiPi Sans Gurmukhi");
            t.SetScriptFallback(UnicodeScript.Gujarati, "LiPi Sans Gujarati");
            t.SetScriptFallback(UnicodeScript.Oriya, "LiPi Sans Odia");
            t.SetScriptFallback(UnicodeScript.Tamil, "LiPi Sans Tamil");
            t.SetScriptFallback(UnicodeScript.Telugu, "LiPi Sans Telugu");
            t.SetScriptFallback(UnicodeScript.Kannada, "LiPi Sans Kannada");
            t.SetScriptFallback(UnicodeScript.Malayalam, "LiPi Sans Malayalam");

            t.SetLastResort(Sans, "LiPi Sans");
            return t;
        }

        /// <summary>Replaces the substitutes for <paramref name="requestedFamily"/> (tried in order).</summary>
        public void SetSubstitutes(string requestedFamily, params FontSubstitute[] substitutes)
        {
            ArgumentNullException.ThrowIfNull(requestedFamily);
            ArgumentNullException.ThrowIfNull(substitutes);
            lock (_gate)
            {
                _families[requestedFamily] = new List<FontSubstitute>(substitutes);
            }
        }

        public void RemoveSubstitutes(string requestedFamily)
        {
            lock (_gate)
            {
                _families.Remove(requestedFamily);
            }
        }

        public IReadOnlyList<FontSubstitute> GetSubstitutes(string requestedFamily)
        {
            lock (_gate)
            {
                return _families.TryGetValue(requestedFamily, out List<FontSubstitute>? l) ? l.ToArray() : Array.Empty<FontSubstitute>();
            }
        }

        public void SetGenericFallback(GenericFontFamily generic, params string[] families)
        {
            lock (_gate)
            {
                _generic[generic] = new List<string>(families);
            }
        }

        public IReadOnlyList<string> GetGenericFallback(GenericFontFamily generic)
        {
            lock (_gate)
            {
                return _generic.TryGetValue(generic, out List<string>? l) ? l.ToArray() : Array.Empty<string>();
            }
        }

        public void SetScriptFallback(UnicodeScript script, params string[] families)
        {
            lock (_gate)
            {
                _scripts[script] = new List<string>(families);
            }
        }

        public IReadOnlyList<string> GetScriptFallback(UnicodeScript script)
        {
            lock (_gate)
            {
                return _scripts.TryGetValue(script, out List<string>? l) ? l.ToArray() : Array.Empty<string>();
            }
        }

        /// <summary>Families tried, in order, when nothing else covers a character.</summary>
        public void SetLastResort(params string[] families)
        {
            lock (_gate)
            {
                _lastResort.Clear();
                _lastResort.AddRange(families);
            }
        }

        public IReadOnlyList<string> GetLastResort()
        {
            lock (_gate)
            {
                return _lastResort.ToArray();
            }
        }
    }
}
