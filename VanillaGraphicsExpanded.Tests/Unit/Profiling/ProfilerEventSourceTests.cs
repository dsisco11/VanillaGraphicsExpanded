using VanillaGraphicsExpanded.Profiling;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Profiling;

/// <summary>Checks the profiler's disabled-scope fast path without enabling an event provider.</summary>
[Collection("Profiler EventSource")]
public sealed class ProfilerEventSourceTests
{
    #region Public API
    /// <summary>Beginning and disposing a scope while profiling is disabled completes without throwing.</summary>
    [Fact]
    public void BeginScope_ReturnsNoop_WhenDisabled()
    {
        // Arrange within the shared provider collection so no sibling listener can enable profiling concurrently.
        Assert.False(Profiler.IsEnabled);

        // Act by requesting a scope while the provider is known to be disabled.
        ProfileScope scope = Profiler.BeginScope("Test.Disabled");

        // Assert the disabled fast path returns the default scope and disposing it has no effect.
        Assert.Equal(default(ProfileScope), scope);
        scope.Dispose();
    }
    #endregion
}
