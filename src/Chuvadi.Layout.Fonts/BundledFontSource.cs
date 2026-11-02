using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>
    /// The fonts bundled in this package, read from embedded resources on first use of each face.
    /// No disk or network access. Thread-safe. Use <see cref="Instance"/>.
    /// </summary>
    public sealed class BundledFontSource : IFontSource
    {
        private const string ResourcePrefix = "Chuvadi.Layout.Fonts.";

        private readonly object _gate = new object();
        private readonly Dictionary<string, FontFace> _loaded = new Dictionary<string, FontFace>(StringComparer.Ordinal);
        private readonly IReadOnlyList<FontFaceDescriptor> _faces;

        private BundledFontSource()
        {
            List<FontFaceDescriptor> faces = new List<FontFaceDescriptor>(BundledFontCatalog.Entries.Length);
            foreach ((string file, string family, string typo, string sub, string full, string ps, int weight, bool italic, int width) in BundledFontCatalog.Entries)
            {
                faces.Add(new FontFaceDescriptor(family, typo, sub, full, ps, weight, italic, width, FontOrigin.Bundled, file, 0));
            }
            _faces = faces;
        }

        /// <summary>The shared instance.</summary>
        public static BundledFontSource Instance { get; } = new BundledFontSource();

        public string Name => "Chuvadi.Layout.Fonts";

        /// <summary>The distinct family names in the pack.</summary>
        public static IReadOnlyList<string> Families
        {
            get
            {
                SortedSet<string> set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in BundledFontCatalog.Entries)
                {
                    set.Add(e.Family);
                }
                return new List<string>(set);
            }
        }

        public IReadOnlyList<FontFaceDescriptor> GetFaces() => _faces;

        public FontFace LoadFace(FontFaceDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            lock (_gate)
            {
                if (_loaded.TryGetValue(descriptor.Key, out FontFace? face))
                {
                    return face;
                }
                byte[] bytes = ReadResource(descriptor.Key);
                face = FontFace.Load(bytes, 0, FontOrigin.Bundled, descriptor.Key);
                _loaded[descriptor.Key] = face;
                return face;
            }
        }

        /// <summary>The raw bytes of a bundled font file (for example "Carlito-Regular.ttf").</summary>
        /// <exception cref="ArgumentException">No such file is bundled.</exception>
        public static byte[] ReadResource(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            Assembly asm = typeof(BundledFontSource).Assembly;
            using Stream? s = asm.GetManifestResourceStream(ResourcePrefix + fileName);
            if (s is null)
            {
                throw new ArgumentException($"'{fileName}' is not a bundled font.", nameof(fileName));
            }
            byte[] data = new byte[s.Length];
            s.ReadExactly(data);
            return data;
        }
    }
}
