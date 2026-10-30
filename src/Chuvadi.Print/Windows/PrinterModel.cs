using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Print.Windows
{
    /// <summary>Printer status flags (Win32 PRINTER_STATUS_*). Every flag the spooler reports is represented.</summary>
    [Flags]
    public enum PrinterStatus : uint
    {
        None = 0,
        Paused = 0x00000001,
        Error = 0x00000002,
        PendingDeletion = 0x00000004,
        PaperJam = 0x00000008,
        PaperOut = 0x00000010,
        ManualFeed = 0x00000020,
        PaperProblem = 0x00000040,
        Offline = 0x00000080,
        IOActive = 0x00000100,
        Busy = 0x00000200,
        Printing = 0x00000400,
        OutputBinFull = 0x00000800,
        NotAvailable = 0x00001000,
        Waiting = 0x00002000,
        Processing = 0x00004000,
        Initializing = 0x00008000,
        WarmingUp = 0x00010000,
        TonerLow = 0x00020000,
        NoToner = 0x00040000,
        PagePunt = 0x00080000,
        UserIntervention = 0x00100000,
        OutOfMemory = 0x00200000,
        DoorOpen = 0x00400000,
        ServerUnknown = 0x00800000,
        PowerSave = 0x01000000
    }

    /// <summary>Printer queue attributes (Win32 PRINTER_ATTRIBUTE_*).</summary>
    [Flags]
    public enum PrinterAttributes : uint
    {
        None = 0,
        Queued = 0x00000001,
        Direct = 0x00000002,
        Default = 0x00000004,
        Shared = 0x00000008,
        Network = 0x00000010,
        Hidden = 0x00000020,
        Local = 0x00000040,
        EnableDevQuery = 0x00000080,
        KeepPrintedJobs = 0x00000100,
        DoCompleteFirst = 0x00000200,
        WorkOffline = 0x00000400,
        EnableBidi = 0x00000800,
        RawOnly = 0x00001000,
        Published = 0x00002000,
        Fax = 0x00004000,
        TerminalServer = 0x00008000
    }

    /// <summary>An installed printer queue.</summary>
    public sealed class PrinterInfo
    {
        internal PrinterInfo(string name, string? serverName, string? shareName, string? portName, string? driverName,
                             string? comment, string? location, PrinterAttributes attributes, PrinterStatus status,
                             int jobCount, bool isDefault)
        {
            Name = name;
            ServerName = serverName;
            ShareName = shareName;
            PortName = portName;
            DriverName = driverName;
            Comment = comment;
            Location = location;
            Attributes = attributes;
            Status = status;
            JobCount = jobCount;
            IsDefault = isDefault;
        }

        /// <summary>The queue name to print to (for a connection, "\\server\share").</summary>
        public string Name { get; }
        public string? ServerName { get; }
        public string? ShareName { get; }
        public string? PortName { get; }
        public string? DriverName { get; }
        public string? Comment { get; }
        public string? Location { get; }
        public PrinterAttributes Attributes { get; }
        public PrinterStatus Status { get; }

        /// <summary>Jobs currently in the queue.</summary>
        public int JobCount { get; }

        public bool IsDefault { get; }
        public bool IsNetwork => (Attributes & PrinterAttributes.Network) != 0 || ServerName is not null;
        public bool IsShared => (Attributes & PrinterAttributes.Shared) != 0;
        public bool IsOffline => (Status & PrinterStatus.Offline) != 0 || (Attributes & PrinterAttributes.WorkOffline) != 0;

        public override string ToString() => Name;
    }

    /// <summary>A paper the driver offers.</summary>
    public sealed class PrinterPaper
    {
        internal PrinterPaper(short id, string name, PaperSize size)
        {
            Id = id;
            Name = name;
            Size = size;
        }

        /// <summary>Driver paper id (DMPAPER_* or a driver-defined value).</summary>
        public short Id { get; }
        public string Name { get; }

        /// <summary>Size as the driver reports it (portrait).</summary>
        public PaperSize Size { get; }

        public override string ToString() => Name + " (" + Id.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>A paper source (tray, feeder, manual slot) the driver offers.</summary>
    public sealed class PrinterBin
    {
        internal PrinterBin(short id, string name)
        {
            Id = id;
            Name = name;
        }

        /// <summary>Driver bin id (DMBIN_* or a driver-defined value).</summary>
        public short Id { get; }
        public string Name { get; }

        public override string ToString() => Name;
    }

    /// <summary>A media type (plain, glossy, transparency, label...) the driver offers.</summary>
    public sealed class PrinterMediaType
    {
        internal PrinterMediaType(uint id, string name)
        {
            Id = id;
            Name = name;
        }

        /// <summary>Driver media-type id (DMMEDIA_* or a driver-defined value).</summary>
        public uint Id { get; }
        public string Name { get; }

        public override string ToString() => Name;
    }

    /// <summary>A device resolution in dots per inch.</summary>
    public readonly struct PrinterResolution : IEquatable<PrinterResolution>
    {
        public PrinterResolution(int x, int y)
        {
            if (x <= 0) throw new ArgumentOutOfRangeException(nameof(x));
            if (y <= 0) throw new ArgumentOutOfRangeException(nameof(y));
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(PrinterResolution other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is PrinterResolution r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(PrinterResolution left, PrinterResolution right) => left.Equals(right);
        public static bool operator !=(PrinterResolution left, PrinterResolution right) => !left.Equals(right);
        public override string ToString() => X.ToString(CultureInfo.InvariantCulture) + "x" + Y.ToString(CultureInfo.InvariantCulture) + " dpi";
    }

    /// <summary>Driver quality presets (DMRES_*), an alternative to an explicit <see cref="PrinterResolution"/>.</summary>
    public enum PrintQuality : short
    {
        Draft = -1,
        Low = -2,
        Medium = -3,
        High = -4
    }

    /// <summary>How <see cref="PrintSettings.Paper"/> is matched to the driver's papers.</summary>
    public enum PaperMatching
    {
        /// <summary>Use the driver paper whose size matches within 1 mm; otherwise request a custom size. Default.</summary>
        ExactOrCustom,
        /// <summary>Use a matching driver paper; fail the job if the driver has none.</summary>
        ExactOnly,
        /// <summary>Always request the exact dimensions as a custom size.</summary>
        CustomOnly
    }

    /// <summary>Everything a printer's driver reports about what it can do.</summary>
    public sealed class WindowsPrinterCapabilities
    {
        internal WindowsPrinterCapabilities(string printerName, IReadOnlyList<PrinterPaper> papers, IReadOnlyList<PrinterBin> bins,
                                            IReadOnlyList<PrinterMediaType> mediaTypes, IReadOnlyList<PrinterResolution> resolutions,
                                            int maxCopies, bool supportsCollate, bool supportsDuplex, bool supportsColor,
                                            int landscapeRotation, PaperSize? minimumCustomSize, PaperSize? maximumCustomSize,
                                            DeviceGeometry geometry)
        {
            PrinterName = printerName;
            Papers = papers;
            Bins = bins;
            MediaTypes = mediaTypes;
            Resolutions = resolutions;
            MaxCopies = maxCopies;
            SupportsCollate = supportsCollate;
            SupportsDuplex = supportsDuplex;
            SupportsColor = supportsColor;
            LandscapeRotation = landscapeRotation;
            MinimumCustomSize = minimumCustomSize;
            MaximumCustomSize = maximumCustomSize;
            Geometry = geometry;
        }

        public string PrinterName { get; }
        public IReadOnlyList<PrinterPaper> Papers { get; }
        public IReadOnlyList<PrinterBin> Bins { get; }
        public IReadOnlyList<PrinterMediaType> MediaTypes { get; }
        public IReadOnlyList<PrinterResolution> Resolutions { get; }

        /// <summary>Most copies the driver makes by itself (1 = no driver copies).</summary>
        public int MaxCopies { get; }

        public bool SupportsCopies => MaxCopies > 1;
        public bool SupportsCollate { get; }
        public bool SupportsDuplex { get; }
        public bool SupportsColor { get; }

        /// <summary>Degrees the driver rotates portrait content for landscape: 0 (no landscape), 90 or 270.</summary>
        public int LandscapeRotation { get; }

        /// <summary>Smallest custom paper the driver accepts, if it reports one.</summary>
        public PaperSize? MinimumCustomSize { get; }

        /// <summary>Largest custom paper the driver accepts, if it reports one.</summary>
        public PaperSize? MaximumCustomSize { get; }

        /// <summary>Device resolution for the settings these capabilities were read with.</summary>
        public PrinterResolution DeviceResolution => new(Geometry.DpiX, Geometry.DpiY);

        /// <summary>Non-printable edges, in points, for the settings these capabilities were read with.</summary>
        public Margins HardMargins => Geometry.HardMargins;

        /// <summary>Sheet size the device reports, in points, for the settings these capabilities were read with.</summary>
        public PageDimensions DeviceSheet => Geometry.SheetSize;

        internal DeviceGeometry Geometry { get; }

        /// <summary>Converts to the device-independent form the print planner uses.</summary>
        /// <param name="maxRasterDpi">Upper bound on the rendering resolution (null = full device resolution).</param>
        public PrintTargetCapabilities ToTargetCapabilities(double? maxRasterDpi = 300)
        {
            if (maxRasterDpi.HasValue && (!(maxRasterDpi.Value > 0) || double.IsInfinity(maxRasterDpi.Value)))
                throw new ArgumentOutOfRangeException(nameof(maxRasterDpi));
            double dpiX = Geometry.DpiX, dpiY = Geometry.DpiY;
            if (maxRasterDpi.HasValue)
            {
                dpiX = Math.Min(dpiX, maxRasterDpi.Value);
                dpiY = Math.Min(dpiY, maxRasterDpi.Value);
            }
            return new PrintTargetCapabilities
            {
                DpiX = dpiX,
                DpiY = dpiY,
                HardMargins = Geometry.HardMargins,
                SupportsCopies = SupportsCopies,
                SupportsCollate = SupportsCollate,
                SupportsDuplex = SupportsDuplex,
                SupportsColor = SupportsColor,
                PreferredPixelFormat = RasterPixelFormat.Bgra32
            };
        }
    }

    /// <summary>Thrown when a Windows printing call fails.</summary>
    public class WindowsPrintException : Exception
    {
        public WindowsPrintException(string message) : base(message) { }
        public WindowsPrintException(string message, Exception innerException) : base(message, innerException) { }

        public WindowsPrintException(string message, int nativeErrorCode)
            : base(message + " (Win32 error " + nativeErrorCode.ToString(CultureInfo.InvariantCulture) + ")")
        {
            NativeErrorCode = nativeErrorCode;
        }

        /// <summary>The Win32 error code, when the failure came from a Win32 call.</summary>
        public int NativeErrorCode { get; }
    }

    /// <summary>Thrown when the named printer does not exist or cannot be opened.</summary>
    public sealed class PrinterNotFoundException : WindowsPrintException
    {
        public PrinterNotFoundException(string printerName, int nativeErrorCode)
            : base("Printer not found or not accessible: '" + printerName + "'", nativeErrorCode)
        {
            PrinterName = printerName;
        }

        public string PrinterName { get; }
    }
}
