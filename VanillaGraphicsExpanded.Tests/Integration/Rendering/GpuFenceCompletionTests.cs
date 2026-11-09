using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies bounded readiness observation leaves fence consumption with its owner.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuFenceCompletionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Completion observation
    /// <summary>A successful wait preserves the live fence for subsequent polling and owner consumption.</summary>
    [Fact]
    public void BoundedWaitDoesNotConsumeFence()
    {
        // Arrange: Insert a real fence whose completion is owned by the command context.
        EnsureContextValid();
        using var fence = RHICommandContext.Current.InsertFence();
        // Act: Wait with a bounded timeout, then poll and explicitly consume the signaled fence.
        var status = RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(5));
        // Assert: Readiness observation preserves validity until the owner consumes the fence.
        Assert.True(status is CompletionStatus.Ready);
        Assert.True(fence.IsValid);
        Assert.Equal(CompletionStatus.Ready, RHICommandContext.Current.Poll(fence));
        Assert.True(fence.TryConsumeIfSignaled());
        Assert.False(fence.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Invalid timeout bounds fail before issuing a GL wait or consuming the fence.</summary>
    [Fact]
    public void InvalidTimeoutPreservesFence()
    {
        // Arrange: Insert a live fence before supplying timeout values outside the supported range.
        EnsureContextValid();
        using var fence = RHICommandContext.Current.InsertFence();
        // Act and assert: Both invalid waits fail before consuming or invalidating the fence.
        Assert.Throws<ArgumentOutOfRangeException>(() => RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(61)));
        Assert.True(fence.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
