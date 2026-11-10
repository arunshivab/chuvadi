using System.Collections.Generic;

namespace Chuvadi.Print.Tests
{
    /// <summary>
    /// JSON written by Chuvadi.Print 1.0.0-rc.2 (reflection-based System.Text.Json) for <see cref="JsonGoldenSamples"/>,
    /// captured before the switch to source generation (D-217 item 1). The source-generated serializers must write
    /// exactly these bytes and read them back.
    /// </summary>
    internal static class JsonGolden
    {
        public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
        {
            ["registry"] = Raw.Lf("""
{
  "version": 1,
  "printers": [
    {
      "name": "Labels",
      "connection": "RawSocket",
      "address": "socket://192.0.2.20"
    },
    {
      "name": "Ward 3 \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD",
      "connection": "Ipp",
      "address": "ipps://192.0.2.10/ipp/print",
      "pinnedCertificate": "AB:CD:EF",
      "forcedRoute": "Raster",
      "location": "Floor 2 \u0022east\u0022",
      "notes": "line1\nline2 \u003C\u0026\u003E",
      "capabilities": "AAEC+v8=",
      "capabilitiesReadAt": "2026-11-06T09:30:15.123+05:30"
    }
  ]
}
"""),
            ["hello-request"] = """{"machineName":"PC-1","agentVersion":"1.0.0-rc.2","printers":["A","B \u0BA4"]}""",
            ["hello-request-min"] = """{"printers":[]}""",
            ["hello-response"] = """{"protocolVersion":1,"pollWaitSeconds":25,"leaseSeconds":120,"serverTime":"2026-11-06T09:30:15.123+05:30"}""",
            ["poll-request"] = """{"waitSeconds":10}""",
            ["job-lease"] = """{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","leaseId":"11111111-2222-3333-4444-555555555555","leaseSeconds":60,"printer":"P","payloadBytes":99,"payloadSha256":"ff","title":"T","attempt":2}""",
            ["job-lease-min"] = """{"jobId":"00000000-0000-0000-0000-000000000000","leaseId":"00000000-0000-0000-0000-000000000000","leaseSeconds":0,"printer":"","payloadBytes":0,"payloadSha256":"","attempt":0}""",
            ["status-report"] = """{"leaseId":"11111111-2222-3333-4444-555555555555","state":"Printed","pagesPrinted":1,"impressionsPrinted":2,"sheetsPrinted":3,"error":"e","audit":{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","documentTitle":"Report","pageCount":7,"copies":1,"printerName":"P","machineName":"M","userName":"U","status":"Printed","startedAt":"2026-11-06T09:30:15.123+05:30","completedAt":"2026-11-06T04:00:01+00:00","tenantId":"T","route":"Raster"}}""",
            ["status-report-min"] = """{"leaseId":"00000000-0000-0000-0000-000000000000","state":"Queued","pagesPrinted":0,"impressionsPrinted":0,"sheetsPrinted":0}""",
            ["status-response"] = """{"cancelRequested":true,"final":true,"leaseSeconds":30}""",
            ["error-response"] = """{"error":"Bad \u0022thing\u0022 \u0BAE"}""",
            ["stored-job"] = """{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","agentId":"site-a","printer":"Ward 3","state":"Printing","submittedAt":"2026-11-06T09:30:15.123+05:30","title":"Discharge \u0BAE","userName":"user1","tenantId":"t1","copies":2,"payloadBytes":1234567890123,"payloadSha256":"ab12","leaseId":"11111111-2222-3333-4444-555555555555","leaseExpiresAt":"2026-11-06T04:00:01+00:00","attempts":3,"cancelRequested":true,"pagesPrinted":4,"impressionsPrinted":5,"sheetsPrinted":6,"error":"none","completedAt":"2026-11-06T04:00:01+00:00","audited":true}""",
            ["stored-job-min"] = """{"jobId":"00000000-0000-0000-0000-000000000000","agentId":"","printer":"","state":"Queued","submittedAt":"0001-01-01T00:00:00+00:00","copies":1,"payloadBytes":0,"payloadSha256":"","attempts":0,"cancelRequested":false,"pagesPrinted":0,"impressionsPrinted":0,"sheetsPrinted":0,"audited":false}""",
            ["journal-entry"] = """{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","startedAt":"2026-11-06T09:30:15.123+05:30","finishedAt":"2026-11-06T04:00:01+00:00","finalReport":{"leaseId":"11111111-2222-3333-4444-555555555555","state":"Printed","pagesPrinted":1,"impressionsPrinted":2,"sheetsPrinted":3,"error":"e","audit":{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","documentTitle":"Report","pageCount":7,"copies":1,"printerName":"P","machineName":"M","userName":"U","status":"Printed","startedAt":"2026-11-06T09:30:15.123+05:30","completedAt":"2026-11-06T04:00:01+00:00","tenantId":"T","route":"Raster"}},"reported":true}""",
            ["journal-entry-min"] = """{"jobId":"00000000-0000-0000-0000-000000000000","reported":false}""",
            ["token-records"] = """[{"agentId":"b-agent","tokenHash":"hash2","issuedAt":"2026-11-06T04:00:01+00:00"},{"agentId":"a-agent","tokenHash":"hash1","issuedAt":"2026-11-06T09:30:15.123+05:30"}]""",
            ["file-job"] = """{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","agentId":"site-a","printer":"Ward 3","state":"Printing","submittedAt":"2026-11-06T09:30:15.123+05:30","title":"Discharge \u0BAE","userName":"user1","tenantId":"t1","copies":2,"payloadBytes":1234567890123,"payloadSha256":"ab12","leaseId":"11111111-2222-3333-4444-555555555555","leaseExpiresAt":"2026-11-06T04:00:01+00:00","attempts":3,"cancelRequested":true,"pagesPrinted":4,"impressionsPrinted":5,"sheetsPrinted":6,"error":"none","completedAt":"2026-11-06T04:00:01+00:00","audited":true}""",
            ["file-journal"] = """{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","startedAt":"2026-11-06T09:30:15.123+05:30","finishedAt":"2026-11-06T04:00:01+00:00","finalReport":{"leaseId":"11111111-2222-3333-4444-555555555555","state":"Printed","pagesPrinted":1,"impressionsPrinted":2,"sheetsPrinted":3,"error":"e","audit":{"jobId":"0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0","documentTitle":"Report","pageCount":7,"copies":1,"printerName":"P","machineName":"M","userName":"U","status":"Printed","startedAt":"2026-11-06T09:30:15.123+05:30","completedAt":"2026-11-06T04:00:01+00:00","tenantId":"T","route":"Raster"}},"reported":true}""",
            ["file-tokens"] = """[{"agentId":"a-agent","tokenHash":"hash1","issuedAt":"2026-11-06T09:30:15.123+05:30"},{"agentId":"b-agent","tokenHash":"hash2","issuedAt":"2026-11-06T04:00:01+00:00"}]""",
        };
    }

    internal static class Raw
    {
        /// <summary>Raw string literals take the source file's line endings; JSON was written with "\n".</summary>
        public static string Lf(string s) => s.Replace("\r\n", "\n");
    }
}
