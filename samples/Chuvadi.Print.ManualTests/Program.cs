using System;

namespace Chuvadi.Print.ManualTests
{
    public static class Program
    {
        public static int Main()
        {
            int passed = 0, failed = 0;
            foreach (var test in VerificationGroups.All())
            {
                try
                {
                    test();
                    Console.WriteLine("PASS  " + test.Method.Name);
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL  " + test.Method.Name + " -> " + ex.Message);
                    failed++;
                }
            }
            Console.WriteLine();
            Console.WriteLine("Total: " + (passed + failed) + "  Passed: " + passed + "  Failed: " + failed);
            return failed == 0 ? 0 : 1;
        }
    }
}
