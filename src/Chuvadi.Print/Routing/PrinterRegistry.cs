using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chuvadi.Print
{
    /// <summary>How a saved printer is reached.</summary>
    public enum PrinterConnection
    {
        /// <summary>An IPP printer; <see cref="SavedPrinter.Address"/> is its ipp:// or ipps:// URI.</summary>
        Ipp = 0,

        /// <summary>A CUPS queue; the address is its IPP URI, for example ipp://localhost:631/printers/ward3.</summary>
        Cups = 1,

        /// <summary>A raw port; the address is socket://host[:port] (port 9100 when omitted).</summary>
        RawSocket = 2,

        /// <summary>A Windows print queue; the address is the queue name.</summary>
        WindowsQueue = 3
    }

    /// <summary>
    /// One printer an application has set up: how to reach it, an optional pinned certificate and forced route, and
    /// the printer's capabilities as last read, with when they were read. Credentials are never stored here.
    /// </summary>
    public sealed class SavedPrinter
    {
        /// <summary>The printer's name in the application; unique in a registry (case is ignored).</summary>
        public string Name { get; set; } = string.Empty;

        public PrinterConnection Connection { get; set; }

        /// <summary>URI or queue name, as described for each <see cref="PrinterConnection"/>.</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>SHA-256 fingerprint of the printer's certificate to trust (ipps://), or null.</summary>
        public string? PinnedCertificate { get; set; }

        /// <summary>A route every job to this printer must use (a site override), or null to let the router choose.</summary>
        public PrintRouteKind? ForcedRoute { get; set; }

        public string? Location { get; set; }
        public string? Notes { get; set; }

        /// <summary>
        /// The printer's capabilities as last read, in the transport's own encoding (for IPP printers, the encoded
        /// printer attributes; Chuvadi.Print.Ipp's IppCapabilityCache reads and writes them). Null when never read.
        /// </summary>
        public byte[]? Capabilities { get; set; }

        /// <summary>When <see cref="Capabilities"/> were read (UTC), or null.</summary>
        public DateTimeOffset? CapabilitiesReadAt { get; set; }

        /// <summary>How old the saved capabilities are at <paramref name="now"/>; null when none are saved.</summary>
        public TimeSpan? CapabilitiesAge(DateTimeOffset now)
            => Capabilities is null || CapabilitiesReadAt is null ? null : now - CapabilitiesReadAt.Value;

        /// <summary>A deep copy.</summary>
        public SavedPrinter Clone() => new()
        {
            Name = Name,
            Connection = Connection,
            Address = Address,
            PinnedCertificate = PinnedCertificate,
            ForcedRoute = ForcedRoute,
            Location = Location,
            Notes = Notes,
            Capabilities = Capabilities is null ? null : (byte[])Capabilities.Clone(),
            CapabilitiesReadAt = CapabilitiesReadAt
        };

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("A saved printer needs a name.");
            if (string.IsNullOrWhiteSpace(Address)) throw new ArgumentException("Saved printer '" + Name + "' needs an address.");
            if (!Enum.IsDefined(Connection)) throw new ArgumentException("Saved printer '" + Name + "' has an unknown connection.");
            if (ForcedRoute.HasValue && !Enum.IsDefined(ForcedRoute.Value))
                throw new ArgumentException("Saved printer '" + Name + "' has an unknown forced route.");
            if (Capabilities is not null && CapabilitiesReadAt is null)
                throw new ArgumentException("Saved printer '" + Name + "' has capabilities without the time they were read.");
        }
    }

    /// <summary>
    /// The printers an application has set up, saved to a JSON file. Thread-safe. Entries are copied in and out, so
    /// changing a returned <see cref="SavedPrinter"/> does not change the registry until it is passed to
    /// <see cref="Set"/>. Credentials are never stored; keep them in the operating system's credential store.
    /// </summary>
    public sealed class PrinterRegistry
    {
        private const int FormatVersion = 1;
        private readonly object _gate = new();
        private readonly Dictionary<string, SavedPrinter> _printers = new(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Copies of every saved printer, by name.</summary>
        public IReadOnlyList<SavedPrinter> Printers
        {
            get
            {
                var list = new List<SavedPrinter>();
                lock (_gate) foreach (SavedPrinter p in _printers.Values) list.Add(p.Clone());
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return list;
            }
        }

        public int Count
        {
            get { lock (_gate) return _printers.Count; }
        }

        /// <summary>Adds the printer, or replaces the one with the same name.</summary>
        public void Set(SavedPrinter printer)
        {
            if (printer is null) throw new ArgumentNullException(nameof(printer));
            printer.Validate();
            SavedPrinter copy = printer.Clone();
            lock (_gate) _printers[copy.Name] = copy;
        }

        /// <summary>A copy of the printer with this name.</summary>
        public bool TryGet(string name, out SavedPrinter? printer)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_gate)
            {
                if (_printers.TryGetValue(name, out SavedPrinter? p))
                {
                    printer = p.Clone();
                    return true;
                }
            }
            printer = null;
            return false;
        }

        public bool Remove(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            lock (_gate) return _printers.Remove(name);
        }

        /// <summary>Writes the registry as JSON (UTF-8, no byte order mark).</summary>
        public void WriteTo(Stream output)
        {
            if (output is null) throw new ArgumentNullException(nameof(output));
            var file = new RegistryFile { Version = FormatVersion, Printers = new List<SavedPrinter>(Printers) };
            JsonSerializer.Serialize(output, file, Json);
        }

        /// <summary>Saves to a file: written to a temporary file next to it, then moved over it, so a crash never leaves half a file.</summary>
        public void SaveTo(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            string full = Path.GetFullPath(path);
            string? folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    WriteTo(stream);
                    stream.Flush(true);
                }
                File.Move(temp, full, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>Reads a registry written by <see cref="WriteTo"/>.</summary>
        /// <exception cref="InvalidDataException">The data is not a registry, is a newer format, or holds an invalid entry.</exception>
        public static PrinterRegistry Read(Stream input)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            RegistryFile? file;
            try
            {
                file = JsonSerializer.Deserialize<RegistryFile>(input, Json);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Not a printer registry: " + ex.Message, ex);
            }
            if (file is null) throw new InvalidDataException("Not a printer registry.");
            if (file.Version < 1 || file.Version > FormatVersion)
                throw new InvalidDataException("Printer registry format " + file.Version + " is not supported (this version reads format " + FormatVersion + ").");
            var registry = new PrinterRegistry();
            foreach (SavedPrinter? p in file.Printers ?? new List<SavedPrinter>())
            {
                if (p is null) continue;
                try { registry.Set(p); }
                catch (ArgumentException ex) { throw new InvalidDataException(ex.Message, ex); }
            }
            return registry;
        }

        /// <summary>Loads a registry file; a missing file gives an empty registry.</summary>
        public static PrinterRegistry Load(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) return new PrinterRegistry();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream);
        }

        private sealed class RegistryFile
        {
            public int Version { get; set; }
            public List<SavedPrinter>? Printers { get; set; }
        }
    }
}
