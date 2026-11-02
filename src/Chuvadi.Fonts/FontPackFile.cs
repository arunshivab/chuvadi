namespace Chuvadi.Fonts
{
    /// <summary>The script a face of the pack is made for.</summary>
    public enum FontScript
    {
        /// <summary>Latin (LiPi Sans).</summary>
        Latin,

        /// <summary>Bengali (LiPi Sans Bengali).</summary>
        Bengali,

        /// <summary>Devanagari (LiPi Sans Devanagari).</summary>
        Devanagari,

        /// <summary>Gujarati (LiPi Sans Gujarati).</summary>
        Gujarati,

        /// <summary>Gurmukhi (LiPi Sans Gurmukhi).</summary>
        Gurmukhi,

        /// <summary>Kannada (LiPi Sans Kannada).</summary>
        Kannada,

        /// <summary>Malayalam (LiPi Sans Malayalam).</summary>
        Malayalam,

        /// <summary>Odia (LiPi Sans Odia).</summary>
        Odia,

        /// <summary>Tamil (LiPi Sans Tamil).</summary>
        Tamil,

        /// <summary>Telugu (LiPi Sans Telugu).</summary>
        Telugu,
    }

    /// <summary>One font file of the pack, described from its own tables.</summary>
    /// <param name="FileName">The file name, for example "LiPi-Sans-Tamil-Bold.ttf".</param>
    /// <param name="FamilyName">The family (name ID 1), for example "LiPi Sans Tamil".</param>
    /// <param name="StyleName">The style (name ID 2): Regular, Bold, Italic or Bold Italic.</param>
    /// <param name="FullName">The full name (name ID 4).</param>
    /// <param name="PostScriptName">The PostScript name (name ID 6).</param>
    /// <param name="Weight">The weight class (OS/2 usWeightClass): 400 or 700.</param>
    /// <param name="Italic">True for an italic face (OS/2 fsSelection bit 0).</param>
    /// <param name="Script">The script the face is made for.</param>
    /// <param name="Length">The file length in bytes.</param>
    /// <param name="Sha256">The SHA-256 of the file, lowercase hexadecimal.</param>
    public sealed record FontPackFile(
        string FileName,
        string FamilyName,
        string StyleName,
        string FullName,
        string PostScriptName,
        int Weight,
        bool Italic,
        FontScript Script,
        long Length,
        string Sha256);
}
