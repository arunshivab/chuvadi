using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Print;

namespace ChuvadiPdfPrinter.Agent
{
    /// <summary>Wires configuration, logging, audit, the pipe capture and the outbox collector together.</summary>
    [SupportedOSPlatform("windows")]
    internal static class AgentHost
    {
        public const string ServiceName = "ChuvadiPdfAgent";

        public static void Run(string configPath, bool echo, CancellationToken cancellationToken)
        {
            PrinterConfig config = PrinterConfig.Load(configPath);
            var log = new FileLog(config.ResolveDiagnosticLog()) { Echo = echo };
            IReadOnlyList<string> problems = config.Validate();
            if (problems.Count > 0)
            {
                foreach (string p in problems) log.Error("Configuration: " + p);
                throw new InvalidDataException("Invalid configuration: " + string.Join(" ", problems));
            }

            string auditPath = config.ResolveAuditLog();
            string? auditFolder = Path.GetDirectoryName(auditPath);
            if (!string.IsNullOrEmpty(auditFolder)) Directory.CreateDirectory(auditFolder);
            var engine = new AgentEngine(config, new FileAuditSink(auditPath), log)
            {
                UserFolderCreated = AgentSecurity.MakePrivate
            };

            log.Info("Chuvadi PDF agent starting: queue '" + config.QueueName + "', pipe \\\\.\\pipe\\" + config.PipeName
                     + ", output " + Environment.ExpandEnvironmentVariables(config.OutputFolder));

            var capture = new QueueCaptureService(engine, new SpoolerJobProvider(), () => AgentSecurity.CreatePipe(config.PipeName, 4));
            string usersRoot = UsersRoot();
            var outbox = new OutboxCollector(engine, () =>
            {
                IEnumerable<string> folders = config.ExtraOutboxFolders.Select(Environment.ExpandEnvironmentVariables);
                if (!string.IsNullOrWhiteSpace(config.PsaPackageFamilyName))
                    folders = folders.Concat(OutboxCollector.PsaOutboxes(usersRoot, config.PsaPackageFamilyName));
                return folders;
            });

            Task.WaitAll(new[]
            {
                capture.RunAsync(cancellationToken),
                outbox.RunAsync(TimeSpan.FromMilliseconds(config.OutboxScanMilliseconds), cancellationToken)
            }, CancellationToken.None);
            log.Info("Chuvadi PDF agent stopped.");
        }

        /// <summary>The folder holding user profiles (normally C:\Users), from %PUBLIC%'s parent.</summary>
        private static string UsersRoot()
        {
            string? publicFolder = Environment.GetEnvironmentVariable("PUBLIC");
            string? parent = string.IsNullOrEmpty(publicFolder) ? null : Path.GetDirectoryName(publicFolder);
            return parent ?? Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Users");
        }
    }
}
