using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Chuvadi.Fonts;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>
    /// The substitute fonts, embedded in this assembly: metric-compatible stand-ins for fonts documents often name
    /// but a machine may lack (Liberation Sans, Serif and Mono for Arial, Times New Roman and Courier New; Carlito for
    /// Calibri; Caladea for Cambria) and closer designs for Georgia (Gelasio) and Segoe UI (Selawik). Lists the files
    /// and hands out their bytes and licences; it parses nothing and caches nothing, and references no layout engine,
    /// so a PDF or print library can use the files too. No disk or network access. Thread-safe.
    /// </summary>
    /// <remarks>
    /// To lay out with these fonts, add the pack to the layout options as a font source:
    /// <c>options.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance))</c> (Chuvadi.Layout).
    /// </remarks>
    public static class SubstituteFontPack
    {
        private const string FontPrefix = "Chuvadi.Layout.Fonts.";
        private const string LicensePrefix = "Chuvadi.Layout.Fonts.Licenses.";

        private static readonly Dictionary<string, (FontFileInfo File, string License)> ByName = BuildIndex();
        private static readonly Dictionary<string, string> LicenseCache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Every font file in the pack, ordered by file name.</summary>
        public static IReadOnlyList<FontFileInfo> Files { get; } = BuildFiles();

        /// <summary>The pack as an <see cref="IFontFilePack"/> (name "Chuvadi.Layout.Fonts").</summary>
        public static IFontFilePack Instance { get; } = new Pack();

        /// <summary>The file with this name (for example "Carlito-Regular.ttf", ignoring case), or null.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        public static FontFileInfo? Find(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            return ByName.TryGetValue(fileName, out (FontFileInfo File, string License) e) ? e.File : null;
        }

        /// <summary>
        /// The faces of one family, ignoring case, matched against both the family and the typographic family:
        /// "Selawik" gives all five Selawik files, "Selawik Light" only that one. Empty when there are none.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="familyName"/> is null.</exception>
        public static IReadOnlyList<FontFileInfo> FindFamily(string familyName)
        {
            ArgumentNullException.ThrowIfNull(familyName);
            List<FontFileInfo> faces = new List<FontFileInfo>();
            foreach (FontFileInfo f in Files)
            {
                if (string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f.TypographicFamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                {
                    faces.Add(f);
                }
            }
            return faces;
        }

        /// <summary>A read-only stream over the file's bytes, positioned at 0. The caller disposes it.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        /// <exception cref="ArgumentException">The pack has no file with this name.</exception>
        public static Stream Open(string fileName)
        {
            if (!TryOpen(fileName, out Stream? stream))
            {
                throw new ArgumentException($"'{fileName}' is not a file of the substitute font pack.", nameof(fileName));
            }
            return stream!;
        }

        /// <summary>Opens the file like <see cref="Open"/>; false (and a null stream) when the pack has no such file.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        public static bool TryOpen(string fileName, out Stream? stream)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            stream = null;
            FontFileInfo? f = Find(fileName);
            if (f is null)
            {
                return false;
            }
            stream = typeof(SubstituteFontPack).Assembly.GetManifestResourceStream(FontPrefix + f.FileName);
            return stream is not null;
        }

        /// <summary>A new copy of the file's bytes.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        /// <exception cref="ArgumentException">The pack has no file with this name.</exception>
        public static byte[] ReadAllBytes(string fileName)
        {
            using Stream s = Open(fileName);
            byte[] data = new byte[s.Length];
            s.ReadExactly(data);
            return data;
        }

        /// <summary>
        /// The full text of the licence that applies to the file. Every file of the pack is under the SIL Open Font
        /// License 1.1; each family carries its own copyright lines (Liberation, Carlito, Caladea, Gelasio, Selawik).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        /// <exception cref="ArgumentException">The pack has no file with this name.</exception>
        public static string GetLicenseText(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            if (!ByName.TryGetValue(fileName, out (FontFileInfo File, string License) e))
            {
                throw new ArgumentException($"'{fileName}' is not a file of the substitute font pack.", nameof(fileName));
            }
            lock (LicenseCache)
            {
                if (!LicenseCache.TryGetValue(e.License, out string? text))
                {
                    Assembly asm = typeof(SubstituteFontPack).Assembly;
                    using Stream s = asm.GetManifestResourceStream(LicensePrefix + e.License)
                        ?? throw new InvalidOperationException($"The licence resource '{e.License}' is missing from Chuvadi.Layout.Fonts.");
                    using StreamReader r = new StreamReader(s);
                    text = r.ReadToEnd();
                    LicenseCache[e.License] = text;
                }
                return text;
            }
        }

        private static Dictionary<string, (FontFileInfo File, string License)> BuildIndex()
        {
            Dictionary<string, (FontFileInfo, string)> index = new Dictionary<string, (FontFileInfo, string)>(StringComparer.OrdinalIgnoreCase);
            foreach ((FontFileInfo file, string license) in SubstituteFontCatalog.Entries)
            {
                index[file.FileName] = (file, license);
            }
            return index;
        }

        private static IReadOnlyList<FontFileInfo> BuildFiles()
        {
            FontFileInfo[] files = new FontFileInfo[SubstituteFontCatalog.Entries.Length];
            for (int i = 0; i < files.Length; i++)
            {
                files[i] = SubstituteFontCatalog.Entries[i].File;
            }
            return Array.AsReadOnly(files);
        }

        private sealed class Pack : IFontFilePack
        {
            public string Name => "Chuvadi.Layout.Fonts";

            public IReadOnlyList<FontFileInfo> Files => SubstituteFontPack.Files;

            public Stream Open(string fileName) => SubstituteFontPack.Open(fileName);

            public string GetLicenseText(string fileName) => SubstituteFontPack.GetLicenseText(fileName);
        }
    }
}
