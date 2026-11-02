using System;
using System.Collections.Generic;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static partial IEnumerable<Action> Later()
        {
            foreach (Action a in L2Groups())
            {
                yield return a;
            }
            foreach (Action a in L3Groups())
            {
                yield return a;
            }
            foreach (Action a in L4Groups())
            {
                yield return a;
            }
            foreach (Action a in L5Groups())
            {
                yield return a;
            }
            foreach (Action a in L6Groups())
            {
                yield return a;
            }
            foreach (Action a in L7Groups())
            {
                yield return a;
            }
            foreach (Action a in L8Groups())
            {
                yield return a;
            }
            foreach (Action a in L11Groups())
            {
                yield return a;
            }
        }
    }
}
