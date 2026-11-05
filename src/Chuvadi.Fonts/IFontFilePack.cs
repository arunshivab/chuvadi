using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Fonts
{
    /// <summary>
    /// A set of font files a package carries: what each file is, its bytes and its licence. It parses nothing.
    /// Chuvadi.Fonts (<see cref="FontPack.Instance"/>) and Chuvadi.Layout.Fonts implement it, and Chuvadi.Layout turns
    /// any implementation into a font source (<c>FontPackSource</c>), so an application can bring its own pack the same
    /// way. Implementations must be thread-safe.
    /// </summary>
    public interface IFontFilePack
    {
        /// <summary>A short name for reports, for example "Chuvadi.Fonts".</summary>
        string Name { get; }

        /// <summary>Every font file of the pack.</summary>
        IReadOnlyList<FontFileInfo> Files { get; }

        /// <summary>A read-only stream over the file's bytes, positioned at 0. The caller disposes it.</summary>
        /// <param name="fileName">A <see cref="FontFileInfo.FileName"/> of this pack (case is ignored).</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        /// <exception cref="System.ArgumentException">The pack has no file with this name.</exception>
        Stream Open(string fileName);

        /// <summary>The full text of the licence that applies to the file.</summary>
        /// <param name="fileName">A <see cref="FontFileInfo.FileName"/> of this pack (case is ignored).</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="fileName"/> is null.</exception>
        /// <exception cref="System.ArgumentException">The pack has no file with this name.</exception>
        string GetLicenseText(string fileName);
    }

    /// <summary>One font file of an <see cref="IFontFilePack"/>, described from its own tables.</summary>
    /// <param name="FileName">The file name, for example "LiPi-Sans-Tamil-Bold.ttf".</param>
    /// <param name="FamilyName">The family (name ID 1), for example "LiPi Sans Tamil" or "Selawik Light".</param>
    /// <param name="TypographicFamilyName">The typographic family (name ID 16), or the family when the file has none;
    /// for example "Selawik" for "Selawik Light".</param>
    /// <param name="StyleName">The style (name ID 2), for example "Regular" or "Bold Italic".</param>
    /// <param name="FullName">The full name (name ID 4).</param>
    /// <param name="PostScriptName">The PostScript name (name ID 6).</param>
    /// <param name="Weight">The weight class (OS/2 usWeightClass), 100 to 900.</param>
    /// <param name="Italic">True for an italic face (OS/2 fsSelection bit 0).</param>
    /// <param name="WidthClass">The width class (OS/2 usWidthClass), 1 to 9; 5 is normal.</param>
    /// <param name="Length">The file length in bytes.</param>
    /// <param name="Sha256">The SHA-256 of the file, lowercase hexadecimal.</param>
    public sealed record FontFileInfo(
        string FileName,
        string FamilyName,
        string TypographicFamilyName,
        string StyleName,
        string FullName,
        string PostScriptName,
        int Weight,
        bool Italic,
        int WidthClass,
        long Length,
        string Sha256);
}
