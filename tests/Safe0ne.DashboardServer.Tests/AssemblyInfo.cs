using Xunit;

// Tests mutate environment variables (token TTL) for deterministic coverage.
// Avoid parallel execution to prevent cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
