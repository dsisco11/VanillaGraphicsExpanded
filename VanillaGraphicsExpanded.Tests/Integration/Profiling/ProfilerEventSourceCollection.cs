using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Profiling;

/// <summary>Serializes tests that enable or inspect the process-wide profiling EventSource.</summary>
[CollectionDefinition("Profiler EventSource", DisableParallelization = true)]
public sealed class ProfilerEventSourceCollection
{
}
