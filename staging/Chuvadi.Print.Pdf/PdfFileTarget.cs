using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Operations;

namespace Chuvadi.Print.Pdf
{
    /// <summary>
    /// "Print to PDF": writes the job as a new PDF whose pages are the printed sheet sides. Each source page is
    /// placed as a vector form XObject (text stays text, nothing is rasterised, P03) at the position, scale and
    /// rotation Chuvadi.Print's <see cref="PageLayout"/> computes for the job's paper, orientation, scale mode,
    /// alignment, margins and auto-rotate; page selection, copies, collation and duplex blank backs follow the
    /// <see cref="PrintPlan"/>, exactly as on a physical printer. Use it with <see cref="PrintJob.SubmitTo"/>.
    /// </summary>
    /// <remarks>
    /// <para>Annotations and form fields are printed by flattening them into the page first
    /// (<see cref="IncludeAnnotations"/>, <see cref="IncludeFormFields"/>).</para>
    /// <para>Settings a vector PDF does not apply are listed in <see cref="LastApproximations"/>: colour mode
    /// (Grayscale and Monochrome keep the original colours) and duplex binding (a PDF has no sheet-turning
    /// edge; the sides are written in order).</para>
    /// <para>The output file is written to a temporary file next to it and renamed into place, so a failed or
    /// cancelled job never leaves a half-written PDF.</para>
    /// </remarks>
    public sealed class PdfFileTarget : IDocumentTarget
    {
        private readonly string? _path;
        private readonly Stream? _stream;

        /// <summary>Prints to the file at <paramref name="outputPath"/> (replaced if it exists).</summary>
        public PdfFileTarget(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            _path = System.IO.Path.GetFullPath(outputPath);
            Name = "PDF file " + System.IO.Path.GetFileName(_path);
        }

        /// <summary>Prints to <paramref name="output"/> (left open).</summary>
        public PdfFileTarget(Stream output)
        {
            _stream = output ?? throw new ArgumentNullException(nameof(output));
            if (!output.CanWrite)
            {
                throw new ArgumentException("The stream must be writable.", nameof(output));
            }

            Name = "PDF stream";
        }

        /// <summary>Display name recorded in the audit trail.</summary>
        public string Name { get; set; }

        /// <summary>Password of the source document, when it is encrypted.</summary>
        public string? Password { get; set; }

        /// <summary>Print annotations (comments, stamps, markup). Default true.</summary>
        public bool IncludeAnnotations { get; set; } = true;

        /// <summary>Print form-field appearances. Default true.</summary>
        public bool IncludeFormFields { get; set; } = true;

        /// <summary>How requested margins combine with <see cref="HardMargins"/>. Default SheetEdge.</summary>
        public MarginReference MarginReference { get; set; } = MarginReference.SheetEdge;

        /// <summary>
        /// Unprintable margins to simulate (as a given printer would impose); a PDF has none. Default none.
        /// </summary>
        public Margins HardMargins { get; set; } = Margins.None;

        /// <summary>Write the blank back sides a duplex job needs (so the PDF prints correctly double-sided). Default true.</summary>
        public bool IncludeBlankBacks { get; set; } = true;

        /// <summary>Cut content off at the printable area, as a printer would. Default true.</summary>
        public bool ClipToPrintableArea { get; set; } = true;

        /// <summary>Encrypt the output PDF (Chuvadi.Pdf <see cref="EncryptionOptions"/>). Default none.</summary>
        public EncryptionOptions? Encryption { get; set; }

        /// <summary>The plan of the last job.</summary>
        public PrintPlan? LastPlan { get; private set; }

        /// <summary>Pages written by the last job (sheet sides, including blank backs when written).</summary>
        public int LastPageCount { get; private set; }

        /// <summary>The file the last job wrote (null for stream output).</summary>
        public string? LastOutputPath { get; private set; }

        /// <summary>Settings of the last job a vector PDF could not apply.</summary>
        public IReadOnlyList<string> LastApproximations { get; private set; } = Array.Empty<string>();

        public PdfFileTarget WithAnnotations(bool include = true)
        {
            IncludeAnnotations = include;
            return this;
        }

        public PdfFileTarget WithFormFields(bool include = true)
        {
            IncludeFormFields = include;
            return this;
        }

        public PdfFileTarget WithMarginReference(MarginReference reference)
        {
            MarginReference = reference;
            return this;
        }

        public PdfFileTarget WithHardMargins(Margins margins)
        {
            HardMargins = margins;
            return this;
        }

        public PdfFileTarget WithBlankBacks(bool include = true)
        {
            IncludeBlankBacks = include;
            return this;
        }

        public PdfFileTarget WithClipping(bool clip = true)
        {
            ClipToPrintableArea = clip;
            return this;
        }

        public PdfFileTarget WithPassword(string? password)
        {
            Password = password;
            return this;
        }

        public PdfFileTarget WithEncryption(EncryptionOptions? encryption)
        {
            Encryption = encryption;
            return this;
        }

        public void Submit(DocumentSubmission submission, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(submission);
            cancellationToken.ThrowIfCancellationRequested();
            PrintSettings settings = submission.Settings;

            using (PdfSource source = new PdfSource(submission.Document, Password))
            {
                PdfDocument doc = source.For(IncludeAnnotations, IncludeFormFields);
                PrintPlan plan = PrintPlan.Create(doc.PageCount, settings, targetHandlesCopies: false);
                PrintTargetCapabilities capabilities = new PrintTargetCapabilities
                {
                    DpiX = 72,
                    DpiY = 72,
                    HardMargins = HardMargins,
                    SupportsDuplex = true,
                    SupportsColor = true,
                };
                PageDimensions sheet = PageLayout.SheetFor(settings.Paper, settings.Orientation);

                PageComposer composer = new PageComposer();
                Dictionary<int, (Transform Transform, RectangleF? Clip)> placements = new Dictionary<int, (Transform, RectangleF?)>();
                int written = 0;
                foreach (PlannedImpression impression in plan.Impressions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (impression.IsBlank && !IncludeBlankBacks)
                    {
                        continue;
                    }

                    composer.AddPage(sheet.WidthPoints, sheet.HeightPoints);
                    written++;
                    if (impression.IsBlank)
                    {
                        continue;
                    }

                    int index = impression.SourcePageNumber - 1;
                    if (!placements.TryGetValue(index, out (Transform Transform, RectangleF? Clip) placed))
                    {
                        PdfPage page = doc.Pages[index];
                        PagePlacement placement = PageLayout.Compute(Displayed(page), settings, capabilities, MarginReference);
                        placed = (PlacementTransform(page, placement), ClipToPrintableArea ? ClipFor(placement) : null);
                        placements[index] = placed;
                    }

                    if (placed.Clip is RectangleF clip)
                    {
                        if (clip.Width <= 0 || clip.Height <= 0)
                        {
                            continue; // entirely outside the printable area
                        }

                        composer.PlacePage(doc, index, placed.Transform, new PlacePageOptions { DestinationClip = clip });
                    }
                    else
                    {
                        composer.PlacePage(doc, index, placed.Transform);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriteOutput(composer);
                LastPlan = plan;
                LastPageCount = written;
                LastOutputPath = _path;
                LastApproximations = Approximations(settings);
            }
        }

        private void WriteOutput(PageComposer composer)
        {
            if (_stream is not null)
            {
                composer.Write(_stream, Encryption);
                _stream.Flush();
                return;
            }

            string path = _path!;
            string? folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    composer.Write(file, Encryption);
                    file.Flush(flushToDisk: true);
                }

                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                throw;
            }
        }

        private static List<string> Approximations(PrintSettings settings)
        {
            List<string> list = new List<string>();
            if (settings.Color != ColorMode.Color)
            {
                list.Add("Color " + settings.Color + ": a vector PDF keeps the original colours.");
            }

            if (settings.Duplex != Duplex.Simplex)
            {
                list.Add("Duplex " + settings.Duplex + ": the sides are written in order (with blank backs); the binding edge is chosen when the PDF is printed.");
            }

            return list;
        }

        /// <summary>The page as displayed: its crop box, turned by its /Rotate.</summary>
        internal static PageDimensions Displayed(PdfPage page)
        {
            (double width, double height) = page.EffectiveSize;
            if (!(width > 0) || !(height > 0))
            {
                throw new InvalidOperationException("Page " + page.PageNumber + " has an empty crop box.");
            }

            return new PageDimensions(width, height);
        }

        /// <summary>
        /// Maps the page's user space onto the sheet (PDF coordinates, origin bottom-left): crop box and /Rotate
        /// to the upright page, then the placement's clockwise rotation, scale and position.
        /// </summary>
        internal static Transform PlacementTransform(PdfPage page, PagePlacement placement)
        {
            PdfRectangle crop = page.CropBox;
            double x1 = Math.Min(crop.X1, crop.X2);
            double y1 = Math.Min(crop.Y1, crop.Y2);
            double w = crop.Width;
            double h = crop.Height;
            int rotate = ((page.Rotate % 360) + 360) % 360;

            // User space to upright display space (origin at the displayed bottom-left corner); PDF /Rotate
            // turns the page clockwise. Same mapping as Chuvadi.Pdf Imposition.FitTransform.
            Transform upright = rotate switch
            {
                90 => new Transform(0, -1, 1, 0, -y1, x1 + w),
                180 => new Transform(-1, 0, 0, -1, x1 + w, y1 + h),
                270 => new Transform(0, 1, -1, 0, y1 + h, -x1),
                _ => new Transform(1, 0, 0, 1, -x1, -y1),
            };
            double dispW = rotate is 90 or 270 ? h : w;
            double dispH = rotate is 90 or 270 ? w : h;

            // The placement's clockwise turn inside the display box.
            Transform turn = placement.Rotation switch
            {
                PageRotation.Clockwise90 => new Transform(0, -1, 1, 0, 0, dispW),
                PageRotation.Rotate180 => new Transform(-1, 0, 0, -1, dispW, dispH),
                PageRotation.Clockwise270 => new Transform(0, 1, -1, 0, dispH, 0),
                _ => Transform.Identity,
            };

            RectanglePoints bounds = placement.ContentBounds;
            double sheetHeight = placement.SheetSize.HeightPoints;
            Transform scale = Transform.CreateScale(placement.ScaleFactor);
            Transform move = Transform.CreateTranslation(bounds.X, sheetHeight - bounds.Y - bounds.Height);
            return upright.Multiply(turn).Multiply(scale).Multiply(move);
        }

        /// <summary>The printable part of the content, in PDF sheet coordinates (origin bottom-left).</summary>
        internal static RectangleF ClipFor(PagePlacement placement)
        {
            RectanglePoints a = placement.PrintableArea;
            RectanglePoints b = placement.ContentBounds;
            double left = Math.Max(a.Left, b.Left);
            double top = Math.Max(a.Top, b.Top);
            double right = Math.Min(a.Right, b.Right);
            double bottom = Math.Min(a.Bottom, b.Bottom);
            double width = Math.Max(0, right - left);
            double height = Math.Max(0, bottom - top);
            return new RectangleF(left, placement.SheetSize.HeightPoints - top - height, width, height);
        }
    }
}
