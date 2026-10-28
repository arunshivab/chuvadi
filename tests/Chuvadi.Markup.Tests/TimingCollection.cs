namespace Chuvadi.Markup.Tests
{
    /// <summary>
    /// Tests that assert wall-clock budgets run in this collection, which xUnit runs on its own after the
    /// parallel tests, so CPU contention from other tests cannot make them flaky.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class TimingCollection
    {
        public const string Name = "Timing";
    }
}
