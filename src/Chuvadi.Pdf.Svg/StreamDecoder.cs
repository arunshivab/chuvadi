// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.0 — SVG export

using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Svg;

/// <summary>Internal helper that decodes a PdfStream through Chuvadi's filter pipeline.</summary>
internal static class StreamDecoder
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    internal static byte[] Decode(PdfStream stream)
    {
        if (!stream.IsFiltered) { return stream.RawBytes; }

        PdfPrimitive? filter = stream.Filter;
        if (filter is PdfName filterName)
        {
            string resolved = FilterRegistry.ResolveAlias(filterName.Value);
            return Pipeline.Decode(resolved, stream.RawBytes, null);
        }

        if (filter is PdfArray filterArray)
        {
            byte[] data = stream.RawBytes;
            for (int i = 0; i < filterArray.Count; i++)
            {
                PdfName? fn = filterArray.GetAs<PdfName>(i);
                if (fn is null) { continue; }
                string resolved = FilterRegistry.ResolveAlias(fn.Value);
                data = Pipeline.Decode(resolved, data, null);
            }
            return data;
        }

        return stream.RawBytes;
    }
}
