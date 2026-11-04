using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>A printer found on the local network by <see cref="NetworkPrinterDiscovery"/>.</summary>
    public sealed class DiscoveredPrinter
    {
        internal DiscoveredPrinter(string instanceName, string serviceType, string? hostName, int port,
                                   IReadOnlyList<IPAddress> addresses, IReadOnlyDictionary<string, string> txt, IPAddress responder)
        {
            InstanceName = instanceName;
            ServiceType = serviceType;
            HostName = hostName;
            Port = port;
            Addresses = addresses;
            Txt = txt;
            Responder = responder;
            Uri = BuildUri();
        }

        /// <summary>The service's name as the printer advertises it, for example "HP Laser Ward 3".</summary>
        public string InstanceName { get; }

        /// <summary>The DNS-SD service type: "_ipp._tcp", "_ipps._tcp" or "_pdl-datastream._tcp" (raw port).</summary>
        public string ServiceType { get; }

        /// <summary>The printer's host name (the SRV target, without the final dot); null when it did not say.</summary>
        public string? HostName { get; }

        /// <summary>The service port (631 for IPP, 9100 for the raw port, as advertised); 0 when it did not say.</summary>
        public int Port { get; }

        /// <summary>The printer's addresses from its A and AAAA records; when it sent none, the address it answered from.</summary>
        public IReadOnlyList<IPAddress> Addresses { get; }

        /// <summary>The TXT record keys and values (keys in lower case), for example "ty", "note", "pdl", "rp", "UUID".</summary>
        public IReadOnlyDictionary<string, string> Txt { get; }

        /// <summary>The address the answer came from.</summary>
        public IPAddress Responder { get; }

        /// <summary>The printer's make and model (TXT "ty"), when given.</summary>
        public string? MakeAndModel => Txt.TryGetValue("ty", out string? v) ? v : null;

        /// <summary>The printer's location (TXT "note"), when given.</summary>
        public string? Location => Txt.TryGetValue("note", out string? v) ? v : null;

        /// <summary>
        /// The URI to print to: ipp://host:port/rp or ipps://host:port/rp (rp from the TXT record, empty when absent),
        /// or socket://host:port for the raw port. The host is the first IPv4 address, else an IPv6 address, else
        /// the host name. Null when the port is unknown.
        /// </summary>
        public string? Uri { get; }

        private string? BuildUri()
        {
            if (Port <= 0) return null;
            string host = HostForUri();
            string port = Port.ToString(CultureInfo.InvariantCulture);
            Txt.TryGetValue("rp", out string? rp);
            string path = "/" + (rp ?? string.Empty).TrimStart('/');
            return ServiceType switch
            {
                "_ipp._tcp" => "ipp://" + host + ":" + port + path,
                "_ipps._tcp" => "ipps://" + host + ":" + port + path,
                "_pdl-datastream._tcp" => "socket://" + host + ":" + port,
                _ => null
            };
        }

        private string HostForUri()
        {
            foreach (IPAddress a in Addresses) if (a.AddressFamily == AddressFamily.InterNetwork) return a.ToString();
            foreach (IPAddress a in Addresses)
                if (a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal) return "[" + a + "]";
            return HostName ?? Responder.ToString();
        }

        /// <summary>
        /// A <see cref="SavedPrinter"/> for this service: <see cref="PrinterConnection.Ipp"/> for IPP and IPPS,
        /// <see cref="PrinterConnection.RawSocket"/> for the raw port, with the location (TXT "note") and the make and
        /// model (TXT "ty") as notes. <paramref name="name"/> defaults to the instance name.
        /// </summary>
        /// <exception cref="InvalidOperationException">The printer did not say its port, so there is no URI.</exception>
        public SavedPrinter ToSavedPrinter(string? name = null) => new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? InstanceName : name,
            Connection = ServiceType == "_pdl-datastream._tcp" ? PrinterConnection.RawSocket : PrinterConnection.Ipp,
            Address = Uri ?? throw new InvalidOperationException("'" + InstanceName + "' did not give its port."),
            Location = Location,
            Notes = MakeAndModel
        };

        public override string ToString() => InstanceName + " (" + ServiceType + ") " + (Uri ?? "no address");
    }

    /// <summary>Options for <see cref="NetworkPrinterDiscovery.Browse"/>.</summary>
    public sealed class NetworkPrinterDiscoveryOptions
    {
        private TimeSpan _duration = TimeSpan.FromSeconds(3);

        /// <summary>
        /// How long to listen for answers. Default 3 seconds; at most 60. Windows Firewall lets answers to a
        /// multicast query in for about 3 seconds, so longer values mostly help on other systems.
        /// </summary>
        public TimeSpan Duration
        {
            get => _duration;
            set => _duration = value > TimeSpan.Zero && value <= TimeSpan.FromSeconds(60)
                ? value : throw new ArgumentOutOfRangeException(nameof(Duration), "Between 0 and 60 seconds.");
        }

        /// <summary>
        /// The service types to look for. Default "_ipp._tcp" (IPP), "_ipps._tcp" (IPP over TLS) and
        /// "_pdl-datastream._tcp" (raw port 9100).
        /// </summary>
        public IList<string> ServiceTypes { get; } = new List<string> { "_ipp._tcp", "_ipps._tcp", "_pdl-datastream._tcp" };
    }

    /// <summary>
    /// Finds printers on the local network with DNS-SD over multicast DNS (RFC 6762, RFC 6763), in our own code.
    /// It runs only when the application calls <see cref="Browse"/> (D-132 item 6). It sends one-shot queries from
    /// a temporary port and reads the answers sent back to that port (RFC 6762 section 5.1); it never listens on
    /// port 5353 and opens no service port (P07). Only IPv4 interfaces are queried.
    /// </summary>
    public static class NetworkPrinterDiscovery
    {
        private static readonly IPEndPoint MulticastGroup = new(IPAddress.Parse("224.0.0.251"), 5353);

        /// <summary>
        /// Queries every active IPv4 network interface and returns the printers that answered within the duration,
        /// one entry per advertised service (a printer offering IPP and the raw port appears twice). Returns an
        /// empty list when no interface can send multicast.
        /// </summary>
        public static IReadOnlyList<DiscoveredPrinter> Browse(NetworkPrinterDiscoveryOptions? options = null,
                                                             CancellationToken cancellationToken = default)
        {
            var local = new List<IPAddress>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || !nic.SupportsMulticast) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) local.Add(ua.Address);
            }
            return BrowseFrom(options ?? new NetworkPrinterDiscoveryOptions(), local, MulticastGroup, cancellationToken);
        }

        /// <summary>The same as Browse, with the local addresses and the destination given (tests).</summary>
        internal static IReadOnlyList<DiscoveredPrinter> BrowseFrom(NetworkPrinterDiscoveryOptions options, IReadOnlyList<IPAddress> localAddresses,
                                                               IPEndPoint destination, CancellationToken cancellationToken)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            var types = new List<string>();
            foreach (string t in options.ServiceTypes)
            {
                if (string.IsNullOrWhiteSpace(t)) throw new ArgumentException("A service type is empty.", nameof(options));
                types.Add(t.Trim().TrimEnd('.').ToLowerInvariant());
            }
            var state = new BrowseState(types);
            var sockets = new List<Socket>();
            try
            {
                foreach (IPAddress address in localAddresses)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    try
                    {
                        socket.Bind(new IPEndPoint(address, 0));
                        if (IsMulticast(destination.Address))
                        {
                            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                        }
                        sockets.Add(socket);
                    }
                    catch (SocketException)
                    {
                        socket.Dispose(); // an interface that cannot send: skip it
                    }
                }
                if (sockets.Count == 0) return Array.Empty<DiscoveredPrinter>();

                var questions = new List<(string Name, ushort Type)>();
                foreach (string t in types) questions.Add((t + ".local", DnsMessage.TypePtr));
                Send(sockets, destination, DnsMessage.Query(questions));

                DateTime start = DateTime.UtcNow;
                DateTime followUpAt = start + TimeSpan.FromTicks(options.Duration.Ticks / 2);
                DateTime deadline = start + options.Duration;
                bool followedUp = false;
                var buffer = new byte[9000];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DateTime now = DateTime.UtcNow;
                    if (now >= deadline) break;
                    if (!followedUp && now >= followUpAt)
                    {
                        followedUp = true;
                        List<(string, ushort)> missing = state.MissingQuestions();
                        if (missing.Count > 0) Send(sockets, destination, DnsMessage.Query(missing));
                    }
                    DateTime wake = followedUp ? deadline : followUpAt;
                    int micros = (int)Math.Clamp((wake - now).TotalMilliseconds * 1000, 1000, 200_000);
                    var readable = new List<Socket>(sockets);
                    Socket.Select(readable, null, null, micros);
                    foreach (Socket s in readable)
                    {
                        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        int read;
                        try { read = s.ReceiveFrom(buffer, ref from); }
                        catch (SocketException) { continue; }
                        try { state.Add(DnsMessage.ParseRecords(buffer.AsSpan(0, read)), ((IPEndPoint)from).Address); }
                        catch (FormatException) { /* not a DNS answer: ignore */ }
                    }
                }
                return state.Results();
            }
            finally
            {
                foreach (Socket s in sockets) s.Dispose();
            }
        }

        private static void Send(List<Socket> sockets, IPEndPoint destination, byte[] query)
        {
            foreach (Socket s in sockets)
            {
                try { s.SendTo(query, destination); }
                catch (SocketException) { /* this interface cannot reach the group now */ }
            }
        }

        private static bool IsMulticast(IPAddress a) => a.AddressFamily == AddressFamily.InterNetwork && (a.GetAddressBytes()[0] & 0xF0) == 0xE0;

        /// <summary>Everything heard so far, keyed by lower-case DNS names.</summary>
        private sealed class BrowseState
        {
            private readonly List<string> _types;
            private readonly Dictionary<string, (string Type, string Instance, string FullName)> _instances = new(StringComparer.Ordinal);
            private readonly Dictionary<string, (string Target, int Port)> _srv = new(StringComparer.Ordinal);
            private readonly Dictionary<string, Dictionary<string, string>> _txt = new(StringComparer.Ordinal);
            private readonly Dictionary<string, List<IPAddress>> _addresses = new(StringComparer.Ordinal);
            private readonly Dictionary<string, IPAddress> _responder = new(StringComparer.Ordinal);

            public BrowseState(List<string> types) => _types = types;

            public void Add(List<DnsRecord> records, IPAddress from)
            {
                foreach (DnsRecord r in records)
                {
                    string key = r.Name.Key;
                    switch (r.Type)
                    {
                        case DnsMessage.TypePtr when r.Target is not null:
                            foreach (string t in _types)
                            {
                                if (key != t + ".local") continue;
                                string full = r.Target.Key;
                                if (!_instances.ContainsKey(full)) _instances[full] = (t, r.Target.Labels[0], r.Target.Display);
                                _responder[full] = from;
                            }
                            break;
                        case DnsMessage.TypeSrv when r.Target is not null:
                            _srv[key] = (r.Target.Display, r.Port);
                            _responder.TryAdd(key, from);
                            break;
                        case DnsMessage.TypeTxt when r.Txt is not null:
                            _txt[key] = r.Txt;
                            break;
                        case DnsMessage.TypeA:
                        case DnsMessage.TypeAaaa:
                            if (r.Address is null) break;
                            if (!_addresses.TryGetValue(key, out List<IPAddress>? list)) _addresses[key] = list = new List<IPAddress>();
                            if (!list.Contains(r.Address)) list.Add(r.Address);
                            break;
                    }
                }
            }

            public List<(string, ushort)> MissingQuestions()
            {
                var q = new List<(string, ushort)>();
                foreach (var (key, i) in _instances)
                {
                    if (!_srv.TryGetValue(key, out var srv)) q.Add((i.FullName, DnsMessage.TypeSrv));
                    else if (!_addresses.ContainsKey(srv.Target.ToLowerInvariant())) q.Add((srv.Target, DnsMessage.TypeA));
                    if (!_txt.ContainsKey(key)) q.Add((i.FullName, DnsMessage.TypeTxt));
                }
                return q;
            }

            public IReadOnlyList<DiscoveredPrinter> Results()
            {
                var list = new List<DiscoveredPrinter>();
                foreach (var (key, i) in _instances)
                {
                    _srv.TryGetValue(key, out var srv);
                    IPAddress responder = _responder.TryGetValue(key, out IPAddress? r) ? r : IPAddress.None;
                    var addresses = new List<IPAddress>();
                    if (srv.Target is not null && _addresses.TryGetValue(srv.Target.ToLowerInvariant(), out List<IPAddress>? a)) addresses.AddRange(a);
                    if (addresses.Count == 0 && !responder.Equals(IPAddress.None)) addresses.Add(responder);
                    var txt = _txt.TryGetValue(key, out var t) ? t : new Dictionary<string, string>();
                    list.Add(new DiscoveredPrinter(i.Instance, i.Type, srv.Target, srv.Port, addresses, txt, responder));
                }
                list.Sort((x, y) =>
                {
                    int c = string.Compare(x.InstanceName, y.InstanceName, StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : string.CompareOrdinal(x.ServiceType, y.ServiceType);
                });
                return list;
            }
        }
    }

    /// <summary>A DNS name as labels; <see cref="Key"/> is the lower-case dotted form used for matching.</summary>
    internal sealed class DnsName
    {
        public DnsName(IReadOnlyList<string> labels)
        {
            Labels = labels;
            var parts = new string[labels.Count];
            for (int i = 0; i < labels.Count; i++) parts[i] = labels[i].Replace(".", "\\.");
            Display = string.Join(".", parts);
            Key = Display.ToLowerInvariant();
        }

        public IReadOnlyList<string> Labels { get; }
        public string Display { get; }
        public string Key { get; }
    }

    internal sealed class DnsRecord
    {
        public DnsName Name { get; init; } = new(Array.Empty<string>());
        public ushort Type { get; init; }
        public DnsName? Target { get; init; }
        public int Port { get; init; }
        public Dictionary<string, string>? Txt { get; init; }
        public IPAddress? Address { get; init; }
    }

    /// <summary>The parts of the DNS message format (RFC 1035 section 4, RFC 2782, RFC 6763) that discovery needs.</summary>
    internal static class DnsMessage
    {
        public const ushort TypeA = 1;
        public const ushort TypePtr = 12;
        public const ushort TypeTxt = 16;
        public const ushort TypeAaaa = 28;
        public const ushort TypeSrv = 33;
        private const ushort ClassIn = 1;
        private const int MaxPointerJumps = 64;

        /// <summary>A standard query (id 0, no flags) with the given questions, class IN, names uncompressed.</summary>
        public static byte[] Query(IReadOnlyList<(string Name, ushort Type)> questions)
        {
            if (questions.Count > ushort.MaxValue) throw new ArgumentException("Too many questions.", nameof(questions));
            var bytes = new List<byte>(512);
            void U16(int v) { bytes.Add((byte)(v >> 8)); bytes.Add((byte)v); }
            U16(0); U16(0); U16(questions.Count); U16(0); U16(0); U16(0);
            foreach ((string name, ushort type) in questions)
            {
                WriteName(bytes, name);
                U16(type);
                U16(ClassIn);
            }
            return bytes.ToArray();
        }

        /// <summary>Writes a dotted name ("\." is a dot inside a label) as length-prefixed UTF-8 labels.</summary>
        public static void WriteName(List<byte> bytes, string name)
        {
            foreach (string label in SplitLabels(name))
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(label);
                if (utf8.Length == 0 || utf8.Length > 63) throw new ArgumentException("A DNS label must be 1 to 63 bytes: " + name, nameof(name));
                bytes.Add((byte)utf8.Length);
                bytes.AddRange(utf8);
            }
            bytes.Add(0);
        }

        private static List<string> SplitLabels(string name)
        {
            var labels = new List<string>();
            var current = new StringBuilder();
            string trimmed = name.EndsWith(".", StringComparison.Ordinal) && !name.EndsWith("\\.", StringComparison.Ordinal) ? name[..^1] : name;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '.') { current.Append('.'); i++; }
                else if (c == '.') { labels.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            if (current.Length > 0) labels.Add(current.ToString());
            return labels;
        }

        /// <summary>Every answer, authority and additional record of a response. Throws FormatException on malformed data.</summary>
        public static List<DnsRecord> ParseRecords(ReadOnlySpan<byte> m)
        {
            if (m.Length < 12) throw new FormatException("A DNS message has a 12-byte header.");
            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(m.Slice(2));
            if ((flags & 0x8000) == 0) return new List<DnsRecord>(); // a query, not a response
            int qd = BinaryPrimitives.ReadUInt16BigEndian(m.Slice(4));
            int records = BinaryPrimitives.ReadUInt16BigEndian(m.Slice(6)) + BinaryPrimitives.ReadUInt16BigEndian(m.Slice(8))
                          + BinaryPrimitives.ReadUInt16BigEndian(m.Slice(10));
            int pos = 12;
            for (int i = 0; i < qd; i++)
            {
                ReadName(m, ref pos);
                Need(m, pos, 4);
                pos += 4;
            }
            var list = new List<DnsRecord>();
            for (int i = 0; i < records; i++)
            {
                DnsName name = ReadName(m, ref pos);
                Need(m, pos, 10);
                ushort type = BinaryPrimitives.ReadUInt16BigEndian(m.Slice(pos));
                int rdLength = BinaryPrimitives.ReadUInt16BigEndian(m.Slice(pos + 8));
                pos += 10;
                Need(m, pos, rdLength);
                int rdStart = pos;
                int rdEnd = pos + rdLength;
                DnsRecord? record = type switch
                {
                    TypePtr => new DnsRecord { Name = name, Type = type, Target = ReadNameAt(m, rdStart) },
                    TypeSrv when rdLength >= 7 => new DnsRecord
                    {
                        Name = name, Type = type,
                        Port = BinaryPrimitives.ReadUInt16BigEndian(m.Slice(rdStart + 4)),
                        Target = ReadNameAt(m, rdStart + 6)
                    },
                    TypeTxt => new DnsRecord { Name = name, Type = type, Txt = ReadTxt(m.Slice(rdStart, rdLength)) },
                    TypeA when rdLength == 4 => new DnsRecord { Name = name, Type = type, Address = new IPAddress(m.Slice(rdStart, 4)) },
                    TypeAaaa when rdLength == 16 => new DnsRecord { Name = name, Type = type, Address = new IPAddress(m.Slice(rdStart, 16)) },
                    _ => null
                };
                if (record is not null) list.Add(record);
                pos = rdEnd;
            }
            return list;
        }

        private static DnsName ReadNameAt(ReadOnlySpan<byte> m, int pos) => ReadName(m, ref pos);

        /// <summary>Reads a possibly compressed name (RFC 1035 section 4.1.4); a pointer loop or an overlong name is malformed.</summary>
        private static DnsName ReadName(ReadOnlySpan<byte> m, ref int pos)
        {
            var labels = new List<string>();
            int cursor = pos;
            int jumps = 0;
            int total = 0;
            bool jumped = false;
            while (true)
            {
                Need(m, cursor, 1);
                byte len = m[cursor];
                if (len == 0)
                {
                    cursor++;
                    break;
                }
                if ((len & 0xC0) == 0xC0)
                {
                    Need(m, cursor, 2);
                    int target = ((len & 0x3F) << 8) | m[cursor + 1];
                    if (++jumps > MaxPointerJumps) throw new FormatException("DNS name compression loop.");
                    if (!jumped) { pos = cursor + 2; jumped = true; }
                    cursor = target;
                    continue;
                }
                if ((len & 0xC0) != 0) throw new FormatException("Unknown DNS label type.");
                Need(m, cursor + 1, len);
                total += len + 1;
                if (total > 255) throw new FormatException("DNS name longer than 255 bytes.");
                labels.Add(Encoding.UTF8.GetString(m.Slice(cursor + 1, len)));
                cursor += 1 + len;
            }
            if (!jumped) pos = cursor;
            return new DnsName(labels);
        }

        /// <summary>TXT strings "key=value" (RFC 6763 section 6); keys in lower case; the first value of a key wins.</summary>
        private static Dictionary<string, string> ReadTxt(ReadOnlySpan<byte> data)
        {
            var txt = new Dictionary<string, string>(StringComparer.Ordinal);
            int pos = 0;
            while (pos < data.Length)
            {
                int len = data[pos++];
                if (pos + len > data.Length) throw new FormatException("TXT string runs past its record.");
                string s = Encoding.UTF8.GetString(data.Slice(pos, len));
                pos += len;
                if (s.Length == 0) continue;
                int eq = s.IndexOf('=');
                string key = (eq < 0 ? s : s[..eq]).ToLowerInvariant();
                if (key.Length == 0) continue;
                txt.TryAdd(key, eq < 0 ? string.Empty : s[(eq + 1)..]);
            }
            return txt;
        }

        private static void Need(ReadOnlySpan<byte> m, int pos, int count)
        {
            if (pos < 0 || count < 0 || pos + count > m.Length) throw new FormatException("DNS message is truncated.");
        }
    }
}
