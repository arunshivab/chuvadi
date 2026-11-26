using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Text;
using Chuvadi.Print;
using Chuvadi.Print.Ipp;

namespace Chuvadi.Print.Tests;

/// <summary>
/// rc.6 security limits (D-406, D-410): S1 IPP collection nesting, S2 the IPP response size cap, S3 spool lengths
/// checked before allocating, S4 page ranges kept as intervals. The reproducers are the rc.5 probes
/// (sec09-ipp-nested-20000.ipp, p03-spool-14byte.cvpr, p03b-spool-range.cvpr), rebuilt here byte for byte.
/// </summary>
public class SecurityLimitsTests
{
    // ------------------------------------------------------------------ S1: IPP nesting (SEC-09)

    /// <summary>An IPP response whose printer attribute holds <paramref name="depth"/> nested collections.</summary>
    internal static byte[] NestedResponse(int depth)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x04 });
        void Attr(byte tag, string name, byte[] value)
        {
            byte[] n = Encoding.ASCII.GetBytes(name);
            ms.WriteByte(tag); ms.WriteByte((byte)(n.Length >> 8)); ms.WriteByte((byte)n.Length); ms.Write(n);
            ms.WriteByte((byte)(value.Length >> 8)); ms.WriteByte((byte)value.Length); ms.Write(value);
        }
        Attr(0x34, "media-col-default", Array.Empty<byte>());
        for (int i = 1; i < depth; i++) { Attr(0x4A, "", "m"u8.ToArray()); Attr(0x34, "", Array.Empty<byte>()); }
        for (int i = 0; i < depth; i++) Attr(0x37, "", Array.Empty<byte>());
        ms.WriteByte(0x03);
        return ms.ToArray();
    }

    private static IppMessage NestedMessage(int depth)
    {
        var inner = new IppCollection().Add("x", IppValue.Integer(1));
        IppValue value = IppValue.Collection(inner);
        for (int i = 1; i < depth; i++) value = IppValue.Collection(new IppCollection().Add("m", value));
        var message = new IppMessage((short)IppStatusCode.SuccessfulOk, 1);
        message.AddGroup(IppTag.PrinterAttributes).Add("media-col-default", value);
        return message;
    }

    private static int Depth(IppValue value)
    {
        int depth = 0;
        while (value.Value is IppCollection c)
        {
            depth++;
            if (c.Members.Count == 0 || c.Members[0].Values.Count == 0) break;
            value = c.Members[0].Values[0];
        }
        return depth;
    }

    [Fact]
    public void Collections_32_deep_are_read()
    {
        IppMessage m = IppMessage.Parse(NestedResponse(32));
        Assert.Equal(32, Depth(m.Group(IppTag.PrinterAttributes)!["media-col-default"]!.Value));
    }

    [Fact]
    public void Collections_33_deep_are_refused()
    {
        var ex = Assert.Throws<IppFormatException>(() => IppMessage.Parse(NestedResponse(33)));
        Assert.Equal("IPP collections are nested more than 32 deep.", ex.Message);
    }

    [Fact]
    public void The_rc5_probe_of_20000_nested_collections_is_refused_without_a_crash()
    {
        byte[] probe = NestedResponse(20000);
        Assert.Equal(320021, probe.Length);   // the size of sec09-ipp-nested-20000.ipp
        var ex = Assert.Throws<IppFormatException>(() => IppMessage.Parse(probe));
        Assert.Equal("IPP collections are nested more than 32 deep.", ex.Message);
        Assert.Throws<IppFormatException>(() => IppMessage.ReadFrom(new MemoryStream(probe), out _));
    }

    [Fact]
    public void Writing_allows_32_levels_and_refuses_33()
    {
        byte[] bytes = NestedMessage(32).ToArray();
        Assert.Equal(32, Depth(IppMessage.Parse(bytes).Group(IppTag.PrinterAttributes)!["media-col-default"]!.Value));
        var ex = Assert.Throws<InvalidOperationException>(() => NestedMessage(33).ToArray());
        Assert.Equal("IPP collections are nested more than 32 deep.", ex.Message);
    }

    [Fact]
    public void A_collection_that_contains_itself_is_refused_when_written()
    {
        var loop = new IppCollection();
        loop.Add("self", IppValue.Collection(loop));
        var message = new IppMessage((short)IppOperation.PrintJob, 1);
        message.AddGroup(IppTag.JobAttributes).Add("media-col", IppValue.Collection(loop));
        Assert.Throws<InvalidOperationException>(() => message.ToArray());
    }

    [Fact]
    public void Saved_capabilities_nested_too_deep_are_ignored_not_crashed_on()
    {
        var printer = new SavedPrinter
        {
            Name = "x", Address = "ipp://127.0.0.1/ipp/print", Capabilities = NestedResponse(20000), CapabilitiesReadAt = DateTimeOffset.UtcNow
        };
        Assert.False(new IppCapabilityCache().LoadFrom(printer));
    }

    // ------------------------------------------------------------------ P-04: an unknown group tag

    [Fact]
    public void An_unknown_attribute_group_tag_is_an_ipp_format_error()
    {
        // Audit B P-04: a delimiter byte that is no group tag gave ArgumentOutOfRangeException.
        byte[] reply = { 2, 0, 0, 0, 0, 0, 0, 1, 0x01, 0x00, 0x03 };
        var ex = Assert.Throws<IppFormatException>(() => IppMessage.Parse(reply));
        Assert.Equal("Unknown IPP attribute-group tag 0x00.", ex.Message);

        // The reserved delimiters 0x0B to 0x0F are read as groups, as before.
        IppMessage reserved = IppMessage.Parse(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x0B, 0x03 });
        Assert.Equal((IppTag)0x0B, Assert.Single(reserved.Groups).Tag);
    }

    [Fact]
    public void A_printer_reply_with_an_unknown_group_tag_is_an_invalid_response()
    {
        byte[] reply = { 2, 0, 0, 0, 0, 0, 0, 1, 0x00, 0x03 };
        using IppClient client = Client(new CannedPrinter(() => new ByteArrayContent(reply)));
        var ex = Assert.Throws<IppException>(() => client.GetPrinterAttributes());
        Assert.Equal("The printer sent an invalid IPP response: Unknown IPP attribute-group tag 0x00.", ex.Message);

        // The fuzz shape that found it: one byte of a valid reply changed. Only IPP errors come out.
        byte[] good = NestedMessage(3).ToArray();
        for (int i = 0; i < good.Length; i++)
        foreach (byte b in new byte[] { 0x00, 0x03, 0x0F, 0x10, 0x34, 0x37, 0x4A, 0xFF })
        {
            byte[] bad = (byte[])good.Clone();
            bad[i] = b;
            try { IppMessage.Parse(bad); }
            catch (IppFormatException) { }
        }
    }

    // ------------------------------------------------------------------ S2: response size, ReadFrom with a limit

    [Fact]
    public void ReadFrom_with_a_limit_reads_up_to_it_and_refuses_past_it()
    {
        byte[] message = NestedMessage(2).ToArray();
        byte[] withDocument = message.Concat(new byte[] { 9, 8, 7 }).ToArray();

        IppMessage read = IppMessage.ReadFrom(new MemoryStream(withDocument), withDocument.Length, out byte[] trailing);
        Assert.Equal((short)IppStatusCode.SuccessfulOk, read.Code);
        Assert.Equal(new byte[] { 9, 8, 7 }, trailing);

        var ex = Assert.Throws<IppFormatException>(() => IppMessage.ReadFrom(new MemoryStream(withDocument), withDocument.Length - 1, out _));
        Assert.Equal("The IPP message is longer than " + (withDocument.Length - 1) + " bytes.", ex.Message);

        Assert.Throws<ArgumentOutOfRangeException>(() => IppMessage.ReadFrom(new MemoryStream(message), 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => IppMessage.ReadFrom(new MemoryStream(message), (long)Array.MaxLength + 1, out _));
        Assert.Throws<ArgumentNullException>(() => IppMessage.ReadFrom(null!, 10, out _));
    }

    [Fact]
    public void ReadFrom_without_a_limit_is_unchanged()
    {
        byte[] message = NestedMessage(2).ToArray();
        IppMessage read = IppMessage.ReadFrom(new MemoryStream(message.Concat(new byte[] { 1 }).ToArray()), out byte[] trailing);
        Assert.Equal(1, read.RequestId);
        Assert.Equal(new byte[] { 1 }, trailing);
    }

    [Fact]
    public void MaxResponseBytes_defaults_to_16_MB_and_is_checked()
    {
        var options = new IppClientOptions();
        Assert.Equal(16L * 1024 * 1024, options.MaxResponseBytes);
        options.MaxResponseBytes = 1;
        options.MaxResponseBytes = Array.MaxLength;
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxResponseBytes = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxResponseBytes = (long)Array.MaxLength + 1);
    }

    /// <summary>Answers every request with fixed bytes, with or without a Content-Length.</summary>
    private sealed class CannedPrinter(Func<HttpContent> content) : HttpMessageHandler
    {
        public int Requests;

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content() };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }

    /// <summary>Content with no length, read as a stream (a chunked response).</summary>
    private sealed class StreamedContent(Func<Stream> open) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => open().CopyToAsync(stream);
        protected override Stream CreateContentReadStream(CancellationToken cancellationToken) => open();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(open());
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    /// <summary>A stream that never ends: zeros, forever.</summary>
    private sealed class EndlessStream : Stream
    {
        public long Served;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); Served += count; return count; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A stream that sends a few bytes and then stalls until it is cancelled.</summary>
    private sealed class StallingStream : Stream
    {
        private bool _sent;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent) { _sent = true; buffer.Span[0] = 2; return 1; }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static IppClient Client(HttpMessageHandler handler, long? max = null, TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        var options = new IppClientOptions();
        if (max is long m) options.MaxResponseBytes = m;
        return new IppClient("ipp://printer.test/ipp/print", http, options);
    }

    [Fact]
    public void A_response_within_the_limit_is_read()
    {
        byte[] reply = NestedMessage(2).ToArray();
        using IppClient client = Client(new CannedPrinter(() => new ByteArrayContent(reply)), reply.Length);
        Assert.True(client.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print")).IsSuccess);

        using IppClient streamed = Client(new CannedPrinter(() => new StreamedContent(() => new MemoryStream(reply))), reply.Length);
        Assert.True(streamed.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print")).IsSuccess);
    }

    [Fact]
    public void A_response_with_a_declared_length_over_the_limit_is_refused_before_reading()
    {
        byte[] reply = NestedMessage(2).ToArray();
        using IppClient client = Client(new CannedPrinter(() => new ByteArrayContent(reply)), reply.Length - 1);
        var ex = Assert.Throws<IppException>(() => client.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print")));
        Assert.Equal("The printer's response is larger than " + (reply.Length - 1) + " bytes (IppClientOptions.MaxResponseBytes).", ex.Message);
    }

    [Fact]
    public void A_declared_length_over_the_limit_reads_none_of_the_body()
    {
        var endless = new EndlessStream();
        using IppClient client = Client(new CannedPrinter(() =>
        {
            var content = new StreamedContent(() => endless);
            content.Headers.ContentLength = 1L << 40;
            return content;
        }), 1_000_000);
        var ex = Assert.Throws<IppException>(() => client.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print")));
        Assert.Contains("larger than 1000000 bytes", ex.Message);
        Assert.Equal(0, endless.Served);
    }

    [Fact]
    public void Routers_for_saved_printers_keep_the_response_limit()
    {
        var options = new IppClientOptions { MaxResponseBytes = 12345 };
        var copy = (IppClientOptions)typeof(IppRoutes).GetMethod("Copy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { options })!;
        Assert.Equal(12345, copy.MaxResponseBytes);
    }

    [Fact]
    public void An_endless_response_is_cut_off_at_the_limit()
    {
        var endless = new EndlessStream();
        using IppClient client = Client(new CannedPrinter(() => new StreamedContent(() => endless)), 1_000_000);
        var ex = Assert.Throws<IppException>(() => client.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print")));
        Assert.Equal("The printer's response is larger than 1000000 bytes (IppClientOptions.MaxResponseBytes).", ex.Message);
        Assert.InRange(endless.Served, 1_000_001, 1_000_000 + 81920);
    }

    [Fact]
    public void The_default_limit_stops_an_endless_response_at_16_MB()
    {
        var endless = new EndlessStream();
        using IppClient client = Client(new CannedPrinter(() => new StreamedContent(() => endless)));
        var ex = Assert.Throws<IppException>(() => client.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print")));
        Assert.Contains("larger than 16777216 bytes", ex.Message);
        Assert.InRange(endless.Served, 16L * 1024 * 1024 + 1, 16L * 1024 * 1024 + 81920);
    }

    [Fact]
    public void A_printer_reply_nested_too_deep_is_an_invalid_response()
    {
        byte[] probe = NestedResponse(20000);
        using IppClient client = Client(new CannedPrinter(() => new ByteArrayContent(probe)));
        var ex = Assert.Throws<IppException>(() => client.GetPrinterAttributes());
        Assert.Equal("The printer sent an invalid IPP response: IPP collections are nested more than 32 deep.", ex.Message);
    }

    [Fact]
    public void The_client_timeout_still_covers_a_response_body_that_stalls()
    {
        using IppClient client = Client(new CannedPrinter(() => new StreamedContent(() => new StallingStream())), timeout: TimeSpan.FromSeconds(1));
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<IppException>(() => client.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print")));
        Assert.StartsWith("Timed out waiting for ", ex.Message);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Cancelling_a_stalled_response_body_is_reported_as_cancellation()
    {
        using IppClient client = Client(new CannedPrinter(() => new StreamedContent(() => new StallingStream())));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        Assert.ThrowsAny<OperationCanceledException>(() =>
            client.SendRaw(IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://printer.test/ipp/print"), cancellationToken: cts.Token));
    }

    // ------------------------------------------------------------------ S3: spool lengths (P-03)

    /// <summary>The 14-byte rc.5 probe: a v4 header declaring a body of Array.MaxLength bytes, and no body.</summary>
    internal static byte[] FourteenBytes()
    {
        var spool = new byte[14];
        "CVPR"u8.CopyTo(spool);
        spool[4] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(spool.AsSpan(10), Array.MaxLength);
        return spool;
    }

    /// <summary>A stream that can't seek, over the given bytes.</summary>
    private sealed class OneWayStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static long Allocated(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void The_14_byte_probe_is_refused_without_allocating_its_declared_length()
    {
        byte[] probe = FourteenBytes();
        string path = Path.Combine(Path.GetTempPath(), "chuvadi-p03-" + Guid.NewGuid().ToString("N") + ".cvpr");
        File.WriteAllBytes(path, probe);
        try
        {
            foreach (Action load in new Action[]
                     {
                         () => PrintJob.LoadSpool(probe),
                         () => PrintJob.LoadSpool(new MemoryStream(probe)),
                         () => PrintJob.LoadSpool(new OneWayStream(probe)),
                         () => PrintJob.LoadSpool(path),
                     })
            {
                long allocated = Allocated(() =>
                {
                    var ex = Assert.Throws<InvalidDataException>(load);
                    Assert.Equal("Truncated spool envelope.", ex.Message);
                });
                Assert.True(allocated < 4 * 1024 * 1024, "allocated " + allocated + " bytes");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Envelope(byte version, byte[] body)
    {
        var ms = new MemoryStream();
        ms.Write("CVPR"u8);
        ms.WriteByte(version);
        ms.WriteByte(0);
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(four, Crc32.Compute(body)); ms.Write(four);
        BinaryPrimitives.WriteInt32LittleEndian(four, body.Length); ms.Write(four);
        ms.Write(body);
        return ms.ToArray();
    }

    /// <summary>The body of a valid v1 envelope with the given page spec, with the PDF length set as given.</summary>
    private static byte[] V1Body(string pages, long pdfLength, byte[] pdf)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1); w.Write(false);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
            w.Write(100.0);
            w.Write(595.0); w.Write(842.0); w.Write(false);
            w.Write(0.0); w.Write(0.0); w.Write(0.0); w.Write(0.0);
            w.Write((byte)1); w.Write((byte)1);
            w.Write(false);
            w.Write(pages);
            w.Write(pdfLength);
            w.Write(pdf);
        }
        return ms.ToArray();
    }

    [Fact]
    public void The_hand_built_body_is_a_valid_v1_envelope()
    {
        // Guards V1Body: with a true PDF length it loads, so the tests below fail only on the part they change.
        PrintJob job = PrintJob.LoadSpool(Envelope(1, V1Body("range:2-3", 3, new byte[] { 1, 2, 3 })));
        Assert.Equal(new byte[] { 1, 2, 3 }, job.PdfBytes);
        Assert.Equal(new[] { 2, 3 }, job.Settings.Pages.Resolve(5));
        Assert.Equal(new PaperSize(595, 842), job.Settings.Paper);
    }

    [Fact]
    public void A_pdf_length_past_the_body_is_refused_before_allocating_it()
    {
        // A valid checksum: the CRC doesn't protect against a crafted length.
        byte[] envelope = Envelope(1, V1Body("all", int.MaxValue, new byte[] { 1, 2, 3 }));
        long allocated = Allocated(() =>
        {
            var ex = Assert.Throws<InvalidDataException>(() => PrintJob.LoadSpool(envelope));
            Assert.Equal("Truncated PDF payload.", ex.Message);
        });
        Assert.True(allocated < 4 * 1024 * 1024, "allocated " + allocated + " bytes");
    }

    [Theory]
    [InlineData("range:5-1")]
    [InlineData("range:0")]
    [InlineData("current:0")]
    [InlineData("first:x")]
    [InlineData("list:1,99999999999")]
    [InlineData("sideways")]
    public void A_corrupt_page_selection_is_one_corrupt_body_error(string pages)
    {
        byte[] envelope = Envelope(1, V1Body(pages, 3, new byte[] { 1, 2, 3 }));
        var ex = Assert.Throws<InvalidDataException>(() => PrintJob.LoadSpool(envelope));
        Assert.Equal("Corrupt spool envelope body.", ex.Message);
    }

    [Fact]
    public void A_valid_envelope_loads_the_same_from_every_kind_of_source()
    {
        byte[] pdf = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
        var job = new PrintJob(pdf).WithPages(PageSelection.Range("2-4,9")).WithCopies(3).WithTitle("t");
        byte[] spool = job.ToSpoolArray();
        foreach (PrintJob loaded in new[]
                 {
                     PrintJob.LoadSpool(spool), PrintJob.LoadSpool(new MemoryStream(spool)), PrintJob.LoadSpool(new OneWayStream(spool))
                 })
        {
            Assert.Equal(pdf, loaded.PdfBytes);
            Assert.Equal("range:2-4,9", loaded.Settings.Pages.ToCanonicalString());
            Assert.Equal(3, loaded.Settings.Copies);
            Assert.Equal("t", loaded.DocumentTitle);
            Assert.Equal(spool, loaded.ToSpoolArray());
        }
    }

    [Fact]
    public void A_one_way_stream_shorter_than_its_header_says_is_truncated()
    {
        byte[] spool = new PrintJob(new byte[] { 1, 2, 3 }).ToSpoolArray();
        var ex = Assert.Throws<InvalidDataException>(() => PrintJob.LoadSpool(new OneWayStream(spool.AsSpan(0, spool.Length - 1).ToArray())));
        Assert.Equal("Truncated spool envelope.", ex.Message);
    }

    // ------------------------------------------------------------------ S4: page ranges (P-03b)

    [Fact]
    public void The_rc5_range_probe_loads_without_expanding()
    {
        // p03b-spool-range.cvpr: a valid spool whose page selection is range:1-2147483647.
        byte[] envelope = Envelope(1, V1Body("range:1-2147483647", 3, new byte[] { 1, 2, 3 }));
        var watch = Stopwatch.StartNew();
        long allocated = Allocated(() =>
        {
            PrintJob job = PrintJob.LoadSpool(envelope);
            Assert.Equal(new[] { 1, 2, 3 }, job.Settings.Pages.Resolve(3));
        });
        Assert.True(allocated < 1024 * 1024, "allocated " + allocated + " bytes");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_huge_range_costs_nothing_until_resolved_against_the_document()
    {
        long allocated = Allocated(() =>
        {
            PageSelection all = PageSelection.Range("1-2147483647");
            Assert.Equal("range:1-2147483647", all.ToCanonicalString());
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, all.Resolve(5));
            Assert.Empty(all.Resolve(0));
        });
        Assert.True(allocated < 1024 * 1024, "allocated " + allocated + " bytes");

        // Resolving is cut to the document before it loops: ten full-size intervals take no time.
        PageSelection many = PageSelection.Range(string.Join(",", Enumerable.Repeat("1-2147483647", 10)));
        var watch = Stopwatch.StartNew();
        Assert.Equal(new[] { 1, 2, 3 }, many.Resolve(3));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), "took " + watch.Elapsed);
    }

    [Fact]
    public void Ranges_resolve_as_before()
    {
        PageSelection p = PageSelection.Range(" 7, 2-4, 3 , ,9-9 ");
        Assert.Equal(new[] { 2, 3, 4, 7, 9 }, p.Resolve(10));
        Assert.Equal(new[] { 2, 3 }, p.Resolve(3));
        Assert.Equal("range: 7, 2-4, 3 , ,9-9 ", p.ToCanonicalString());
        Assert.Equal(new[] { 2147483646, 2147483647 }, PageSelection.Range("2147483646-2147483647").Resolve(int.MaxValue));
    }

    [Theory]
    [InlineData("5-1", "Range end before start: 5-1")]
    [InlineData("0", "Invalid page number: '0'")]
    [InlineData("1-x", "Invalid page number: 'x'")]
    [InlineData("2147483648", "Invalid page number: '2147483648'")]
    public void Bad_ranges_fail_with_the_same_messages(string spec, string message)
    {
        var ex = Assert.Throws<FormatException>(() => PageSelection.Range(spec));
        Assert.Equal(message, ex.Message);
    }
}
