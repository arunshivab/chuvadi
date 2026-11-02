using System;
using System.Collections.Generic;
using Chuvadi.Fonts;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>
    /// The built-in fonts: the LiPi Sans files of the Chuvadi.Fonts package (D-101, D-113), added to every resolver
    /// unless <see cref="LayoutOptions.UseBuiltInFonts"/> is false. Faces are parsed once, on first use. The origin is
    /// <see cref="FontOrigin.Bundled"/>, as when the same files came from Chuvadi.Layout.Fonts, so substitution reports
    /// do not change. Thread-safe.
    /// </summary>
    internal sealed class BuiltInFontSource : IFontSource
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, FontFace> _loaded = new Dictionary<string, FontFace>(StringComparer.Ordinal);
        private readonly IReadOnlyList<FontFaceDescriptor> _faces;

        private BuiltInFontSource()
        {
            List<FontFaceDescriptor> faces = new List<FontFaceDescriptor>(FontPack.Files.Count);
            foreach (FontPackFile f in FontPack.Files)
            {
                // Every face of the pack is of normal width (OS/2 usWidthClass 5).
                faces.Add(new FontFaceDescriptor(f.FamilyName, f.FamilyName, f.StyleName, f.FullName, f.PostScriptName,
                    f.Weight, f.Italic, 5, FontOrigin.Bundled, f.FileName, 0));
            }
            _faces = faces;
        }

        public static BuiltInFontSource Instance { get; } = new BuiltInFontSource();

        public string Name => "Chuvadi.Fonts";

        public IReadOnlyList<FontFaceDescriptor> GetFaces() => _faces;

        public FontFace LoadFace(FontFaceDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            lock (_gate)
            {
                if (!_loaded.TryGetValue(descriptor.Key, out FontFace? face))
                {
                    face = FontFace.Load(FontPack.ReadAllBytes(descriptor.Key), 0, FontOrigin.Bundled, descriptor.Key);
                    _loaded[descriptor.Key] = face;
                }
                return face;
            }
        }
    }
}
