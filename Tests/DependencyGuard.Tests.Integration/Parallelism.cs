using NUnit.Framework;

// Every test works in a folder of its own and starts processes of its own (a build, or the CLI, which waits for
// MSBuild), so they run side by side, each with a fixture instance of its own.
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
