using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Chuvadi.Layout.Fonts;
using PdfFontFace = Chuvadi.Pdf.Fonts.Rendering.FontFace;

namespace Chuvadi.Layout.Pdf
{
    /// <summary>
    /// The Chuvadi.Pdf <see cref="PdfFontFace"/> for each Chuvadi.Layout <see cref="FontFace"/>, loaded once and kept
    /// as long as the Layout face lives. Chuvadi.Pdf 3.25 loads collection faces itself (and keeps its shaping tables
    /// with the face), so the adapter no longer extracts faces from .ttc files.
    /// </summary>
    internal static class PdfFaces
    {
        private static readonly ConditionalWeakTable<FontFace, PdfFontFace> Faces = new ConditionalWeakTable<FontFace, PdfFontFace>();
        private static readonly ConditionalWeakTable<FontFace, byte[]> Programs = new ConditionalWeakTable<FontFace, byte[]>();

        /// <summary>The loaded Chuvadi.Pdf face (for shaping and metrics).</summary>
        /// <exception cref="Chuvadi.Pdf.Fonts.Rendering.FontRenderingException">The font is invalid or unsupported by Chuvadi.Pdf.</exception>
        public static PdfFontFace Get(FontFace face) => Faces.GetValue(face, f => PdfFontFace.Load(Program(f), f.FaceIndex));

        /// <summary>The whole font file of a Layout face (the collection for a .ttc face), without copying when possible.</summary>
        public static byte[] Program(FontFace face) => Programs.GetValue(face, f =>
            MemoryMarshal.TryGetArray(f.Data, out ArraySegment<byte> seg) && seg.Array is byte[] a && seg.Offset == 0 && seg.Count == a.Length
                ? a
                : f.Data.ToArray());
    }
}
