using Xunit;

// MSBuildLocator and MSBuildWorkspace are process-wide, one test writes to samples/Orders and puts it
// back, and every workspace load is expensive. Running the collections one at a time is both safer and,
// in practice, no slower.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
