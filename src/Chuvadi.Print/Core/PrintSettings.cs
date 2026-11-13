using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Print
{
    /// <summary>
    /// Device-independent print intent. Every option carries a sensible default and the
    /// full set of choices (see the enums and value types). Object-initializer friendly.
    /// </summary>
    public sealed class PrintSettings
    {
        public PageSelection Pages { get; set; } = PageSelection.All;
        public int Copies { get; set; } = 1;
        public bool Collate { get; set; } = true;
        public Duplex Duplex { get; set; } = Duplex.Simplex;
        public ColorMode Color { get; set; } = ColorMode.Color;
        public PaperSize Paper { get; set; } = PaperSize.A4;
        public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
        public ScaleMode Scale { get; set; } = ScaleMode.FitToPage;

        /// <summary>Percentage used only when <see cref="Scale"/> is <see cref="ScaleMode.Custom"/>.</summary>
        public double CustomScalePercent { get; set; } = 100.0;

        public ContentAlignment Alignment { get; set; } = ContentAlignment.Center;
        public Margins Margins { get; set; } = Margins.Default;

        /// <summary>
        /// When true, a page whose orientation differs from the printable area's (a landscape page on a
        /// portrait sheet, or the reverse) is turned 90 degrees clockwise to fit better. Default false.
        /// </summary>
        /// <remarks>
        /// When the printer lays the document out itself (the PDF is sent as it is, to an IPP printer or a CUPS
        /// queue) and <see cref="Orientation"/> is <see cref="PageOrientation.Portrait"/>, the orientation is left
        /// to the printer: <c>orientation-requested</c> "none" is sent when the printer lists it, otherwise no
        /// orientation at all, so the printer or CUPS turns landscape pages itself.
        /// </remarks>
        public bool AutoRotate { get; set; }

        /// <summary>When true, print without showing any dialog (unattended/silent printing).</summary>
        public bool Silent { get; set; }

        /// <summary>
        /// Settings left to the printer's own default: paper, one- or two-sided, colour and copies. Default
        /// <see cref="PrinterDefault.None"/>: every setting comes from these settings, exactly as before.
        /// </summary>
        /// <remarks>
        /// <para>A flagged setting is not sent to the printer (IPP job ticket, Windows DEVMODE and PrintTicket), so the
        /// printer applies its default.</para>
        /// <para>When Chuvadi.Print renders the pages itself, it needs to know that default to lay them out (sheet size,
        /// the back of a two-sided sheet, grey or colour). The target reports it
        /// (<see cref="PrintTargetCapabilities.DefaultPaper"/> and the others), and <see cref="ResolvePrinterDefaults"/>
        /// uses it. When the printer reports no default, the job is laid out with the value in these settings, nothing
        /// is sent, and the audit event says so (<see cref="PrintAuditEvent.PrinterDefaults"/>).</para>
        /// <para>Orientation and the tray are not included: orientation belongs to the document's layout, and an unset
        /// tray already means the printer's default.</para>
        /// </remarks>
        public PrinterDefault PrinterDefaults { get; set; }

        public PrintSettings Clone() => new()
        {
            Pages = Pages,
            Copies = Copies,
            Collate = Collate,
            Duplex = Duplex,
            Color = Color,
            Paper = Paper,
            Orientation = Orientation,
            Scale = Scale,
            CustomScalePercent = CustomScalePercent,
            Alignment = Alignment,
            Margins = Margins,
            AutoRotate = AutoRotate,
            Silent = Silent,
            PrinterDefaults = PrinterDefaults
        };

        /// <summary>
        /// A copy of these settings for laying out a job on a printer with these capabilities: each setting flagged
        /// in <see cref="PrinterDefaults"/> takes the printer's reported default. The flags stay set, so the target
        /// still leaves those settings out of what it sends.
        /// </summary>
        /// <param name="capabilities">What the target reported, including its defaults.</param>
        /// <param name="notes">
        /// Receives one line per flagged setting, saying what was used: "paper: printer default (A4)", or
        /// "paper: printer default not reported; laid out for A4" when the printer reports none.
        /// </param>
        /// <remarks>
        /// <see cref="PrinterDefault.Copies"/> always gives one copy here: one set is produced, and a printer that makes
        /// copies applies its own default number.
        /// </remarks>
        public PrintSettings ResolvePrinterDefaults(PrintTargetCapabilities capabilities, ICollection<string>? notes = null)
        {
            if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
            PrintSettings resolved = Clone();
            PrinterDefault flags = PrinterDefaults;
            if (flags == PrinterDefault.None) return resolved;

            if ((flags & PrinterDefault.Paper) != 0)
            {
                if (capabilities.DefaultPaper is PaperSize paper)
                {
                    resolved.Paper = paper;
                    notes?.Add("paper: printer default (" + paper + ")");
                }
                else
                {
                    notes?.Add("paper: printer default not reported; laid out for " + Paper);
                }
            }

            if ((flags & PrinterDefault.Duplex) != 0)
            {
                if (capabilities.DefaultDuplex is Duplex duplex)
                {
                    resolved.Duplex = duplex;
                    notes?.Add("duplex: printer default (" + duplex + ")");
                }
                else
                {
                    notes?.Add("duplex: printer default not reported; laid out for " + Duplex);
                }
            }

            if ((flags & PrinterDefault.Color) != 0)
            {
                if (capabilities.DefaultColor is ColorMode color)
                {
                    resolved.Color = color;
                    notes?.Add("color: printer default (" + color + ")");
                }
                else
                {
                    notes?.Add("color: printer default not reported; laid out for " + Color);
                }
            }

            if ((flags & PrinterDefault.Copies) != 0)
            {
                resolved.Copies = 1;
                if (!capabilities.SupportsCopies)
                    notes?.Add("copies: 1 (the printer does not make copies)");
                else if (capabilities.DefaultCopies is int copies)
                    notes?.Add("copies: printer default (" + copies.ToString(CultureInfo.InvariantCulture) + ")");
                else
                    notes?.Add("copies: printer default (not reported)");
            }

            return resolved;
        }

        /// <summary>
        /// The audit lines for a job the printer lays out itself (the document is sent as it is): each flagged setting
        /// is the printer's default, whatever it is.
        /// </summary>
        internal static void DescribeDocumentDefaults(PrinterDefault flags, ICollection<string> notes)
        {
            if ((flags & PrinterDefault.Paper) != 0) notes.Add("paper: printer default");
            if ((flags & PrinterDefault.Duplex) != 0) notes.Add("duplex: printer default");
            if ((flags & PrinterDefault.Color) != 0) notes.Add("color: printer default");
            if ((flags & PrinterDefault.Copies) != 0) notes.Add("copies: printer default");
        }
    }
}
