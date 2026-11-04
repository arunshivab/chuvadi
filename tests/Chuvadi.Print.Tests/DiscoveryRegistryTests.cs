using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.ManualTests;

namespace Chuvadi.Print.Tests;

/// <summary>Builds DNS responses by hand (RFC 1035 wire format), with optional name compression.</summary>
internal sealed class DnsResponseBuilder
{
    private readonly List<byte> _b = new();
    private readonly List<byte[]> _records = new();
    private int _count;

    public int Offset => 12 + _records.Sum(r => r.Length);

    public static byte[] Name(string dotted)
    {
        var b = new List<byte>();
        foreach (string label in dotted.Split('.'))
        {
            byte[] u = Encoding.UTF8.GetBytes(label);
            b.Add((byte)u.Length);
            b.AddRange(u);
        }
        b.Add(0);
        return b.ToArray();
    }

    public static byte[] Pointer(int offset) => new[] { (byte)(0xC0 | (offset >> 8)), (byte)offset };

    public DnsResponseBuilder Record(byte[] name, ushort type, byte[] rdata)
    {
        var r = new List<byte>(name);
        void U16(int v) { r.Add((byte)(v >> 8)); r.Add((byte)v); }
        U16(type); U16(0x8001); // class IN with the cache-flush bit
        r.AddRange(new byte[] { 0, 0, 0, 120 });
        U16(rdata.Length);
        r.AddRange(rdata);
        _records.Add(r.ToArray());
        _count++;
        return this;
    }

    public static byte[] Srv(int port, byte[] target)
    {
        var r = new List<byte> { 0, 0, 0, 0, (byte)(port >> 8), (byte)port };
        r.AddRange(target);
        return r.ToArray();
    }

    public static byte[] Txt(params string[] items)
    {
        var r = new List<byte>();
        foreach (string s in items)
        {
            byte[] u = Encoding.UTF8.GetBytes(s);
            r.Add((byte)u.Length);
            r.AddRange(u);
        }
        return r.ToArray();
    }

    public byte[] Build(ushort id = 0)
    {
        var m = new List<byte>();
        void U16(int v) { m.Add((byte)(v >> 8)); m.Add((byte)v); }
        U16(id); U16(0x8400); U16(0); U16(_count); U16(0); U16(0);
        foreach (byte[] r in _records) m.AddRange(r);
        return m.ToArray();
    }
}

public class DnsMessageTests
{
    [Fact]
    public void Query_encodes_questions_in_rfc1035_form()
    {
        byte[] q = DnsMessage.Query(new[] { ("_ipp._tcp.local", DnsMessage.TypePtr) });
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, q.Take(12).ToArray());
        Assert.Equal(DnsResponseBuilder.Name("_ipp._tcp.local"), q.Skip(12).Take(17).ToArray());
        Assert.Equal(new byte[] { 0, 12, 0, 1 }, q.Skip(29).ToArray());
        Assert.Throws<ArgumentException>(() => DnsMessage.Query(new[] { (new string('a', 64) + ".local", DnsMessage.TypeA) }));
    }

    [Fact]
    public void Records_are_parsed_with_name_compression()
    {
        var b = new DnsResponseBuilder();
        byte[] instance = DnsResponseBuilder.Name("Ward 3 Laser._ipp._tcp.local");
        int ptrNameAt = 12;
        b.Record(DnsResponseBuilder.Name("_ipp._tcp.local"), DnsMessage.TypePtr, instance);
        int instanceAt = ptrNameAt + DnsResponseBuilder.Name("_ipp._tcp.local").Length + 10; // the PTR's rdata
        b.Record(DnsResponseBuilder.Pointer(instanceAt), DnsMessage.TypeSrv, DnsResponseBuilder.Srv(631, DnsResponseBuilder.Name("ward3.local")));
        b.Record(DnsResponseBuilder.Pointer(instanceAt), DnsMessage.TypeTxt, DnsResponseBuilder.Txt("rp=ipp/print", "ty=Test Laser", "Note=Ward 3", "flag", "=skipped"));
        b.Record(DnsResponseBuilder.Name("ward3.local"), DnsMessage.TypeA, new byte[] { 10, 0, 0, 21 });

        List<DnsRecord> records = DnsMessage.ParseRecords(b.Build());
        Assert.Equal(4, records.Count);
        Assert.Equal("Ward 3 Laser", records[0].Target!.Labels[0]);
        Assert.Equal("ward 3 laser._ipp._tcp.local", records[1].Name.Key);
        Assert.Equal(631, records[1].Port);
        Assert.Equal("ward3.local", records[1].Target!.Display);
        Assert.Equal("ipp/print", records[2].Txt!["rp"]);
        Assert.Equal("Ward 3", records[2].Txt!["note"]);
        Assert.Equal(string.Empty, records[2].Txt!["flag"]);
        Assert.False(records[2].Txt!.ContainsKey(string.Empty));
        Assert.Equal(IPAddress.Parse("10.0.0.21"), records[3].Address);
    }

    [Fact]
    public void Malformed_messages_are_rejected()
    {
        Assert.Throws<FormatException>(() => DnsMessage.ParseRecords(new byte[5]));
        // A record whose name points at itself.
        var loop = new List<byte> { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xC0, 12 };
        Assert.Throws<FormatException>(() => DnsMessage.ParseRecords(loop.ToArray()));
        // Truncated record data.
        byte[] ok = new DnsResponseBuilder().Record(DnsResponseBuilder.Name("a.local"), DnsMessage.TypeA, new byte[] { 1, 2, 3, 4 }).Build();
        Assert.Throws<FormatException>(() => DnsMessage.ParseRecords(ok.AsSpan(0, ok.Length - 2)));
        // A query (QR bit clear) yields no records.
        Assert.Empty(DnsMessage.ParseRecords(DnsMessage.Query(new[] { ("x.local", DnsMessage.TypeA) })));
    }
}

public class NetworkPrinterDiscoveryTests
{
    /// <summary>
    /// A loopback stand-in for a printer's mDNS responder: answers the first query with the PTR record only, and a
    /// later query (the follow-up for SRV, TXT and the address) with the rest, so the follow-up round is exercised.
    /// </summary>
    private sealed class FakeResponder : IDisposable
    {
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private readonly Thread _thread;
        private volatile bool _stop;

        public FakeResponder()
        {
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _socket.ReceiveTimeout = 200;
            EndPoint = (IPEndPoint)_socket.LocalEndPoint!;
            _thread = new Thread(Serve) { IsBackground = true };
            _thread.Start();
        }

        public IPEndPoint EndPoint { get; }
        public List<List<(string Name, ushort Type)>> Queries { get; } = new();

        private void Serve()
        {
            var buffer = new byte[2048];
            while (!_stop)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int read;
                try { read = _socket.ReceiveFrom(buffer, ref from); }
                catch (SocketException) { continue; }
                var questions = ReadQuestions(buffer.AsSpan(0, read));
                lock (Queries) Queries.Add(questions);
                byte[] reply = questions.Any(q => q.Type == DnsMessage.TypePtr) ? FirstAnswer() : FollowUpAnswer();
                _socket.SendTo(reply, from);
            }
        }

        private static List<(string Name, ushort Type)> ReadQuestions(ReadOnlySpan<byte> m)
        {
            var list = new List<(string Name, ushort Type)>();
            int qd = BinaryPrimitives.ReadUInt16BigEndian(m.Slice(4));
            int pos = 12;
            for (int i = 0; i < qd; i++)
            {
                var labels = new List<string>();
                while (m[pos] != 0) { labels.Add(Encoding.UTF8.GetString(m.Slice(pos + 1, m[pos]))); pos += m[pos] + 1; }
                pos++;
                list.Add((string.Join(".", labels), BinaryPrimitives.ReadUInt16BigEndian(m.Slice(pos))));
                pos += 4;
            }
            return list;
        }

        private static byte[] FirstAnswer() => new DnsResponseBuilder()
            .Record(DnsResponseBuilder.Name("_ipp._tcp.local"), DnsMessage.TypePtr, DnsResponseBuilder.Name("Ward 3 Laser._ipp._tcp.local"))
            .Record(DnsResponseBuilder.Name("_pdl-datastream._tcp.local"), DnsMessage.TypePtr, DnsResponseBuilder.Name("Ward 3 Laser._pdl-datastream._tcp.local"))
            .Record(DnsResponseBuilder.Name("_pdl-datastream._tcp.local"), DnsMessage.TypePtr, DnsResponseBuilder.Name("Front Desk._pdl-datastream._tcp.local"))
            // Answers for a type nobody asked for are ignored.
            .Record(DnsResponseBuilder.Name("_http._tcp.local"), DnsMessage.TypePtr, DnsResponseBuilder.Name("Router._http._tcp.local"))
            .Build();

        private static byte[] FollowUpAnswer() => new DnsResponseBuilder()
            .Record(DnsResponseBuilder.Name("Ward 3 Laser._ipp._tcp.local"), DnsMessage.TypeSrv, DnsResponseBuilder.Srv(631, DnsResponseBuilder.Name("ward3.local")))
            .Record(DnsResponseBuilder.Name("Ward 3 Laser._ipp._tcp.local"), DnsMessage.TypeTxt, DnsResponseBuilder.Txt("rp=ipp/print", "ty=Test Laser", "note=Ward 3"))
            .Record(DnsResponseBuilder.Name("Ward 3 Laser._pdl-datastream._tcp.local"), DnsMessage.TypeSrv, DnsResponseBuilder.Srv(9100, DnsResponseBuilder.Name("ward3.local")))
            .Record(DnsResponseBuilder.Name("ward3.local"), DnsMessage.TypeA, new byte[] { 10, 0, 0, 21 })
            .Build();

        public void Dispose()
        {
            _stop = true;
            _thread.Join(TimeSpan.FromSeconds(2));
            _socket.Dispose();
        }
    }

    [Fact]
    public void Printers_are_found_with_a_follow_up_for_missing_records()
    {
        using var responder = new FakeResponder();
        var options = new NetworkPrinterDiscoveryOptions { Duration = TimeSpan.FromSeconds(1) };
        IReadOnlyList<DiscoveredPrinter> found = NetworkPrinterDiscovery.BrowseFrom(options, new[] { IPAddress.Loopback }, responder.EndPoint, CancellationToken.None);

        Assert.Equal(3, found.Count);
        DiscoveredPrinter front = found[0];
        Assert.Equal("Front Desk", front.InstanceName);
        Assert.Null(front.Uri);                                 // never said its port
        Assert.Equal(new[] { IPAddress.Loopback }, front.Addresses); // falls back to the responder

        DiscoveredPrinter ipp = found.Single(p => p.ServiceType == "_ipp._tcp");
        Assert.Equal("Ward 3 Laser", ipp.InstanceName);
        Assert.Equal("ward3.local", ipp.HostName);
        Assert.Equal(631, ipp.Port);
        Assert.Equal("ipp://10.0.0.21:631/ipp/print", ipp.Uri);
        Assert.Equal("Test Laser", ipp.MakeAndModel);
        Assert.Equal("Ward 3", ipp.Location);

        DiscoveredPrinter raw = found.Single(p => p.ServiceType == "_pdl-datastream._tcp" && p.InstanceName == "Ward 3 Laser");
        Assert.Equal("socket://10.0.0.21:9100", raw.Uri);

        lock (responder.Queries)
        {
            Assert.True(responder.Queries.Count >= 2, "a follow-up query was sent");
            Assert.Equal(new[] { "_ipp._tcp.local", "_ipps._tcp.local", "_pdl-datastream._tcp.local" },
                         responder.Queries[0].Select(q => q.Name).ToArray());
            Assert.Contains(responder.Queries[1], q => q.Name == "Ward 3 Laser._ipp._tcp.local" && q.Type == DnsMessage.TypeSrv);
            Assert.Contains(responder.Queries[1], q => q.Name == "Ward 3 Laser._ipp._tcp.local" && q.Type == DnsMessage.TypeTxt);
        }

        SavedPrinter saved = ipp.ToSavedPrinter();
        Assert.Equal(("Ward 3 Laser", PrinterConnection.Ipp, "ipp://10.0.0.21:631/ipp/print", "Ward 3", "Test Laser"),
                     (saved.Name, saved.Connection, saved.Address, saved.Location, saved.Notes));
        Assert.Equal(PrinterConnection.RawSocket, raw.ToSavedPrinter("Ward 3 raw").Connection);
        Assert.Throws<InvalidOperationException>(() => front.ToSavedPrinter());
    }

    [Fact]
    public void Nothing_answers_within_the_duration()
    {
        int port;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            port = ((IPEndPoint)probe.LocalEndPoint!).Port;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var found = NetworkPrinterDiscovery.BrowseFrom(new NetworkPrinterDiscoveryOptions { Duration = TimeSpan.FromMilliseconds(400) },
                                                       new[] { IPAddress.Loopback }, new IPEndPoint(IPAddress.Loopback, port), CancellationToken.None);
        Assert.Empty(found);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
        Assert.Empty(NetworkPrinterDiscovery.BrowseFrom(new NetworkPrinterDiscoveryOptions(), Array.Empty<IPAddress>(),
                                                        new IPEndPoint(IPAddress.Loopback, port), CancellationToken.None));
    }

    [Fact]
    public void Options_are_checked_and_cancellation_is_honoured()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkPrinterDiscoveryOptions { Duration = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkPrinterDiscoveryOptions { Duration = TimeSpan.FromMinutes(2) });
        using var responder = new FakeResponder();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => NetworkPrinterDiscovery.BrowseFrom(new NetworkPrinterDiscoveryOptions(),
            new[] { IPAddress.Loopback }, responder.EndPoint, cts.Token));
    }

    [Fact]
    public void Browsing_the_real_network_never_throws()
    {
        // On a build machine this usually finds nothing; it must still return within the duration.
        var found = NetworkPrinterDiscovery.Browse(new NetworkPrinterDiscoveryOptions { Duration = TimeSpan.FromMilliseconds(300) });
        Assert.NotNull(found);
    }
}

public class CupsQueueTests
{
    [Fact]
    public void Cups_get_printers_lists_every_queue()
    {
        using var server = new FakeIppPrinter();
        server.CupsQueues.Add(Queue("ward3", "ipp://localhost:631/printers/ward3", true, "Ward 3 Laser", "application/pdf", "image/pwg-raster"));
        server.CupsQueues.Add(Queue("labels", "ipp://localhost:631/printers/labels", false, null, "application/vnd.cups-raw"));

        IReadOnlyList<CupsQueue> queues = CupsQueues.List(server.Uri);
        Assert.Equal(new[] { "ward3", "labels" }, queues.Select(q => q.Name).ToArray());
        Assert.Equal("ipp://localhost:631/printers/ward3", queues[0].Uri);
        Assert.Equal("Ward 3 Laser", queues[0].Info);
        Assert.True(queues[0].IsAcceptingJobs);
        Assert.False(queues[1].IsAcceptingJobs);
        Assert.Equal(IppPrinterState.Idle, queues[0].State);
        Assert.Equal(new[] { "application/pdf", "image/pwg-raster" }, queues[0].DocumentFormats);

        var (request, _) = server.Requests.Single(r => r.Request.Operation == IppOperation.CupsGetPrinters);
        Assert.Equal(0x4002, (int)request.Code);
        Assert.Contains(request.Group(IppTag.OperationAttributes)!["requested-attributes"]!.Values, v => v.AsString() == "printer-uri-supported");

        using var client = new IppClient(server.Uri);
        client.CupsGetPrinters(new[] { "printer-name" }, limit: 1);
        IppMessage limited = server.Requests.Last().Request;
        Assert.Equal(1, limited.Group(IppTag.OperationAttributes)!["limit"]!.Value.AsInteger());
        Assert.Throws<ArgumentOutOfRangeException>(() => client.CupsGetPrinters(null, 0));
    }

    [Fact]
    public void A_server_without_queues_gives_an_empty_list()
    {
        using var server = new FakeIppPrinter();
        Assert.Empty(CupsQueues.List(server.Uri));
    }

    private static IppAttributeGroup Queue(string name, string uri, bool accepting, string? info, params string[] formats)
    {
        var g = new IppAttributeGroup(IppTag.PrinterAttributes);
        g.Add("printer-name", IppValue.Name(name));
        g.Add("printer-uri-supported", IppValue.Uri(uri));
        if (info is not null) g.Add("printer-info", IppValue.Text(info));
        g.Add("printer-state", IppValue.Enum((int)IppPrinterState.Idle));
        g.Add("printer-is-accepting-jobs", IppValue.Boolean(accepting));
        g.Add("printer-is-shared", IppValue.Boolean(false));
        g.Add("document-format-supported", formats.Select(IppValue.MimeMediaType).ToArray());
        return g;
    }
}

public class PrinterRegistryTests
{
    private static readonly PageDimensions A4 = PageDimensions.FromPaper(PaperSize.A4);

    [Fact]
    public void Registry_round_trips_through_a_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "chuvadi-registry-" + Guid.NewGuid().ToString("N"), "printers.json");
        try
        {
            var registry = new PrinterRegistry();
            registry.Set(new SavedPrinter
            {
                Name = "Ward 3 Laser", Connection = PrinterConnection.Ipp, Address = "ipps://10.0.0.21/ipp/print",
                PinnedCertificate = new string('A', 64), ForcedRoute = PrintRouteKind.Raster, Location = "Ward 3",
                Capabilities = new byte[] { 1, 2, 3 }, CapabilitiesReadAt = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero)
            });
            registry.Set(new SavedPrinter { Name = "Labels", Connection = PrinterConnection.RawSocket, Address = "socket://10.0.0.30" });
            registry.SaveTo(path);

            string json = File.ReadAllText(path);
            Assert.Contains("\"version\": 1", json);
            Assert.Contains("\"connection\": \"RawSocket\"", json);
            Assert.Contains("\"forcedRoute\": \"Raster\"", json);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));

            PrinterRegistry loaded = PrinterRegistry.Load(path);
            Assert.Equal(new[] { "Labels", "Ward 3 Laser" }, loaded.Printers.Select(p => p.Name).ToArray());
            Assert.True(loaded.TryGet("ward 3 laser", out SavedPrinter? ward));
            Assert.Equal(PrintRouteKind.Raster, ward!.ForcedRoute);
            Assert.Equal(new byte[] { 1, 2, 3 }, ward.Capabilities);
            Assert.Equal(TimeSpan.FromHours(1), ward.CapabilitiesAge(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero)));
            Assert.Null(loaded.Printers[0].CapabilitiesAge(DateTimeOffset.UtcNow));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Entries_are_copies_and_names_are_unique_ignoring_case()
    {
        var registry = new PrinterRegistry();
        var p = new SavedPrinter { Name = "Front", Connection = PrinterConnection.WindowsQueue, Address = "Front Desk" };
        registry.Set(p);
        p.Address = "changed";
        Assert.True(registry.TryGet("FRONT", out SavedPrinter? stored));
        Assert.Equal("Front Desk", stored!.Address);
        stored.Address = "also changed";
        Assert.Equal("Front Desk", registry.Printers[0].Address);

        registry.Set(new SavedPrinter { Name = "FRONT", Connection = PrinterConnection.WindowsQueue, Address = "Front Desk 2" });
        Assert.Equal(1, registry.Count);
        Assert.True(registry.Remove("front"));
        Assert.Equal(0, registry.Count);
        Assert.False(registry.Remove("front"));
    }

    [Fact]
    public void Invalid_entries_and_files_are_rejected()
    {
        var registry = new PrinterRegistry();
        Assert.Throws<ArgumentException>(() => registry.Set(new SavedPrinter { Name = " ", Address = "x" }));
        Assert.Throws<ArgumentException>(() => registry.Set(new SavedPrinter { Name = "n", Address = "" }));
        Assert.Throws<ArgumentException>(() => registry.Set(new SavedPrinter { Name = "n", Address = "x", Connection = (PrinterConnection)9 }));
        Assert.Throws<ArgumentException>(() => registry.Set(new SavedPrinter { Name = "n", Address = "x", Capabilities = new byte[1] }));

        Assert.Throws<InvalidDataException>(() => PrinterRegistry.Read(new MemoryStream(Encoding.UTF8.GetBytes("not json"))));
        Assert.Throws<InvalidDataException>(() => PrinterRegistry.Read(new MemoryStream(Encoding.UTF8.GetBytes("{\"version\": 2, \"printers\": []}"))));
        Assert.Throws<InvalidDataException>(() => PrinterRegistry.Read(new MemoryStream(Encoding.UTF8.GetBytes("{\"version\": 1, \"printers\": [{\"name\": \"\", \"address\": \"x\"}]}"))));
        Assert.Equal(0, PrinterRegistry.Load(Path.Combine(Path.GetTempPath(), "chuvadi-no-such-" + Guid.NewGuid().ToString("N") + ".json")).Count);
    }

    [Fact]
    public void Capabilities_are_saved_with_the_printer_and_reused_by_its_router()
    {
        using var printer = new FakeIppPrinter();
        var clock = new ManualTime();
        var cache = new IppCapabilityCache(null, clock);
        using (var client = new IppClient(printer.Uri)) cache.Get(client);
        var saved = new SavedPrinter { Name = "Ward 3", Connection = PrinterConnection.Ipp, Address = printer.Uri, ForcedRoute = PrintRouteKind.Raster };
        Assert.True(cache.StoreIn(saved));
        Assert.Equal(clock.Now, saved.CapabilitiesReadAt);

        var registry = new PrinterRegistry();
        registry.Set(saved);
        var stream = new MemoryStream();
        registry.WriteTo(stream);
        stream.Position = 0;
        Assert.True(PrinterRegistry.Read(stream).TryGet("Ward 3", out SavedPrinter? back));

        // A new cache loaded from the saved entry does not ask the printer while the entry is fresh.
        int reads = printer.Requests.Count(r => r.Request.Operation == IppOperation.GetPrinterAttributes);
        var fresh = new IppCapabilityCache(null, clock);
        using PrintRouter router = IppRoutes.CreateRouter(back!, null, fresh);
        Assert.Equal("Ward 3", router.Name);
        Assert.Equal(PrintRouteKind.Raster, router.ForcedRoute);
        PrintRouteDecision decision = router.Choose(new PrintJob(FakeDocument.Uniform(1, A4)).WithRasterizer(new FakeRasterizer()).WithFontsEmbedded());
        Assert.Equal(PrintRouteKind.Raster, decision.Route!.Kind);
        Assert.Equal(reads, printer.Requests.Count(r => r.Request.Operation == IppOperation.GetPrinterAttributes));

        // Stale saved capabilities are read again.
        clock.Now += TimeSpan.FromMinutes(11);
        router.Choose(new PrintJob(FakeDocument.Uniform(1, A4)).WithRasterizer(new FakeRasterizer()));
        Assert.Equal(reads + 1, printer.Requests.Count(r => r.Request.Operation == IppOperation.GetPrinterAttributes));

        Assert.False(new IppCapabilityCache().StoreIn(new SavedPrinter { Name = "x", Address = "ipp://nowhere/ipp/print" }));
        Assert.False(fresh.LoadFrom(new SavedPrinter { Name = "x", Address = "a", Capabilities = new byte[] { 9 }, CapabilitiesReadAt = clock.Now }));
    }

    [Fact]
    public void Saved_printer_pins_its_certificate_and_only_ipp_or_cups_make_ipp_routers()
    {
        string pin = new string('B', 64);
        using PrintRouter router = IppRoutes.CreateRouter(new SavedPrinter { Name = "C", Connection = PrinterConnection.Cups, Address = "ipps://printserver/printers/c", PinnedCertificate = pin });
        Assert.Equal(PrintRouteKind.Cups, router.Routes[0].Kind);
        var pdf = (IppPdfRoute)router.Routes[0];
        Assert.Equal(pin, pdf.Target.Client.Options.PinnedCertificates.Single());

        var options = new IppClientOptions();
        using (IppRoutes.CreateRouter(new SavedPrinter { Name = "C", Connection = PrinterConnection.Ipp, Address = "ipps://p/ipp/print", PinnedCertificate = pin }, options)) { }
        Assert.Empty(options.PinnedCertificates); // the caller's options are not changed

        Assert.Throws<ArgumentException>(() => IppRoutes.CreateRouter(new SavedPrinter { Name = "R", Connection = PrinterConnection.RawSocket, Address = "socket://p" }));
    }
}
