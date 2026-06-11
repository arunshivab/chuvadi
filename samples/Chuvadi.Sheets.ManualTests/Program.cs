using System;
using System.IO;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Entry point for manual tests. Dispatches to individual test classes based on command-line
/// arguments. With no arguments, runs all tests in sequence.
///
/// Usage:
///   dotnet run                          # run all tests
///   dotnet run -- minimal               # run only MinimalXlsxTest
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "output");
        Directory.CreateDirectory(outputDir);

        Console.WriteLine($"Chuvadi.Sheets manual tests");
        Console.WriteLine($"Output directory: {outputDir}");
        Console.WriteLine(new string('-', 70));
        Console.WriteLine();

        var testName = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

        try
        {
            if (testName is "all" or "minimal")
                MinimalXlsxTest.Run(outputDir);

            if (testName is "all" or "styled")
                StyledXlsxTest.Run(outputDir);

            if (testName is "all" or "streaming")
                StreamingWriterTest.Run(outputDir);

            if (testName is "all" or "polish")
                FeaturesPolishTest.Run(outputDir);

            if (testName is "all" or "ergonomic")
                ErgonomicExportTest.Run(outputDir);

            if (testName is "all" or "reader")
                ReaderTest.Run(outputDir);

            if (testName is "all" or "zip")
                ZipExtensionTest.Run(outputDir);

            if (testName is "all" or "protection")
                ProtectionTest.Run(outputDir);

            if (testName is "all" or "hardening")
                HardeningTest.Run(outputDir);

            Console.WriteLine();
            Console.WriteLine(new string('-', 70));
            Console.WriteLine("All tests completed successfully.");
            Console.WriteLine($"Inspect generated files in: {outputDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(new string('-', 70));
            Console.WriteLine($"TEST FAILED: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
