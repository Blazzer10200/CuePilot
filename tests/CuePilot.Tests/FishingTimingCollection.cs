namespace CuePilot.Tests;

// These suites enforce detector wall-clock budgets. Running them alongside
// unrelated replay/image suites measures test-runner contention instead.
[CollectionDefinition("Fishing timing", DisableParallelization = true)]
public sealed class FishingTimingCollection;
