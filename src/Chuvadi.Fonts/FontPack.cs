using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Chuvadi.Fonts
{
    /// <summary>
    /// The LiPi Sans font files, embedded in this assembly. Lists the files and hands out their bytes; it parses
    /// nothing and caches nothing (callers keep their own parsed faces). No disk or network access. Thread-safe.
    /// </summary>
    public static class FontPack
    {
        private const string ResourcePrefix = "Chuvadi.Fonts.";
        private const string LicenseResource = ResourcePrefix + "LiPiSans-OFL.txt";

        private static readonly Dictionary<string, FontPackFile> ByName = BuildIndex();
        private static string? _license;

        /// <summary>Every font file in the pack, ordered by file name.</summary>
        public static IReadOnlyList<FontPackFile> Files { get; } = Array.AsReadOnly(FontPackCatalog.Files);

        /// <summary>
        /// The pack as an <see cref="IFontFilePack"/> (name "Chuvadi.Fonts"), for code that reads any font pack the
        /// same way, such as Chuvadi.Layout's <c>FontPackSource</c>. Its files are <see cref="Files"/> as
        /// <see cref="FontFileInfo"/>: the typographic family is the family and the width class 5 (normal) for every
        /// LiPi Sans file, as their tables state.
        /// </summary>
        public static IFontFilePack Instance { get; } = new Pack();

        /// <summary>The SIL Open Font License 1.1 text that applies to every file of the pack.</summary>
        public static string LicenseText => _license ??= ReadLicense();

        /// <summary>The file with this name (for example "LiPi-Sans-Tamil-Regular.ttf", ignoring case), or null.</summary>
        public static FontPackFile? Find(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            return ByName.TryGetValue(fileName, out FontPackFile? f) ? f : null;
        }

        /// <summary>The faces of one family ("LiPi Sans", "LiPi Sans Tamil", ...; ignoring case), empty when there are none.</summary>
        public static IReadOnlyList<FontPackFile> FindFamily(string familyName)
        {
            ArgumentNullException.ThrowIfNull(familyName);
            List<FontPackFile> faces = new List<FontPackFile>();
            foreach (FontPackFile f in FontPackCatalog.Files)
            {
                if (string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                {
                    faces.Add(f);
                }
            }
            return faces;
        }

        /// <summary>A read-only stream over the file's bytes, positioned at 0. The caller disposes it.</summary>
        /// <exception cref="ArgumentException">The pack has no file with this name.</exception>
        public static Stream Open(string fileName)
        {
            if (!TryOpen(fileName, out Stream? stream))
            {
                throw new ArgumentException($"'{fileName}' is not a file of the font pack.", nameof(fileName));
            }
            return stream!;
        }

        /// <summary>Opens the file like <see cref="Open"/>; false (and a null stream) when the pack has no such file.</summary>
        public static bool TryOpen(string fileName, out Stream? stream)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            stream = null;
            FontPackFile? f = Find(fileName);
            if (f is null)
            {
                return false;
            }
            stream = typeof(FontPack).Assembly.GetManifestResourceStream(ResourcePrefix + f.FileName);
            return stream is not null;
        }

        /// <summary>A new copy of the file's bytes.</summary>
        /// <exception cref="ArgumentException">The pack has no file with this name.</exception>
        public static byte[] ReadAllBytes(string fileName)
        {
            using Stream s = Open(fileName);
            byte[] data = new byte[s.Length];
            s.ReadExactly(data);
            return data;
        }

        private static Dictionary<string, FontPackFile> BuildIndex()
        {
            Dictionary<string, FontPackFile> index = new Dictionary<string, FontPackFile>(StringComparer.OrdinalIgnoreCase);
            foreach (FontPackFile f in FontPackCatalog.Files)
            {
                index[f.FileName] = f;
            }
            return index;
        }

        private sealed class Pack : IFontFilePack
        {
            private readonly IReadOnlyList<FontFileInfo> _files;

            public Pack()
            {
                FontFileInfo[] files = new FontFileInfo[FontPackCatalog.Files.Length];
                for (int i = 0; i < files.Length; i++)
                {
                    FontPackFile f = FontPackCatalog.Files[i];
                    // No LiPi Sans file has a typographic family (name ID 16), and all are of normal width (usWidthClass 5).
                    files[i] = new FontFileInfo(f.FileName, f.FamilyName, f.FamilyName, f.StyleName, f.FullName,
                        f.PostScriptName, f.Weight, f.Italic, 5, f.Length, f.Sha256);
                }
                _files = Array.AsReadOnly(files);
            }

            public string Name => "Chuvadi.Fonts";

            public IReadOnlyList<FontFileInfo> Files => _files;

            public Stream Open(string fileName) => FontPack.Open(fileName);

            public string GetLicenseText(string fileName)
            {
                ArgumentNullException.ThrowIfNull(fileName);
                if (Find(fileName) is null)
                {
                    throw new ArgumentException($"'{fileName}' is not a file of the font pack.", nameof(fileName));
                }
                return LicenseText;
            }
        }

        private static string ReadLicense()
        {
            Assembly asm = typeof(FontPack).Assembly;
            using Stream s = asm.GetManifestResourceStream(LicenseResource)
                ?? throw new InvalidOperationException("The licence resource is missing from Chuvadi.Fonts.");
            using StreamReader r = new StreamReader(s);
            return r.ReadToEnd();
        }
    }
}
