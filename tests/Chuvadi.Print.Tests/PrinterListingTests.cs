using System.Net;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;
using Chuvadi.Print.ManualTests;

namespace Chuvadi.Print.Tests;

/// <summary>
/// Printer lists for local and remote CUPS servers (D-326 P3 option B): the CupsQueue type, uuid and device URI, the
/// configured CUPS server, and one merged list with every printer once.
/// </summary>
public class PrinterListingTests
{
    private const string PrinterUuid = "6a3f1b2c-0d4e-4f5a-9b8c-7d6e5f4a3b2c";

    private static IppAttributeGroup Queue(string name, string uri, string? uuid = null, string? deviceUri = null, int? type = null,
                                           string? location = null, string? model = null)
    {
        var g = new IppAttributeGroup(IppTag.PrinterAttributes);
        g.Add("printer-name", IppValue.Name(name));
        g.Add("printer-uri-supported", IppValue.Uri(uri));
        g.Add("printer-state", IppValue.Enum((int)IppPrinterState.Idle));
        g.Add("printer-is-accepting-jobs", IppValue.Boolean(true));
        if (uuid is not null) g.Add("printer-uuid", IppValue.Uri(uuid));
        if (deviceUri is not null) g.Add("device-uri", IppValue.Uri(deviceUri));
        if (type.HasValue) g.Add("printer-type", IppValue.Enum(type.Value));
        if (location is not null) g.Add("printer-location", IppValue.Text(location));
        if (model is not null) g.Add("printer-make-and-model", IppValue.Text(model));
        return g;
    }

    private static CupsQueue Q(string name, string uri, string? uuid = null, string? deviceUri = null, int? type = null,
                               string? location = null, string? model = null)
        => new(Queue(name, uri, uuid, deviceUri, type, location, model));

    private static DiscoveredPrinter Found(string instance, string type, string address, int port, string? rp = null,
                                           string? uuid = null, string? model = null)
    {
        var txt = new Dictionary<string, string>();
        if (rp is not null) txt["rp"] = rp;
        if (uuid is not null) txt["uuid"] = uuid;
        if (model is not null) txt["ty"] = model;
        IPAddress ip = IPAddress.Parse(address);
        return new DiscoveredPrinter(instance, type, instance.Replace(' ', '-') + ".local", port, new[] { ip }, txt, ip);
    }

    private static IReadOnlyList<ListedPrinter> Merge(IReadOnlyList<CupsQueue> local, params DiscoveredPrinter[] discovered)
        => PrinterListing.Merge(new List<(string, IReadOnlyList<CupsQueue>)> { (CupsQueues.LocalServer, local) }, discovered);

    // ------------------------------------------------------------------ CupsQueue

    [Fact]
    public void Cups_queue_reads_the_printer_type_uuid_and_device_uri_and_the_request_asks_for_them()
    {
        using var server = new FakeIppPrinter();
        server.CupsQueues.Add(Queue("local", "ipp://localhost:631/printers/local", "urn:uuid:" + PrinterUuid,
                                    "ipp://192.0.2.5/ipp/print", type: 0x900C));
        server.CupsQueues.Add(Queue("shared", "ipp://localhost:631/printers/shared", type: 0x900E));
        server.CupsQueues.Add(Queue("bare", "ipp://localhost:631/printers/bare"));

        IReadOnlyList<CupsQueue> queues = CupsQueues.List(server.Uri);
        Assert.Equal(0x900C, queues[0].PrinterType);
        Assert.False(queues[0].IsRemote);
        Assert.Equal("urn:uuid:" + PrinterUuid, queues[0].Uuid);
        Assert.Equal("ipp://192.0.2.5/ipp/print", queues[0].DeviceUri);
        Assert.True(queues[1].IsRemote);
        Assert.Null(queues[2].PrinterType);
        Assert.False(queues[2].IsRemote);
        Assert.Null(queues[2].Uuid);
        Assert.Null(queues[2].DeviceUri);

        var (request, _) = server.Requests.Single(r => r.Request.Operation == IppOperation.CupsGetPrinters);
        string[] requested = request.Group(IppTag.OperationAttributes)!["requested-attributes"]!.Values.Select(v => v.AsString()).ToArray();
        Assert.Equal(new[]
        {
            "printer-name", "printer-uri-supported", "printer-info", "printer-location", "printer-make-and-model",
            "printer-state", "printer-is-accepting-jobs", "printer-is-shared", "document-format-supported",
            "printer-type", "printer-uuid", "device-uri"
        }, requested);
    }

    // ------------------------------------------------------------------ the configured server

    [Fact]
    public void Client_conf_server_name_is_read_case_insensitively_skipping_comments_and_the_last_one_wins()
    {
        Assert.Null(CupsQueues.ServerNameIn(null));
        Assert.Null(CupsQueues.ServerNameIn("# ServerName nothing\nEncryption Required\n"));
        Assert.Equal("print-server", CupsQueues.ServerNameIn("servername print-server\n"));
        Assert.Equal("second:8631", CupsQueues.ServerNameIn("ServerName first\r\n  # ServerName commented\r\nServerName\tsecond:8631\r\n"));
    }

    [Theory]
    [InlineData("print-server", "ipp://print-server:631/")]
    [InlineData("print-server:8631", "ipp://print-server:8631/")]
    [InlineData("192.0.2.9", "ipp://192.0.2.9:631/")]
    [InlineData("[2001:db8::9]:631", "ipp://[2001:db8::9]:631/")]
    [InlineData("[2001:db8::9]", "ipp://[2001:db8::9]:631/")]
    [InlineData("2001:db8::9", "ipp://[2001:db8::9]:631/")]
    [InlineData("print-server/version=1.1", "ipp://print-server:631/")]
    [InlineData("print-server:631/version=1.1", "ipp://print-server:631/")]
    [InlineData("ipps://print-server:443/", "ipps://print-server:443/")]
    [InlineData("ipp://print-server:631", "ipp://print-server:631/")]
    [InlineData("/run/cups/cups.sock", CupsQueues.LocalServer)]
    public void A_cups_server_address_becomes_an_ipp_uri(string server, string expected)
        => Assert.Equal(expected, CupsQueues.ToServerUri(server));

    [Fact]
    public void Configured_server_comes_from_cups_server_then_the_users_then_the_systems_client_conf()
    {
        string home = Path.Combine("home", "arun");
        string userConf = Path.Combine(home, ".cups", "client.conf");
        var files = new Dictionary<string, string>
        {
            [userConf] = "ServerName user-server\n",
            ["/etc/cups/client.conf"] = "ServerName system-server:8631\n"
        };
        string? Read(string path) => files.TryGetValue(path, out string? text) ? text : null;

        Assert.Equal("ipp://env-server:631/", CupsQueues.ConfiguredServerFrom(n => n == "CUPS_SERVER" ? "env-server" : null, home, Read));
        Assert.Equal("ipp://user-server:631/", CupsQueues.ConfiguredServerFrom(_ => null, home, Read));
        Assert.Equal("ipp://user-server:631/", CupsQueues.ConfiguredServerFrom(n => n == "CUPS_SERVER" ? "  " : null, home, Read));
        files.Remove(userConf);
        Assert.Equal("ipp://system-server:8631/", CupsQueues.ConfiguredServerFrom(_ => null, home, Read));
        Assert.Equal("ipp://system-server:8631/", CupsQueues.ConfiguredServerFrom(_ => null, null, Read));
        files.Clear();
        Assert.Null(CupsQueues.ConfiguredServerFrom(_ => null, home, Read));
    }

    [Fact]
    public void Configured_server_on_this_machine_never_throws()
    {
        string? server = CupsQueues.ConfiguredServer();
        if (OperatingSystem.IsWindows()) Assert.Null(server);
        else if (server is not null) Assert.StartsWith("ipp", server);
    }

    // ------------------------------------------------------------------ merging

    [Fact]
    public void A_printer_found_by_cups_and_by_dns_sd_is_listed_once_with_the_cups_queue_first()
    {
        CupsQueue queue = Q("HPInkTank", "ipp://localhost:631/printers/HPInkTank", "urn:uuid:" + PrinterUuid, "ipp://192.0.2.5/ipp/print");
        DiscoveredPrinter ipp = Found("HP Ink Tank 410", "_ipp._tcp", "192.0.2.5", 631, "ipp/print", PrinterUuid.ToUpperInvariant(), "HP Ink Tank 410");
        DiscoveredPrinter ipps = Found("HP Ink Tank 410", "_ipps._tcp", "192.0.2.5", 443, "ipp/print", PrinterUuid);

        ListedPrinter printer = Assert.Single(Merge(new[] { queue }, ipp, ipps));
        Assert.Equal("HPInkTank", printer.Name);
        Assert.Equal("ipp://localhost:631/printers/HPInkTank", printer.Uri);
        Assert.True(printer.IsCupsQueue);
        Assert.Same(queue, Assert.Single(printer.Queues).Queue);
        Assert.Equal(CupsQueues.LocalServer, printer.Queues[0].Server);
        Assert.Equal(new[] { ipp, ipps }, printer.Discovered);
        Assert.Equal("urn:uuid:" + PrinterUuid, printer.Uuid);
        Assert.Equal("HP Ink Tank 410", printer.MakeAndModel);
    }

    [Fact]
    public void A_queue_added_by_hand_and_the_queue_cups_made_itself_are_one_printer()
    {
        // As in Arun's lp test: "HPInkTank" (added with lpadmin, device ipp://...) and the queue CUPS created from DNS-SD
        // (device dnssd://...?uuid=...). They differ in printer-uuid; the DNS-SD advertisement links them.
        CupsQueue added = Q("HPInkTank", "ipp://localhost:631/printers/HPInkTank", "urn:uuid:11111111-1111-1111-1111-111111111111",
                            "ipp://192.0.2.5/ipp/print");
        CupsQueue automatic = Q("HP_Ink_Tank_Wireless_410_series_52DE83", "ipp://localhost:631/printers/HP_Ink_Tank_Wireless_410_series_52DE83",
                                "urn:uuid:22222222-2222-2222-2222-222222222222",
                                "dnssd://HP%20Ink%20Tank%20Wireless%20410%20series%20%5B52DE83%5D._ipp._tcp.local/?uuid=" + PrinterUuid);
        DiscoveredPrinter advert = Found("HP Ink Tank Wireless 410 series [52DE83]", "_ipp._tcp", "192.0.2.5", 631, "ipp/print", PrinterUuid);

        ListedPrinter printer = Assert.Single(Merge(new[] { added, automatic }, advert));
        Assert.Equal("HPInkTank", printer.Name);
        Assert.Equal(new[] { "HPInkTank", "HP_Ink_Tank_Wireless_410_series_52DE83" }, printer.Queues.Select(q => q.Queue.Name));
        Assert.Same(advert, Assert.Single(printer.Discovered));
    }

    [Fact]
    public void Device_uri_and_advertised_uri_match_without_case_trailing_slash_or_the_default_port()
    {
        CupsQueue queue = Q("ward3", "ipp://localhost:631/printers/ward3", deviceUri: "IPP://Ward3-Laser.local/ipp/print/");
        DiscoveredPrinter advert = Found("Ward 3", "_ipp._tcp", "192.0.2.7", 631, "ipp/print");
        // The advertisement's URI uses the address; give one that uses the same host name instead.
        var byName = new DiscoveredPrinter("Ward 3", "_ipp._tcp", "ward3-laser.local", 631, Array.Empty<IPAddress>(),
                                           new Dictionary<string, string> { ["rp"] = "ipp/print" }, IPAddress.Parse("192.0.2.7"));
        Assert.Equal("ipp://ward3-laser.local:631/ipp/print", byName.Uri);

        IReadOnlyList<ListedPrinter> printers = Merge(new[] { queue }, advert, byName);
        Assert.Equal(2, printers.Count);
        Assert.Equal("ward3", printers[0].Name);
        Assert.Same(byName, Assert.Single(printers[0].Discovered));
        // The advertisement by address is a different URI and has no UUID, so it stays its own entry.
        Assert.False(printers[1].IsCupsQueue);
        Assert.Equal("Ward 3", printers[1].Name);
        Assert.Equal("ipp://192.0.2.7:631/ipp/print", printers[1].Uri);
    }

    [Fact]
    public void Unrelated_printers_stay_separate_cups_first_then_dns_sd_in_the_order_found()
    {
        CupsQueue labels = Q("labels", "ipp://localhost:631/printers/labels", "urn:uuid:aaaaaaaa-0000-0000-0000-000000000001");
        CupsQueue ward = Q("ward", "ipp://localhost:631/printers/ward", "urn:uuid:aaaaaaaa-0000-0000-0000-000000000002");
        DiscoveredPrinter raw = Found("Raw Printer", "_pdl-datastream._tcp", "192.0.2.20", 9100);
        DiscoveredPrinter other = Found("Other", "_ipp._tcp", "192.0.2.21", 631, "ipp/print", "aaaaaaaa-0000-0000-0000-000000000099");

        IReadOnlyList<ListedPrinter> printers = Merge(new[] { labels, ward }, raw, other);
        Assert.Equal(new[] { "labels", "ward", "Raw Printer", "Other" }, printers.Select(p => p.Name));
        Assert.Equal("socket://192.0.2.20:9100", printers[2].Uri);
        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000099", printers[3].Uuid);
    }

    [Fact]
    public void A_queue_on_a_remote_server_and_the_same_printer_on_the_local_server_merge_by_uuid()
    {
        CupsQueue local = Q("ward3", "ipp://localhost:631/printers/ward3", "urn:uuid:" + PrinterUuid, type: 0x900E);
        CupsQueue remote = Q("ward3", "ipp://print-server:631/printers/ward3", "urn:uuid:" + PrinterUuid.ToUpperInvariant());
        IReadOnlyList<ListedPrinter> printers = PrinterListing.Merge(
            new List<(string, IReadOnlyList<CupsQueue>)>
            {
                (CupsQueues.LocalServer, new[] { local }),
                ("ipp://print-server:631/", new[] { remote })
            },
            Array.Empty<DiscoveredPrinter>());
        ListedPrinter printer = Assert.Single(printers);
        Assert.Equal(new[] { CupsQueues.LocalServer, "ipp://print-server:631/" }, printer.Queues.Select(q => q.Server));
        Assert.Equal("ipp://localhost:631/printers/ward3", printer.Uri);
    }

    [Fact]
    public void Keys_ignore_uuid_prefix_and_case_and_uri_case_slash_and_default_ports()
    {
        Assert.Equal(PrinterListing.UuidKey("urn:uuid:" + PrinterUuid.ToUpperInvariant()), PrinterListing.UuidKey("{" + PrinterUuid + "}"));
        Assert.Null(PrinterListing.UuidKey("  "));
        Assert.Null(PrinterListing.UuidKey("urn:uuid:"));
        Assert.Equal(PrinterListing.UriKey("ipp://Host:631/ipp/print/"), PrinterListing.UriKey("ipp://host/ipp/print"));
        Assert.NotEqual(PrinterListing.UriKey("ipp://host:8631/ipp/print"), PrinterListing.UriKey("ipp://host/ipp/print"));
        Assert.Equal(PrinterListing.UriKey("socket://host:9100"), PrinterListing.UriKey("socket://HOST/"));
        Assert.Equal(PrinterUuid, PrinterListing.DnsSdUuid("dnssd://x._ipp._tcp.local/?bidi&uuid=" + PrinterUuid));
        Assert.Null(PrinterListing.DnsSdUuid("ipp://host/ipp/print?uuid=" + PrinterUuid));
        Assert.Null(PrinterListing.DnsSdUuid("dnssd://x._ipp._tcp.local/"));
    }

    // ------------------------------------------------------------------ listing servers

    [Fact]
    public void Listing_asks_each_server_once_and_reports_the_ones_that_fail()
    {
        using var first = new FakeIppPrinter();
        first.CupsQueues.Add(Queue("ward3", "ipp://localhost:631/printers/ward3", "urn:uuid:" + PrinterUuid));
        using var second = new FakeIppPrinter();
        second.CupsQueues.Add(Queue("ward3-remote", "ipp://print-server:631/printers/ward3", "urn:uuid:" + PrinterUuid));
        second.CupsQueues.Add(Queue("labels", "ipp://print-server:631/printers/labels"));
        using ReservedPort closed = LoopbackPorts.ReserveClosedTcpPort();
        string down = "ipp://127.0.0.1:" + closed.Port + "/";

        var options = new PrinterListingOptions { IncludeLocalServer = false, BrowseNetwork = false };
        options.CupsServers.Add(second.Uri);
        options.CupsServers.Add(down);
        options.CupsServers.Add(second.Uri); // listed once

        PrinterListingResult result = PrinterListing.ListFrom(options, first.Uri, CancellationToken.None);

        Assert.Equal(3, result.Servers.Count);
        Assert.Equal(new[] { "ward3", "labels" }, result.Printers.Select(p => p.Name));
        Assert.Equal(2, result.Printers[0].Queues.Count);
        PrinterListingProblem problem = Assert.Single(result.Problems);
        Assert.Equal(down, problem.Source);
        Assert.Single(second.Requests, r => r.Request.Operation == IppOperation.CupsGetPrinters);
    }

    [Fact]
    public void Listing_with_nothing_to_ask_is_empty_and_an_empty_server_is_refused()
    {
        var none = new PrinterListingOptions { IncludeLocalServer = false, IncludeConfiguredServer = false, BrowseNetwork = false };
        PrinterListingResult result = PrinterListing.List(none);
        Assert.Empty(result.Printers);
        Assert.Empty(result.Servers);
        Assert.Empty(result.Problems);

        var bad = new PrinterListingOptions { IncludeLocalServer = false, BrowseNetwork = false };
        bad.CupsServers.Add(" ");
        Assert.Throws<ArgumentException>(() => PrinterListing.ListFrom(bad, null, CancellationToken.None));
    }

    [Fact]
    public void Listing_stops_when_cancelled()
    {
        using var server = new FakeIppPrinter();
        var options = new PrinterListingOptions { IncludeLocalServer = false, BrowseNetwork = false };
        options.CupsServers.Add(server.Uri);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PrinterListing.ListFrom(options, null, cts.Token));
    }

    [Fact]
    public void Local_server_is_listed_by_default_except_on_windows()
        => Assert.Equal(!OperatingSystem.IsWindows(), new PrinterListingOptions().IncludeLocalServer);
}
