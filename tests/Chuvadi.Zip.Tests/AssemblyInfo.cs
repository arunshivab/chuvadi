using Xunit;

// The bounded-memory checks measure process-wide heap growth, so tests must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
