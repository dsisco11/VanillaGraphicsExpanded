using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies bounded readiness observation leaves fence consumption with its owner.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFenceCompletionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Completion observation
    /// <summary>A successful wait preserves the live fence for subsequent polling and owner consumption.</summary>
    [Fact]
    public void BoundedWaitDoesNotConsumeFence()
    {
        EnsureContextValid();
        using var fence = RHICommandContext.Current.InsertFence();
        var status = RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(5));
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
        EnsureContextValid();
        using var fence = RHICommandContext.Current.InsertFence();
        Assert.Throws<ArgumentOutOfRangeException>(() => RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(61)));
        Assert.True(fence.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
