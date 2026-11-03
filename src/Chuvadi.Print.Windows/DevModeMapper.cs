using System;
using System.Collections.Generic;

namespace Chuvadi.Print.Windows
{
    /// <summary>The DEVMODE choices for a job, independent of any driver.</summary>
    internal sealed class DevModeRequest
    {
        public short Orientation { get; init; } = DevModeBuffer.DMORIENT_PORTRAIT;

        /// <summary>Driver paper id, or 0 for a custom size given by <see cref="PaperWidthTenthsMm"/>/<see cref="PaperLengthTenthsMm"/>.</summary>
        public short PaperId { get; init; }

        public short PaperWidthTenthsMm { get; init; }
        public short PaperLengthTenthsMm { get; init; }
        public short Copies { get; init; } = 1;
        public bool Collate { get; init; }
        public short Color { get; init; } = DevModeBuffer.DMCOLOR_COLOR;
        public short Duplex { get; init; } = DevModeBuffer.DMDUP_SIMPLEX;
        public short? Bin { get; init; }
        public uint? MediaType { get; init; }

        /// <summary>dmPrintQuality: an X resolution in dpi, or a negative DMRES_* preset.</summary>
        public short? PrintQuality { get; init; }

        public short? YResolution { get; init; }

        public bool IsCustomPaper => PaperId == 0;
    }

    /// <summary>Translates device-independent <see cref="PrintSettings"/> into DEVMODE fields. Pure; portable.</summary>
    internal static class DevModeMapper
    {
        /// <summary>Tolerance for treating a driver paper as the requested paper: 1 mm, in points.</summary>
        internal const double PaperTolerancePoints = 72.0 / 25.4;

        public static DevModeRequest Map(PrintSettings settings, IReadOnlyList<PrinterPaper> papers, PaperMatching matching,
                                         bool targetHandlesCopies, PrinterBin? bin, PrinterMediaType? mediaType,
                                         PrinterResolution? resolution, PrintQuality? quality)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (papers is null) throw new ArgumentNullException(nameof(papers));

            (short paperId, short width, short length) = ResolvePaper(settings.Paper, papers, matching);

            short copies = 1;
            if (targetHandlesCopies)
            {
                if (settings.Copies > short.MaxValue)
                    throw new InvalidOperationException("Too many copies for the driver: " + settings.Copies + ".");
                copies = (short)settings.Copies;
            }

            short? printQuality = null, yResolution = null;
            if (resolution.HasValue)
            {
                printQuality = checked((short)resolution.Value.X);
                yResolution = checked((short)resolution.Value.Y);
            }
            else if (quality.HasValue)
            {
                printQuality = (short)quality.Value;
            }

            return new DevModeRequest
            {
                Orientation = settings.Orientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape
                    ? DevModeBuffer.DMORIENT_LANDSCAPE
                    : DevModeBuffer.DMORIENT_PORTRAIT,
                PaperId = paperId,
                PaperWidthTenthsMm = width,
                PaperLengthTenthsMm = length,
                Copies = copies,
                Collate = targetHandlesCopies && settings.Collate,
                Color = settings.Color == ColorMode.Color ? DevModeBuffer.DMCOLOR_COLOR : DevModeBuffer.DMCOLOR_MONOCHROME,
                Duplex = settings.Duplex switch
                {
                    Duplex.Vertical => DevModeBuffer.DMDUP_VERTICAL,
                    Duplex.Horizontal => DevModeBuffer.DMDUP_HORIZONTAL,
                    _ => DevModeBuffer.DMDUP_SIMPLEX
                },
                Bin = bin?.Id,
                MediaType = mediaType?.Id,
                PrintQuality = printQuality,
                YResolution = yResolution
            };
        }

        /// <summary>
        /// Picks the driver paper for <paramref name="paper"/>: returns its id with zero dimensions, or id 0
        /// with the portrait dimensions in tenths of a millimetre for a custom size.
        /// </summary>
        public static (short PaperId, short WidthTenthsMm, short LengthTenthsMm) ResolvePaper(
            PaperSize paper, IReadOnlyList<PrinterPaper> papers, PaperMatching matching)
        {
            if (papers is null) throw new ArgumentNullException(nameof(papers));
            double shortEdge = Math.Min(paper.WidthPoints, paper.HeightPoints);
            double longEdge = Math.Max(paper.WidthPoints, paper.HeightPoints);

            if (matching != PaperMatching.CustomOnly)
            {
                PrinterPaper? best = null;
                double bestError = double.MaxValue;
                foreach (PrinterPaper candidate in papers)
                {
                    double cShort = Math.Min(candidate.Size.WidthPoints, candidate.Size.HeightPoints);
                    double cLong = Math.Max(candidate.Size.WidthPoints, candidate.Size.HeightPoints);
                    double error = Math.Max(Math.Abs(cShort - shortEdge), Math.Abs(cLong - longEdge));
                    if (error > PaperTolerancePoints) continue;
                    bool nameMatch = paper.Name is not null
                        && string.Equals(candidate.Name, paper.Name, StringComparison.OrdinalIgnoreCase);
                    // Prefer a same-named paper, then the closest size, then the lowest (standard) id.
                    double score = nameMatch ? -1 : error;
                    if (best is null || score < bestError || (score == bestError && candidate.Id < best.Id))
                    {
                        best = candidate;
                        bestError = score;
                    }
                }
                if (best is not null) return (best.Id, 0, 0);
                if (matching == PaperMatching.ExactOnly)
                    throw new InvalidOperationException("The printer has no paper matching " + paper + ".");
            }

            return (0, ToTenthsMm(shortEdge), ToTenthsMm(longEdge));
        }

        /// <summary>Writes the request into a driver DEVMODE. Fields the driver's DEVMODE lacks are skipped.</summary>
        public static void Apply(DevModeRequest request, DevModeBuffer devMode)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (devMode is null) throw new ArgumentNullException(nameof(devMode));

            devMode.SetInt16(DevModeBuffer.OffsetOrientation, DevModeBuffer.DM_ORIENTATION, request.Orientation);

            if (request.IsCustomPaper)
            {
                devMode.SetInt16(DevModeBuffer.OffsetPaperSize, DevModeBuffer.DM_PAPERSIZE, 0);
                devMode.SetInt16(DevModeBuffer.OffsetPaperWidth, DevModeBuffer.DM_PAPERWIDTH, request.PaperWidthTenthsMm);
                devMode.SetInt16(DevModeBuffer.OffsetPaperLength, DevModeBuffer.DM_PAPERLENGTH, request.PaperLengthTenthsMm);
            }
            else
            {
                devMode.SetInt16(DevModeBuffer.OffsetPaperSize, DevModeBuffer.DM_PAPERSIZE, request.PaperId);
                devMode.ClearFlag(DevModeBuffer.DM_PAPERWIDTH | DevModeBuffer.DM_PAPERLENGTH);
            }
            devMode.ClearFlag(DevModeBuffer.DM_FORMNAME);

            devMode.SetInt16(DevModeBuffer.OffsetScale, DevModeBuffer.DM_SCALE, 100);
            devMode.SetInt16(DevModeBuffer.OffsetCopies, DevModeBuffer.DM_COPIES, request.Copies);
            devMode.SetInt16(DevModeBuffer.OffsetCollate, DevModeBuffer.DM_COLLATE,
                             request.Collate ? DevModeBuffer.DMCOLLATE_TRUE : DevModeBuffer.DMCOLLATE_FALSE);
            devMode.SetInt16(DevModeBuffer.OffsetColor, DevModeBuffer.DM_COLOR, request.Color);
            devMode.SetInt16(DevModeBuffer.OffsetDuplex, DevModeBuffer.DM_DUPLEX, request.Duplex);

            if (request.Bin.HasValue)
                devMode.SetInt16(DevModeBuffer.OffsetDefaultSource, DevModeBuffer.DM_DEFAULTSOURCE, request.Bin.Value);
            if (request.MediaType.HasValue)
                devMode.SetUInt32(DevModeBuffer.OffsetMediaType, DevModeBuffer.DM_MEDIATYPE, request.MediaType.Value);
            if (request.PrintQuality.HasValue)
                devMode.SetInt16(DevModeBuffer.OffsetPrintQuality, DevModeBuffer.DM_PRINTQUALITY, request.PrintQuality.Value);
            if (request.YResolution.HasValue)
                devMode.SetInt16(DevModeBuffer.OffsetYResolution, DevModeBuffer.DM_YRESOLUTION, request.YResolution.Value);
        }

        internal static short ToTenthsMm(double points)
        {
            double tenths = Math.Round(points / 72.0 * 254.0);
            if (tenths < 1 || tenths > short.MaxValue)
                throw new InvalidOperationException("Paper dimension out of range for the driver: " + points + "pt.");
            return (short)tenths;
        }

        internal static double FromTenthsMm(int tenths) => tenths / 254.0 * 72.0;
    }
}
