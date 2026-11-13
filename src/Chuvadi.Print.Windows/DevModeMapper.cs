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

        /// <summary>
        /// Settings left as the driver's default DEVMODE has them (<see cref="PrintSettings.PrinterDefaults"/>):
        /// <see cref="DevModeMapper.Apply"/> does not write those fields.
        /// </summary>
        public PrinterDefault Keep { get; init; }

        public bool IsCustomPaper => PaperId == 0;
    }

    /// <summary>What a driver's default DEVMODE says about paper, sides, colour and copies; null where it says nothing.</summary>
    internal readonly record struct DevModeDefaults(PaperSize? Paper, Duplex? Duplex, ColorMode? Color, int? Copies)
    {
        /// <summary>Reads the defaults from <paramref name="devMode"/>, naming the paper from <paramref name="papers"/>.</summary>
        public static DevModeDefaults Read(DevModeBuffer devMode, IReadOnlyList<PrinterPaper> papers)
        {
            if (devMode is null) throw new ArgumentNullException(nameof(devMode));
            if (papers is null) throw new ArgumentNullException(nameof(papers));
            uint fields = devMode.Fields;

            PaperSize? paper = null;
            if (Has(devMode, fields, DevModeBuffer.OffsetPaperLength, DevModeBuffer.DM_PAPERLENGTH)
                && Has(devMode, fields, DevModeBuffer.OffsetPaperWidth, DevModeBuffer.DM_PAPERWIDTH)
                && devMode.ReadInt16(DevModeBuffer.OffsetPaperLength) > 0 && devMode.ReadInt16(DevModeBuffer.OffsetPaperWidth) > 0)
            {
                // dmPaperLength and dmPaperWidth override dmPaperSize when both are set.
                paper = new PaperSize(DevModeMapper.FromTenthsMm(devMode.ReadInt16(DevModeBuffer.OffsetPaperWidth)),
                                      DevModeMapper.FromTenthsMm(devMode.ReadInt16(DevModeBuffer.OffsetPaperLength)));
            }
            else if (Has(devMode, fields, DevModeBuffer.OffsetPaperSize, DevModeBuffer.DM_PAPERSIZE))
            {
                short id = devMode.ReadInt16(DevModeBuffer.OffsetPaperSize);
                foreach (PrinterPaper candidate in papers)
                {
                    if (candidate.Id != id) continue;
                    paper = candidate.Size;
                    break;
                }
            }

            Duplex? duplex = null;
            if (Has(devMode, fields, DevModeBuffer.OffsetDuplex, DevModeBuffer.DM_DUPLEX))
            {
                duplex = devMode.ReadInt16(DevModeBuffer.OffsetDuplex) switch
                {
                    DevModeBuffer.DMDUP_SIMPLEX => Print.Duplex.Simplex,
                    DevModeBuffer.DMDUP_VERTICAL => Print.Duplex.Vertical,
                    DevModeBuffer.DMDUP_HORIZONTAL => Print.Duplex.Horizontal,
                    _ => null
                };
            }

            ColorMode? color = null;
            if (Has(devMode, fields, DevModeBuffer.OffsetColor, DevModeBuffer.DM_COLOR))
            {
                color = devMode.ReadInt16(DevModeBuffer.OffsetColor) switch
                {
                    DevModeBuffer.DMCOLOR_COLOR => ColorMode.Color,
                    // Driver "monochrome" prints shades of grey.
                    DevModeBuffer.DMCOLOR_MONOCHROME => ColorMode.Grayscale,
                    _ => null
                };
            }

            int? copies = null;
            if (Has(devMode, fields, DevModeBuffer.OffsetCopies, DevModeBuffer.DM_COPIES))
            {
                short value = devMode.ReadInt16(DevModeBuffer.OffsetCopies);
                if (value >= 1) copies = value;
            }

            return new DevModeDefaults(paper, duplex, color, copies);
        }

        private static bool Has(DevModeBuffer devMode, uint fields, int offset, uint flag)
            => (fields & flag) != 0 && devMode.Has(offset);
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

            // With PrinterDefault.Paper the paper is not written, so it is not matched either (no error for a paper the
            // driver lacks).
            (short paperId, short width, short length) = (settings.PrinterDefaults & PrinterDefault.Paper) != 0
                ? ((short)0, (short)0, (short)0)
                : ResolvePaper(settings.Paper, papers, matching);

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
                YResolution = yResolution,
                Keep = settings.PrinterDefaults
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

            // Fields in request.Keep stay as the driver's default DEVMODE has them.
            if ((request.Keep & PrinterDefault.Paper) == 0)
            {
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
            }

            devMode.SetInt16(DevModeBuffer.OffsetScale, DevModeBuffer.DM_SCALE, 100);
            if ((request.Keep & PrinterDefault.Copies) == 0)
            {
                devMode.SetInt16(DevModeBuffer.OffsetCopies, DevModeBuffer.DM_COPIES, request.Copies);
                devMode.SetInt16(DevModeBuffer.OffsetCollate, DevModeBuffer.DM_COLLATE,
                                 request.Collate ? DevModeBuffer.DMCOLLATE_TRUE : DevModeBuffer.DMCOLLATE_FALSE);
            }
            if ((request.Keep & PrinterDefault.Color) == 0)
                devMode.SetInt16(DevModeBuffer.OffsetColor, DevModeBuffer.DM_COLOR, request.Color);
            if ((request.Keep & PrinterDefault.Duplex) == 0)
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
