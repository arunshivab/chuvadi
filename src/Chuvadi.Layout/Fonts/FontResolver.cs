using System;
using System.Collections.Generic;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>What a requested family/style was resolved to, ordered from best to worst.</summary>
    public enum FontMatchKind
    {
        /// <summary>The requested family was found (the style may still be synthesised; see <see cref="FontResolution"/>).</summary>
        Exact,

        /// <summary>A metric-compatible substitute (same advance widths and line metrics): breaks stay faithful.</summary>
        MetricCompatibleSubstitute,

        /// <summary>A substitute of similar design that is not metric-compatible: line and page breaks may move.</summary>
        Substitute,

        /// <summary>The generic class fallback (sans-serif / serif / monospace).</summary>
        GenericFallback,

        /// <summary>A face chosen because the requested face lacks glyphs for the text's script.</summary>
        ScriptFallback,

        /// <summary>The last-resort face.</summary>
        LastResort,
    }

    /// <summary>A font request: family name plus style. Weight follows CSS (400 normal, 700 bold).</summary>
    public readonly record struct FontRequest(string Family, int Weight = 400, bool Italic = false, GenericFontFamily Generic = GenericFontFamily.Unknown)
    {
        public bool Bold => Weight >= 600;
    }

    /// <summary>The face chosen for a request, how it was chosen, and what has to be synthesised.</summary>
    public sealed record FontResolution(
        FontFace Face,
        FontRequest Request,
        FontMatchKind Kind,
        bool SyntheticBold,
        bool SyntheticItalic)
    {
        /// <summary>True when line breaks computed with this face match those of the requested font.</summary>
        public bool IsMetricFaithful => Kind is FontMatchKind.Exact or FontMatchKind.MetricCompatibleSubstitute;
    }

    /// <summary>A reported substitution: the family a document asked for and what was used instead.</summary>
    public sealed record FontSubstitution(
        string RequestedFamily,
        string UsedFamily,
        string UsedFace,
        FontMatchKind Kind,
        bool MetricCompatible,
        FontOrigin UsedOrigin,
        UnicodeScript Script);

    /// <summary>
    /// Resolves font requests against an ordered list of sources and a substitution table, and records every
    /// substitution. Resolution order: the requested family in each source in order (callers put document-embedded
    /// fonts first, then supplied fonts, then bundled packs, then — only if allowed — system fonts); then the
    /// family's substitutes; then the generic fallback; then the last resort. Character coverage is handled by
    /// <see cref="ResolveForCodepoint"/>, which adds the script fallback. Thread-safe; faces are cached.
    /// </summary>
    public sealed class FontResolver
    {
        private readonly object _gate = new object();
        private readonly IReadOnlyList<IFontSource> _sources;
        private readonly Dictionary<string, (IFontSource Source, List<FontFaceDescriptor> Faces)?> _familyIndex = new Dictionary<string, (IFontSource, List<FontFaceDescriptor>)?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(IFontSource, string, int), FontFace> _faceCache = new Dictionary<(IFontSource, string, int), FontFace>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<FontRequest, FontResolution> _resolutionCache = new System.Collections.Concurrent.ConcurrentDictionary<FontRequest, FontResolution>();
        private readonly List<FontSubstitution> _substitutions = new List<FontSubstitution>();
        private readonly HashSet<(string, string, UnicodeScript)> _reported = new HashSet<(string, string, UnicodeScript)>();
        private List<FontFaceDescriptor>? _allFaces;

        public FontResolver(IEnumerable<IFontSource> sources, FontSubstitutionTable? substitutions = null)
        {
            ArgumentNullException.ThrowIfNull(sources);
            _sources = new List<IFontSource>(sources);
            Substitutions = substitutions ?? FontSubstitutionTable.CreateDefault();
        }

        public FontSubstitutionTable Substitutions { get; }

        public IReadOnlyList<IFontSource> Sources => _sources;

        /// <summary>Every distinct substitution made so far, in the order first made.</summary>
        public IReadOnlyList<FontSubstitution> ReportedSubstitutions
        {
            get
            {
                lock (_gate)
                {
                    return _substitutions.ToArray();
                }
            }
        }

        /// <summary>Resolves a family and style to a face.</summary>
        /// <exception cref="LayoutException">No source supplies any face at all.</exception>
        public FontResolution Resolve(FontRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Family))
            {
                request = request with { Family = string.Empty };
            }
            // Lock-free hit path: resolution runs once per cluster run while measuring.
            if (_resolutionCache.TryGetValue(request, out FontResolution? hit))
            {
                return hit;
            }
            lock (_gate)
            {
                if (_resolutionCache.TryGetValue(request, out FontResolution? cached))
                {
                    return cached;
                }
                FontResolution r = ResolveCore(request);
                _resolutionCache[request] = r;
                if (r.Kind != FontMatchKind.Exact)
                {
                    Report(request.Family, r, UnicodeScript.Common);
                }
                return r;
            }
        }

        /// <summary>
        /// Resolves the face to draw <paramref name="codepoint"/> with, given the run's primary resolution.
        /// Returns <paramref name="primary"/> when it has a glyph; otherwise the script fallback, then any
        /// face of the last-resort families that has the glyph. Returns <paramref name="primary"/> (which will
        /// draw .notdef) when nothing covers the character.
        /// </summary>
        public FontResolution ResolveForCodepoint(FontResolution primary, int codepoint)
        {
            ArgumentNullException.ThrowIfNull(primary);
            if (primary.Face.HasGlyph(codepoint) || IsDefaultIgnorable(codepoint))
            {
                return primary;
            }
            UnicodeScript script = UnicodeScripts.GetScript(codepoint);
            lock (_gate)
            {
                List<string> families = new List<string>(Substitutions.GetScriptFallback(script));
                families.AddRange(Substitutions.GetLastResort());
                foreach (string family in families)
                {
                    FontResolution? r = TryFamily(primary.Request with { Family = family }, FontMatchKind.ScriptFallback);
                    if (r is not null && r.Face.HasGlyph(codepoint))
                    {
                        Report(primary.Request.Family, r, script);
                        return r;
                    }
                }
            }
            return primary;
        }

        private static bool IsDefaultIgnorable(int cp)
            => cp is 0x00AD or 0x034F or 0x061C or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or 0x3164 or 0xFEFF or 0xFFA0
               || (cp >= 0x180B && cp <= 0x180F) || (cp >= 0x200B && cp <= 0x200F) || (cp >= 0x202A && cp <= 0x202E)
               || (cp >= 0x2060 && cp <= 0x206F) || (cp >= 0xFE00 && cp <= 0xFE0F) || (cp >= 0xE0000 && cp <= 0xE0FFF)
               || cp < 0x20 || (cp >= 0x7F && cp < 0xA0);

        private FontResolution ResolveCore(FontRequest request)
        {
            if (request.Family.Length > 0)
            {
                FontResolution? exact = TryFamily(request, FontMatchKind.Exact);
                if (exact is not null)
                {
                    return exact;
                }
                foreach (FontSubstitute s in Substitutions.GetSubstitutes(request.Family))
                {
                    FontResolution? sub = TryFamily(request with { Family = s.Family },
                        s.MetricCompatible ? FontMatchKind.MetricCompatibleSubstitute : FontMatchKind.Substitute);
                    if (sub is not null)
                    {
                        return sub with { Request = request };
                    }
                }
            }
            foreach (string f in Substitutions.GetGenericFallback(request.Generic))
            {
                FontResolution? g = TryFamily(request with { Family = f }, FontMatchKind.GenericFallback);
                if (g is not null)
                {
                    return g with { Request = request };
                }
            }
            foreach (string f in Substitutions.GetLastResort())
            {
                FontResolution? l = TryFamily(request with { Family = f }, FontMatchKind.LastResort);
                if (l is not null)
                {
                    return l with { Request = request };
                }
            }
            // Anything at all.
            List<FontFaceDescriptor> all = AllFaces();
            if (all.Count == 0)
            {
                throw new LayoutException("No fonts are available: LayoutOptions.UseBuiltInFonts is off and no other font source is configured (for example the Chuvadi.Layout.Fonts pack, or AllowSystemFonts).");
            }
            FontResolution? any = TryFamily(request with { Family = all[0].FamilyName }, FontMatchKind.LastResort);
            return any! with { Request = request };
        }

        private List<FontFaceDescriptor> AllFaces()
        {
            if (_allFaces is null)
            {
                _allFaces = new List<FontFaceDescriptor>();
                foreach (IFontSource s in _sources)
                {
                    _allFaces.AddRange(s.GetFaces());
                }
            }
            return _allFaces;
        }

        private FontResolution? TryFamily(FontRequest request, FontMatchKind kind)
        {
            (IFontSource Source, List<FontFaceDescriptor> Faces)? entry = FindFamily(request.Family);
            if (entry is null)
            {
                return null;
            }
            FontFaceDescriptor d = PickStyle(entry.Value.Faces, request.Weight, request.Italic);
            FontFace face = Load(entry.Value.Source, d);
            bool synthBold = request.Weight >= 600 && face.Weight < 600;
            bool synthItalic = request.Italic && !face.IsItalic;
            return new FontResolution(face, request, kind, synthBold, synthItalic);
        }

        private (IFontSource, List<FontFaceDescriptor>)? FindFamily(string family)
        {
            if (_familyIndex.TryGetValue(family, out (IFontSource, List<FontFaceDescriptor>)? hit))
            {
                return hit;
            }
            (IFontSource, List<FontFaceDescriptor>)? found = null;
            foreach (IFontSource source in _sources)
            {
                List<FontFaceDescriptor> legacy = new List<FontFaceDescriptor>();
                List<FontFaceDescriptor> typographic = new List<FontFaceDescriptor>();
                foreach (FontFaceDescriptor d in source.GetFaces())
                {
                    if (string.Equals(d.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                    {
                        legacy.Add(d);
                    }
                    else if (string.Equals(d.TypographicFamilyName, family, StringComparison.OrdinalIgnoreCase))
                    {
                        typographic.Add(d);
                    }
                }
                // OOXML names legacy families ("Calibri Light"); prefer them, then typographic ones.
                List<FontFaceDescriptor> faces = legacy.Count > 0 ? legacy : typographic;
                if (faces.Count > 0)
                {
                    found = (source, faces);
                    break;
                }
            }
            _familyIndex[family] = found;
            return found;
        }

        /// <summary>CSS Fonts Level 4 font-style then font-weight matching (normal width only).</summary>
        internal static FontFaceDescriptor PickStyle(IReadOnlyList<FontFaceDescriptor> faces, int weight, bool italic)
        {
            List<FontFaceDescriptor> styled = new List<FontFaceDescriptor>();
            foreach (FontFaceDescriptor d in faces)
            {
                if (d.IsItalic == italic)
                {
                    styled.Add(d);
                }
            }
            if (styled.Count == 0)
            {
                styled.AddRange(faces);
            }
            // Prefer normal width (5), else nearest.
            int bestWidth = int.MaxValue;
            foreach (FontFaceDescriptor d in styled)
            {
                bestWidth = Math.Min(bestWidth, Math.Abs(d.WidthClass - 5));
            }
            styled.RemoveAll(d => Math.Abs(d.WidthClass - 5) != bestWidth);

            FontFaceDescriptor? best = null;
            int bestScore = int.MaxValue;
            foreach (FontFaceDescriptor d in styled)
            {
                int score = WeightDistance(weight, d.Weight);
                if (score < bestScore)
                {
                    best = d;
                    bestScore = score;
                }
            }
            return best!;
        }

        private static int WeightDistance(int desired, int available)
        {
            if (available == desired)
            {
                return 0;
            }
            if (desired >= 400 && desired <= 500)
            {
                if (available > desired && available <= 500)
                {
                    return available - desired;
                }
                if (available < desired)
                {
                    return 1000 + (desired - available);
                }
                return 2000 + (available - desired);
            }
            if (desired < 400)
            {
                return available < desired ? desired - available : 1000 + (available - desired);
            }
            return available > desired ? available - desired : 1000 + (desired - available);
        }

        private FontFace Load(IFontSource source, FontFaceDescriptor d)
        {
            (IFontSource, string, int) key = (source, d.Key, d.FaceIndex);
            if (!_faceCache.TryGetValue(key, out FontFace? face))
            {
                face = source.LoadFace(d);
                _faceCache[key] = face;
            }
            return face;
        }

        private void Report(string requested, FontResolution r, UnicodeScript script)
        {
            string used = r.Face.FamilyName;
            if (_reported.Add((requested, used, script)))
            {
                _substitutions.Add(new FontSubstitution(requested, used, r.Face.FullName, r.Kind,
                    r.Kind == FontMatchKind.MetricCompatibleSubstitute, r.Face.Origin, script));
            }
        }
    }
}
