global using Xunit;
global using FluentAssertions;

// ActivityListener is process-global. When test classes in this assembly run
// in parallel, listeners attached by one class capture activities emitted by
// another class's saga harness — making the Activities tests order/race
// dependent. Serialise tests within this assembly only. Other assemblies are
// unaffected.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
