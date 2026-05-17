// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.8 — CMYK render output

using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Images.Tests;

public sealed class CmykTests
{
    [Fact]
    public void CmykImage_FromBgra_PreservesDimensions()
    {
        int w = 4, h = 3;
        PixelBuffer pb = new(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                pb.SetPixelBgra(x, y, b: 255, g: 0, r: 0, a: 255);
            }
        }
        CmykImage cmyk = CmykImage.FromBgra(pb);
        cmyk.Width.Should().Be(w);
        cmyk.Height.Should().Be(h);
    }

    [Fact]
    public void CmykImage_PureBlueInput_ProducesCmykOutput()
    {
        // BGRA pure blue: B=255, G=0, R=0 → cyan+magenta in CMYK.
        int w = 1, h = 1;
        PixelBuffer pb = new(w, h);
        pb.SetPixelBgra(0, 0, b: 255, g: 0, r: 0, a: 255);
        CmykImage cmyk = CmykImage.FromBgra(pb);
        cmyk.Pixels.Length.Should().Be(4);
    }

    [Fact]
    public void CmykImage_SetPixel_RoundTripsThroughBuffer()
    {
        int w = 2, h = 2;
        CmykImage img = new(w, h);
        img.SetPixel(0, 0, c: 100, m: 50, yel: 25, k: 10);
        img.Pixels[0].Should().Be(100);
        img.Pixels[1].Should().Be(50);
        img.Pixels[2].Should().Be(25);
        img.Pixels[3].Should().Be(10);
    }

    [Fact]
    public void CmykTiffEncoder_Encode_StartsWithTiffMagic()
    {
        int w = 4, h = 3;
        CmykImage cmyk = new(w, h);
        byte[] tiff = CmykTiffEncoder.Encode(cmyk);
        // TIFF magic: II*\0 (little-endian) or MM\0* (big-endian)
        bool littleEndian = tiff[0] == (byte)'I' && tiff[1] == (byte)'I'
            && tiff[2] == 42 && tiff[3] == 0;
        bool bigEndian = tiff[0] == (byte)'M' && tiff[1] == (byte)'M'
            && tiff[2] == 0 && tiff[3] == 42;
        (littleEndian || bigEndian).Should().BeTrue();
    }
}
