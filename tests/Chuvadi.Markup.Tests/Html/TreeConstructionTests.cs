using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chuvadi.Markup.Tests.Html
{
    /// <summary>The html5lib tree-construction suite (both scripting modes where a test does not pin one).</summary>
    public class TreeConstructionTests
    {
        public static IEnumerable<object[]> Files() =>
            Directory.GetFiles(SpecFile.ConformancePath("html5lib", "tree-construction"), "*.dat")
                .Select(f => new object[] { Path.GetFileName(f) });

        internal static List<string> RunFile(string name)
        {
            var failures = new List<string>();
            foreach (var test in TreeDump.Load(SpecFile.ConformancePath("html5lib", "tree-construction", name)))
            {
                var modes = test.Scripting is bool b ? new[] { b } : new[] { true, false };
                foreach (bool scripting in modes)
                {
                    string actual;
                    try
                    {
                        actual = TreeDump.Run(test, scripting);
                    }
                    catch (Exception ex)
                    {
                        actual = "EXCEPTION " + ex;
                    }
                    if (actual != test.Expected)
                        failures.Add(test + (scripting ? " [script-on]" : " [script-off]") + "\n#data\n" + test.Data
                            + (test.Fragment is null ? "" : "\n#fragment " + test.Fragment) + "\n#expected\n" + test.Expected + "\n#actual\n" + actual);
                }
            }
            return failures;
        }

        [Theory]
        [MemberData(nameof(Files))]
        public void File(string name)
        {
            var failures = RunFile(name);
            Assert.True(failures.Count == 0, failures.Count + " failures:\n\n" + string.Join("\n\n", failures.Take(5)));
        }
    }
}
