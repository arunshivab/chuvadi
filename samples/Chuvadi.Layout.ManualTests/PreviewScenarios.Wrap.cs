using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        /// <summary>Text flowing beside floating images: square left and right, both sides, one side, tight polygon, and a page-relative image anchored below the text it displaces.</summary>
        internal static FlowDocument WrapShowcase()
        {
            CharacterFormat h1 = Body with { FontFamily = "Cambria", FontSize = 16, Bold = true, Color = new LayoutColor(0x1F, 0x3A, 0x60) };
            ParagraphFormat h1p = new ParagraphFormat { SpaceBefore = 10, SpaceAfter = 6, KeepWithNext = true };
            ParagraphFormat bodyP = new ParagraphFormat { SpaceAfter = 8, LineSpacing = 1.08, Alignment = ParagraphAlignment.Justify };
            string text = "Text flows beside a floating object line by line. Each line takes the space left free by the object and its wrap distance, "
                + "and lines below the object return to the full width of the column. Word fills the left side of a row before the right side "
                + "when both sides are allowed, and skips spaces too narrow for a word. ";
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            d.Sections.Add(s);

            FlowParagraph Para(string body, AnchoredImageInline? image)
            {
                FlowParagraph p = FlowParagraph.Of(body, Body, bodyP);
                if (image is not null)
                {
                    p.Inlines.Insert(0, image);
                }
                return p;
            }

            // A page-relative image near the bottom of the page, anchored to the last paragraph of the page: the text above
            // its anchor still flows around it.
            s.Blocks.Add(FlowParagraph.Of("Square wrapping, image on the left", h1, h1p));
            s.Blocks.Add(Para(text + text, new AnchoredImageInline(SamplePng(), 110, 70, Body) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.Start, DistanceBottom = 4 }));
            s.Blocks.Add(FlowParagraph.Of("Square wrapping, image on the right", h1, h1p));
            s.Blocks.Add(Para(text + text, new AnchoredImageInline(SamplePng(), 130, 60, Body) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.End, OffsetY = 6, VerticalAlignment = null }));
            s.Blocks.Add(FlowParagraph.Of("Both sides of a centred image", h1, h1p));
            s.Blocks.Add(Para(text + text + text, new AnchoredImageInline(SamplePng(), 90, 70, Body) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.Center, OffsetY = 14 }));
            s.Blocks.Add(FlowParagraph.Of("Largest side only", h1, h1p));
            s.Blocks.Add(Para(text + text, new AnchoredImageInline(SamplePng(), 90, 50, Body) { Wrap = FloatWrap.Square, WrapSide = FloatWrapSide.Largest, OffsetX = 110 }));
            s.Blocks.Add(FlowParagraph.Of("Tight wrapping around a triangle", h1, h1p));
            s.Blocks.Add(Para(text + text + text, new AnchoredImageInline(TrianglePng(), 170, 110, Body)
            {
                Wrap = FloatWrap.Tight,
                HorizontalAlignment = FloatAlignment.Start,
                DistanceRight = 6,
                WrapPolygon = new List<WrapPoint> { new WrapPoint(0, 0), new WrapPoint(170, 0), new WrapPoint(0, 110) },
            }));
            s.Blocks.Add(FlowParagraph.Of("A page-relative image anchored further down", h1, h1p));
            s.Blocks.Add(Para(text + text + text, null));
            s.Blocks.Add(Para("This paragraph anchors an image positioned relative to the page, above this paragraph. The text before the anchor wraps around it too.",
                new AnchoredImageInline(SamplePng(), 120, 80, Body)
                {
                    Wrap = FloatWrap.Square,
                    HorizontalRelativeTo = HorizontalAnchor.Margin,
                    VerticalRelativeTo = VerticalAnchor.Page,
                    HorizontalAlignment = FloatAlignment.End,
                    OffsetY = 330,
                }));
            for (int i = 0; i < 4; i++)
            {
                s.Blocks.Add(Para(text + text, null));
            }
            return d;
        }
    
        /// <summary>A PNG whose opaque part is the triangle (0,0)-(W,0)-(0,H), transparent elsewhere, to show tight wrapping.</summary>
        internal static LayoutImage TrianglePng()
        {
            const int W = 68, H = 44;
            byte[] raw = new byte[H * ((W * 4) + 1)];
            for (int y = 0; y < H; y++)
            {
                int row = y * ((W * 4) + 1);
                for (int x = 0; x < W; x++)
                {
                    bool inside = ((x + 0.5) / W) + ((y + 0.5) / H) <= 1;
                    raw[row + 1 + (x * 4)] = (byte)(0x1F + (x * 2));
                    raw[row + 2 + (x * 4)] = (byte)(0x3A + (y * 3));
                    raw[row + 3 + (x * 4)] = 0x90;
                    raw[row + 4 + (x * 4)] = inside ? (byte)0xFF : (byte)0;
                }
            }
            using MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            byte[] ihdr = new byte[13];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, W);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), H);
            ihdr[8] = 8;
            ihdr[9] = 6;
            Chunk(png, "IHDR", ihdr);
            using (MemoryStream z = new MemoryStream())
            {
                using (System.IO.Compression.ZLibStream zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    zs.Write(raw);
                }
                Chunk(png, "IDAT", z.ToArray());
            }
            Chunk(png, "IEND", Array.Empty<byte>());
            return new LayoutImage(png.ToArray(), LayoutImageFormat.Png, W, H);
        }
    }
}
