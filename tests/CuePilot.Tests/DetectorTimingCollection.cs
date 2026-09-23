namespace CuePilot.Tests;

// These suites enforce detector wall-clock budgets, or drive the observer engine
// on the real sample clock. Running them alongside unrelated replay/image suites
// measures test-runner contention instead. Only join this collection when a test
// reads an actual clock; suites that inject their own timestamps stay parallel.
[CollectionDefinition("Detector timing", DisableParallelization = true)]
public sealed class DetectorTimingCollection;
