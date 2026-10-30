using System;
using System.Runtime.Versioning;
using System.Threading;

namespace ChuvadiPdfPrinter.Agent
{
    /// <summary>
    /// ChuvadiPdfAgent.exe
    ///   --service                   run as the Windows service (the installer registers this)
    ///   --console                   run in the foreground, logging to the console (Ctrl+C to stop)
    ///   --self-test [--printer "Name"] [--timeout 120]
    ///                               print a test page through the virtual printer and check the saved PDF
    ///   --show-config               print the effective configuration
    ///   --validate                  check the configuration file
    ///   --config &lt;path&gt;             configuration file (default %ProgramData%\Chuvadi\PdfPrinter\config.json)
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("ChuvadiPdfAgent runs on Windows only.");
                return 2;
            }

            string configPath = Option(args, "--config") ?? PrinterConfig.DefaultPath();
            try
            {
                if (Has(args, "--service"))
                {
                    WindowsService.Run(AgentHost.ServiceName, token => AgentHost.Run(configPath, echo: false, token));
                    return 0;
                }
                if (Has(args, "--console"))
                {
                    using var cts = new CancellationTokenSource();
                    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                    Console.WriteLine("Chuvadi PDF agent running in the console. Ctrl+C to stop.");
                    AgentHost.Run(configPath, echo: true, cts.Token);
                    return 0;
                }
                if (Has(args, "--self-test"))
                {
                    int timeout = int.TryParse(Option(args, "--timeout"), out int t) && t > 0 ? t : 120;
                    return SelfTest.Run(configPath, Option(args, "--printer"), timeout);
                }
                if (Has(args, "--show-config"))
                {
                    Console.WriteLine("# " + configPath);
                    Console.WriteLine(PrinterConfig.Load(configPath).ToJson());
                    return 0;
                }
                if (Has(args, "--validate"))
                {
                    var problems = PrinterConfig.Load(configPath).Validate();
                    foreach (string p in problems) Console.WriteLine("PROBLEM  " + p);
                    Console.WriteLine(problems.Count == 0 ? "Configuration OK: " + configPath : problems.Count + " problem(s)");
                    return problems.Count == 0 ? 0 : 1;
                }
                Console.WriteLine("Usage: ChuvadiPdfAgent --service | --console | --self-test [--printer \"Name\"] [--timeout s] | --show-config | --validate  [--config path]");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static string? Option(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
        }
    }
}
