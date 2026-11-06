using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chuvadi.Print;
using Chuvadi.Print.Remote;

namespace Chuvadi.Print.Tests
{
    /// <summary>
    /// D-217 item 1: the source-generated serializers write exactly what 1.0.0-rc.2's reflection-based serializer wrote
    /// (<see cref="JsonGolden"/>), and read files and messages written by rc.2, including case-insensitive names and
    /// numeric enums.
    /// </summary>
    public class JsonCompatibilityTests
    {
        /// <summary>The golden text with this machine's line ending (indented JSON uses Environment.NewLine).</summary>
        private static string Golden(string name) => JsonGolden.Files[name].Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        /// <summary>The options rc.2 used for Remote, kept here to cross-check with the reflection serializer.</summary>
        private static readonly JsonSerializerOptions Rc2Remote = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string Utf8(byte[] b) => Encoding.UTF8.GetString(b);

        [Fact]
        public void RegistryIsWrittenAsRc2WroteIt()
        {
            var ms = new MemoryStream();
            JsonGoldenSamples.Registry().WriteTo(ms);
            Assert.Equal(Golden("registry"), Utf8(ms.ToArray()));
        }

        [Fact]
        public void RegistryWrittenByRc2ReadsBack()
        {
            PrinterRegistry reg = PrinterRegistry.Read(new MemoryStream(Encoding.UTF8.GetBytes(Golden("registry"))));
            Assert.Equal(2, reg.Count);
            Assert.True(reg.TryGet("Ward 3 தமிழ்", out SavedPrinter? p));
            Assert.Equal(PrintRouteKind.Raster, p!.ForcedRoute);
            Assert.Equal(new byte[] { 0, 1, 2, 250, 255 }, p.Capabilities);
            var ms = new MemoryStream();
            reg.WriteTo(ms);
            Assert.Equal(Golden("registry"), Utf8(ms.ToArray()));
        }

        [Fact]
        public void RegistryReadsNumericEnumsAndIgnoresUnknownProperties()
        {
            string json = "{\"version\":1,\"extra\":true,\"printers\":[{\"name\":\"N\",\"connection\":2,\"address\":\"socket://h\",\"forcedRoute\":5,\"future\":[1]}]}";
            PrinterRegistry reg = PrinterRegistry.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            Assert.True(reg.TryGet("N", out SavedPrinter? p));
            Assert.Equal(PrinterConnection.RawSocket, p!.Connection);
            Assert.Equal((PrintRouteKind)5, p.ForcedRoute);
        }

        [Fact]
        public void RemoteMessagesAreWrittenAsRc2WroteThem()
        {
            foreach ((string name, object value, Type type) in JsonGoldenSamples.RemoteValues())
            {
                byte[] actual = JsonSerializer.SerializeToUtf8Bytes(value, RemoteProtocol.Json.GetTypeInfo(type)!);
                Assert.True(Golden(name) == Utf8(actual), name + ": " + Utf8(actual));
                Assert.Equal(Utf8(JsonSerializer.SerializeToUtf8Bytes(value, type, Rc2Remote)), Utf8(actual));
            }
        }

        [Fact]
        public void RemoteMessagesWrittenByRc2ReadBack()
        {
            foreach ((string name, object _, Type type) in JsonGoldenSamples.RemoteValues())
            {
                var info = RemoteProtocol.Json.GetTypeInfo(type)!;
                object? read = JsonSerializer.Deserialize(Encoding.UTF8.GetBytes(Golden(name)), info);
                Assert.NotNull(read);
                Assert.True(Golden(name) == Utf8(JsonSerializer.SerializeToUtf8Bytes(read, info)), name);
            }
        }

        [Fact]
        public void RemoteReadingIgnoresCaseAcceptsNumericEnumsAndUnknownProperties()
        {
            PollRequest? poll = JsonSerializer.Deserialize(Encoding.UTF8.GetBytes("{\"WAITSECONDS\":7,\"x\":1}"), RemoteProtocol.Json.PollRequest);
            Assert.Equal(7, poll!.WaitSeconds);
            StatusReport? report = JsonSerializer.Deserialize(Encoding.UTF8.GetBytes("{\"State\":4,\"audit\":{\"status\":\"Failed\"}}"), RemoteProtocol.Json.StatusReport);
            Assert.Equal(HubJobState.Printed, report!.State);
            Assert.Equal(PrintJobStatus.Failed, report.Audit!.Status);
        }

        [Fact]
        public void FileStoresWriteAsRc2Wrote()
        {
            string root = Path.Combine(Path.GetTempPath(), "chuvadi-json-" + Guid.NewGuid().ToString("N"));
            try
            {
                var jobs = new FileJobStore(Path.Combine(root, "jobs"));
                jobs.Add(JsonGoldenSamples.FullJob(), new byte[] { 1, 2, 3 });
                string jobFile = Directory.GetFiles(Path.Combine(root, "jobs"), "*.json", SearchOption.AllDirectories).Single();
                Assert.Equal(Golden("file-job"), File.ReadAllText(jobFile));
                Assert.Equal(JsonGoldenSamples.FullJob().Title, jobs.Get(JsonGoldenSamples.FullJob().JobId)!.Title);

                string tokenPath = Path.Combine(root, "tokens.json");
                var tokens = new FileAgentTokenStore(tokenPath);
                foreach (AgentTokenRecord r in JsonGoldenSamples.Tokens()) tokens.Set(r);
                Assert.Equal(Golden("file-tokens"), File.ReadAllText(tokenPath));
                Assert.Equal("hash1", new FileAgentTokenStore(tokenPath).Get("a-agent")!.TokenHash);

                var journal = new AgentJournal(Path.Combine(root, "journal"));
                journal.Save(JsonGoldenSamples.FullJournal());
                string journalFile = Directory.GetFiles(Path.Combine(root, "journal")).Single();
                Assert.Equal(Golden("file-journal"), File.ReadAllText(journalFile));
                Assert.True(journal.Get(JsonGoldenSamples.FullJournal().JobId)!.Reported);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
