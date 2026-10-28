using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Zip.ManualTests;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    /// <summary>Runs each end-to-end verification group (shared with the console runner) as its own xUnit case.</summary>
    public class VerificationGroupTests
    {
        public static IEnumerable<object[]> Groups() => VerificationGroups.All().Select(a => new object[] { a.Method.Name });

        [Theory]
        [MemberData(nameof(Groups))]
        public void Group_passes(string name)
        {
            var action = VerificationGroups.All().Single(a => a.Method.Name == name);
            action();
        }
    }
}
