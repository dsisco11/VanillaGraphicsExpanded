using System.Reflection;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises temporal-role swaps and ownership across production target recreation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnBufferOwnershipTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Lifecycle tests

    /// <summary>Swapping roles preserves allocations; recreation retires both temporal roles and publication.</summary>
    [Fact]
    public void TemporalSwapsPreserveOwnershipAcrossRecreationAndDisposal()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var config = new VgeConfig();
        config.LumOn.HalfResolution = true;
        config.LumOn.ProbeSpacingPx = 8;
        using var buffers = new LumOnBufferManager(assets.Api, config);
        Assert.False(buffers.EnsureBuffers(32, 24));
        Assert.True(buffers.EnsureBuffers(32, 24));
        Assert.Equal(16, buffers.HalfResWidth);
        Assert.Equal(12, buffers.HalfResHeight);
        Assert.Equal(4, buffers.ProbeCountX);
        Assert.Equal(3, buffers.ProbeCountY);
        var originalResources = PublishedResources(buffers);
        var current = buffers.ScreenProbeAtlasCurrentTex;
        var history = buffers.ScreenProbeAtlasHistoryTex;
        var currentMeta = buffers.ScreenProbeAtlasMetaCurrentTex;
        var historyMeta = buffers.ScreenProbeAtlasMetaHistoryTex;
        var currentFbo = buffers.ScreenProbeAtlasCurrentFbo;

        buffers.SwapRadianceBuffers();
        Assert.Same(history, buffers.ScreenProbeAtlasCurrentTex);
        Assert.Same(current, buffers.ScreenProbeAtlasHistoryTex);
        Assert.Same(historyMeta, buffers.ScreenProbeAtlasMetaCurrentTex);
        Assert.Same(currentMeta, buffers.ScreenProbeAtlasMetaHistoryTex);
        Assert.Same(history, buffers.ScreenProbeAtlasCurrentFbo![0]);
        Assert.All(originalResources, resource => Assert.False(resource.IsDisposed));
        buffers.SwapRadianceBuffers();
        Assert.Same(currentFbo, buffers.ScreenProbeAtlasCurrentFbo);
        buffers.SwapRadianceBuffers();

        buffers.HasPublishedIndirect = true;
        var revision = buffers.HistoryRevision;
        buffers.RequestRecreateBuffers("ownership regression");
        Assert.False(buffers.HasPublishedIndirect);
        Assert.False(buffers.EnsureBuffers(32, 24));
        Assert.True(buffers.HistoryRevision > revision);
        Assert.All(originalResources, resource => Assert.True(resource.IsDisposed));
        Assert.True(buffers.EnsureBuffers(32, 24));

        var recreatedResources = PublishedResources(buffers);
        Assert.False(buffers.EnsureBuffers(48, 32));
        Assert.All(recreatedResources, resource => Assert.True(resource.IsDisposed));
        Assert.Equal(24, buffers.HalfResWidth);
        Assert.Equal(16, buffers.HalfResHeight);
        Assert.Equal(6, buffers.ProbeCountX);
        Assert.Equal(4, buffers.ProbeCountY);
        var finalResources = PublishedResources(buffers);
        buffers.SwapRadianceBuffers();
        buffers.Dispose();
        Assert.All(finalResources, resource => Assert.True(resource.IsDisposed));
    }

    #endregion

    #region Resource inspection

    /// <summary>Captures the public resource contract without depending on target-set storage details.</summary>
    private static GpuResource[] PublishedResources(LumOnBufferManager buffers)
    {
        return typeof(LumOnBufferManager).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => typeof(GpuResource).IsAssignableFrom(property.PropertyType))
            .Select(property => property.GetValue(buffers))
            .OfType<GpuResource>()
            .Distinct()
            .ToArray();
    }

    #endregion
}
