using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Chuvadi.Fonts;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>
    /// A font source over any font pack that implements Chuvadi.Fonts' <see cref="IFontFilePack"/>: the substitute
    /// fonts of Chuvadi.Layout.Fonts (<c>SubstituteFontPack.Instance</c>), the LiPi Sans fonts of Chuvadi.Fonts
    /// (<see cref="FontPack.Instance"/>, which Layout already adds as its built-in fonts), or an application's own pack.
    /// Each file is described from the pack's <see cref="FontFileInfo"/> without reading it, and parsed once, on first
    /// use. Thread-safe.
    /// </summary>
    /// <remarks>
    /// Use <see cref="For(IFontFilePack)"/> for the shared source of a pack, so that its faces are parsed once per
    /// process however many layouts use it; <see cref="FontPackSource(IFontFilePack, FontOrigin)"/> makes a separate
    /// source with its own parsed faces.
    /// </remarks>
    public sealed class FontPackSource : IFontSource
    {
        private static readonly ConditionalWeakTable<IFontFilePack, FontPackSource> Shared = new ConditionalWeakTable<IFontFilePack, FontPackSource>();

        private readonly object _gate = new object();
        private readonly Dictionary<string, FontFace> _loaded = new Dictionary<string, FontFace>(StringComparer.Ordinal);
        private readonly IReadOnlyList<FontFaceDescriptor> _faces;

        /// <summary>A new source over <paramref name="pack"/>, with its own parsed faces.</summary>
        /// <param name="pack">The font pack.</param>
        /// <param name="origin">The origin reported for its faces; default <see cref="FontOrigin.Bundled"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pack"/> is null.</exception>
        public FontPackSource(IFontFilePack pack, FontOrigin origin = FontOrigin.Bundled)
        {
            ArgumentNullException.ThrowIfNull(pack);
            Pack = pack;
            Origin = origin;
            List<FontFaceDescriptor> faces = new List<FontFaceDescriptor>(pack.Files.Count);
            foreach (FontFileInfo f in pack.Files)
            {
                faces.Add(new FontFaceDescriptor(f.FamilyName, f.TypographicFamilyName, f.StyleName, f.FullName,
                    f.PostScriptName, f.Weight, f.Italic, f.WidthClass, origin, f.FileName, 0));
            }
            _faces = faces;
        }

        /// <summary>
        /// The shared source of <paramref name="pack"/> (origin <see cref="FontOrigin.Bundled"/>): the same instance for
        /// the same pack, so each face is parsed once per process.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="pack"/> is null.</exception>
        public static FontPackSource For(IFontFilePack pack)
        {
            ArgumentNullException.ThrowIfNull(pack);
            return Shared.GetValue(pack, p => new FontPackSource(p));
        }

        /// <summary>The pack this source reads.</summary>
        public IFontFilePack Pack { get; }

        /// <summary>The origin reported for its faces.</summary>
        public FontOrigin Origin { get; }

        /// <summary>The pack's name.</summary>
        public string Name => Pack.Name;

        /// <inheritdoc/>
        public IReadOnlyList<FontFaceDescriptor> GetFaces() => _faces;

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is null.</exception>
        /// <exception cref="ArgumentException">The pack has no file named by the descriptor's key.</exception>
        public FontFace LoadFace(FontFaceDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            lock (_gate)
            {
                if (!_loaded.TryGetValue(descriptor.Key, out FontFace? face))
                {
                    byte[] bytes;
                    using (Stream s = Pack.Open(descriptor.Key))
                    {
                        bytes = new byte[s.Length];
                        s.ReadExactly(bytes);
                    }
                    face = FontFace.Load(bytes, 0, Origin, descriptor.Key);
                    _loaded[descriptor.Key] = face;
                }
                return face;
            }
        }
    }
}
