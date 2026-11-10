using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Chuvadi.Fonts;

namespace Chuvadi.Fonts.Liberation
{
    /// <summary>
    /// The Liberation fonts 2.1.5, embedded in this assembly: Liberation Sans, Serif and Mono in Regular, Bold, Italic
    /// and Bold Italic (12 TrueType files). They are metric-compatible with Arial, Times New Roman and Courier New, and
    /// so the stand-ins for the Helvetica, Times and Courier families of the PDF Standard 14 fonts. Lists the files and
    /// hands out their bytes and the licence; it parses nothing and caches nothing but the licence text, and references
    /// no layout engine, so a PDF, print or layout library can use the files. No disk or network access. Thread-safe.
    /// </summary>
    /// <remarks>
    /// With Chuvadi.Layout: <c>options.FontSources.Add(FontPackSource.For(LiberationPack.Instance))</c>.
    /// The fonts are under the SIL Open Font License 1.1; the Reserved Font Name is "Liberation". They are shipped
    /// unmodified, as the licence requires of a font that keeps its name.
    /// </remarks>
    public static class LiberationPack
    {
        private const string ResourcePrefix = "Chuvadi.Fonts.Liberation.";
        private const string LicenseResource = "Chuvadi.Fonts.Liberation.Liberation-OFL.txt";

        private static readonly Dictionary<string, FontFileInfo> ByName = BuildIndex();
        private static string? s_license;

        /// <summary>The 12 font files, ordered by file name.</summary>
        public static IReadOnlyList<FontFileInfo> Files { get; } = Array.AsReadOnly(LiberationCatalog.Files);

        /// <summary>The pack as an <see cref="IFontFilePack"/> (name "Chuvadi.Fonts.Liberation").</summary>
        public static IFontFilePack Instance { get; } = new Pack();

        /// <summary>
        /// The full text of the SIL Open Font License 1.1 with the Liberation copyright lines and Reserved Font Names.
        /// It applies to every file of the pack.
        /// </summary>
        public static string LicenseText => s_license ??= ReadLicense();

        /// <summary>The file with this name (for example "LiberationSans-Regular.ttf", ignoring case), or null.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        public static FontFileInfo? Find(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            return ByName.TryGetValue(fileName, out FontFileInfo? f) ? f : null;
        }

        /// <summary>
        /// The faces of one family, ignoring case, matched against both the family and the typographic family:
        /// "Liberation Sans" gives its four files. Empty when there are none.
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
                throw new ArgumentException($"'{fileName}' is not a file of the Liberation font pack.", nameof(fileName));
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
            stream = typeof(LiberationPack).Assembly.GetManifestResourceStream(ResourcePrefix + f.FileName);
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

        private static string ReadLicense()
        {
            Assembly asm = typeof(LiberationPack).Assembly;
            using Stream s = asm.GetManifestResourceStream(LicenseResource)
                ?? throw new InvalidOperationException("The licence resource is missing from Chuvadi.Fonts.Liberation.");
            using StreamReader r = new StreamReader(s);
            return r.ReadToEnd();
        }

        private static Dictionary<string, FontFileInfo> BuildIndex()
        {
            Dictionary<string, FontFileInfo> index = new Dictionary<string, FontFileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (FontFileInfo f in LiberationCatalog.Files)
            {
                index[f.FileName] = f;
            }
            return index;
        }

        private sealed class Pack : IFontFilePack
        {
            public string Name => "Chuvadi.Fonts.Liberation";

            public IReadOnlyList<FontFileInfo> Files => LiberationPack.Files;

            public Stream Open(string fileName) => LiberationPack.Open(fileName);

            public string GetLicenseText(string fileName)
            {
                ArgumentNullException.ThrowIfNull(fileName);
                if (Find(fileName) is null)
                {
                    throw new ArgumentException($"'{fileName}' is not a file of the Liberation font pack.", nameof(fileName));
                }
                return LicenseText;
            }
        }
    }
}
