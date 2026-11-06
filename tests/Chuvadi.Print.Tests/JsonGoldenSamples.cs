using System;
using System.Collections.Generic;
using Chuvadi.Print;
using Chuvadi.Print.Remote;

namespace Chuvadi.Print.Tests
{
    /// <summary>
    /// Fixed sample values for the JSON compatibility tests (D-217 item 1): every property set, with non-ASCII text,
    /// nulls, every enum kind and time zones, plus minimal values. The golden JSON in JsonCompatibilityTests was
    /// captured from these with the reflection-based serializer of 1.0.0-rc.2.
    /// </summary>
    internal static class JsonGoldenSamples
    {
        private static readonly DateTimeOffset T1 = new(2026, 11, 6, 9, 30, 15, 123, TimeSpan.FromHours(5.5));
        private static readonly DateTimeOffset T2 = new(2026, 11, 6, 4, 0, 1, TimeSpan.Zero);
        private static readonly Guid G1 = new("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
        private static readonly Guid G2 = new("11111111-2222-3333-4444-555555555555");

        public static PrinterRegistry Registry()
        {
            var reg = new PrinterRegistry();
            reg.Set(new SavedPrinter
            {
                Name = "Ward 3 தமிழ்",
                Connection = PrinterConnection.Ipp,
                Address = "ipps://192.0.2.10/ipp/print",
                PinnedCertificate = "AB:CD:EF",
                ForcedRoute = PrintRouteKind.Raster,
                Location = "Floor 2 \"east\"",
                Notes = "line1\nline2 <&>",
                Capabilities = new byte[] { 0, 1, 2, 250, 255 },
                CapabilitiesReadAt = T1
            });
            reg.Set(new SavedPrinter { Name = "Labels", Connection = PrinterConnection.RawSocket, Address = "socket://192.0.2.20" });
            return reg;
        }

        public static StoredJob FullJob() => new()
        {
            JobId = G1,
            AgentId = "site-a",
            Printer = "Ward 3",
            State = HubJobState.Printing,
            SubmittedAt = T1,
            Title = "Discharge ம",
            UserName = "user1",
            TenantId = "t1",
            Copies = 2,
            PayloadBytes = 1234567890123,
            PayloadSha256 = "ab12",
            LeaseId = G2,
            LeaseExpiresAt = T2,
            Attempts = 3,
            CancelRequested = true,
            PagesPrinted = 4,
            ImpressionsPrinted = 5,
            SheetsPrinted = 6,
            Error = "none",
            CompletedAt = T2,
            Audited = true
        };

        public static AuditRecord FullAudit() => new()
        {
            JobId = G1,
            DocumentTitle = "Report",
            PageCount = 7,
            Copies = 1,
            PrinterName = "P",
            MachineName = "M",
            UserName = "U",
            Status = PrintJobStatus.Printed,
            StartedAt = T1,
            CompletedAt = T2,
            TenantId = "T",
            Error = null,
            Route = "Raster"
        };

        public static AgentJournalEntry FullJournal() => new()
        {
            JobId = G1,
            StartedAt = T1,
            FinishedAt = T2,
            FinalReport = new StatusReport { LeaseId = G2, State = HubJobState.Printed, PagesPrinted = 1, ImpressionsPrinted = 2, SheetsPrinted = 3, Error = "e", Audit = FullAudit() },
            Reported = true
        };

        public static List<AgentTokenRecord> Tokens() => new()
        {
            new AgentTokenRecord { AgentId = "b-agent", TokenHash = "hash2", IssuedAt = T2 },
            new AgentTokenRecord { AgentId = "a-agent", TokenHash = "hash1", IssuedAt = T1 }
        };

        /// <summary>Name, value and declared type of every Remote JSON root, full and minimal.</summary>
        public static IEnumerable<(string Name, object Value, Type Type)> RemoteValues()
        {
            yield return ("hello-request", new HelloRequest { MachineName = "PC-1", AgentVersion = "1.0.0-rc.2", Printers = new List<string> { "A", "B த" } }, typeof(HelloRequest));
            yield return ("hello-request-min", new HelloRequest(), typeof(HelloRequest));
            yield return ("hello-response", new HelloResponse { PollWaitSeconds = 25, LeaseSeconds = 120, ServerTime = T1 }, typeof(HelloResponse));
            yield return ("poll-request", new PollRequest { WaitSeconds = 10 }, typeof(PollRequest));
            yield return ("job-lease", new JobLease { JobId = G1, LeaseId = G2, LeaseSeconds = 60, Printer = "P", PayloadBytes = 99, PayloadSha256 = "ff", Title = "T", Attempt = 2 }, typeof(JobLease));
            yield return ("job-lease-min", new JobLease(), typeof(JobLease));
            yield return ("status-report", FullJournal().FinalReport!, typeof(StatusReport));
            yield return ("status-report-min", new StatusReport(), typeof(StatusReport));
            yield return ("status-response", new StatusResponse { CancelRequested = true, Final = true, LeaseSeconds = 30 }, typeof(StatusResponse));
            yield return ("error-response", new ErrorResponse { Error = "Bad \"thing\" ம" }, typeof(ErrorResponse));
            yield return ("stored-job", FullJob(), typeof(StoredJob));
            yield return ("stored-job-min", new StoredJob(), typeof(StoredJob));
            yield return ("journal-entry", FullJournal(), typeof(AgentJournalEntry));
            yield return ("journal-entry-min", new AgentJournalEntry(), typeof(AgentJournalEntry));
            yield return ("token-records", Tokens(), typeof(List<AgentTokenRecord>));
        }
    }
}
