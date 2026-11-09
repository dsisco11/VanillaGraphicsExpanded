using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks temporal-role swaps and ownership across production target recreation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnBufferOwnershipTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Swapping roles preserves allocations; recreation retires both temporal roles and publication.</summary>
    [Fact]
    public void TemporalSwapsPreserveOwnershipAcrossRecreationAndDisposal()
    {
        // Arrange one published allocation set, then retain its resource identities across each transition.
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
        Assert.Empty(originalResources.OfType<GpuFramebuffer>());
        var currentPass = buffers.ScreenProbeAtlasCurrentPass!;
        var current = buffers.ScreenProbeAtlasCurrentTex;
        var history = buffers.ScreenProbeAtlasHistoryTex;
        var currentMeta = buffers.ScreenProbeAtlasMetaCurrentTex;
        var historyMeta = buffers.ScreenProbeAtlasMetaHistoryTex;
        var currentFbo = buffers.ScreenProbeAtlasCurrentPass;

        // Act twice to verify role reversal and restoration without reallocating either temporal texture.
        buffers.SwapRadianceBuffers();
        Assert.Same(history, buffers.ScreenProbeAtlasCurrentTex);
        Assert.Same(current, buffers.ScreenProbeAtlasHistoryTex);
        Assert.Same(historyMeta, buffers.ScreenProbeAtlasMetaCurrentTex);
        Assert.Same(currentMeta, buffers.ScreenProbeAtlasMetaHistoryTex);
        Assert.Equal((GpuTextureRef)history!, buffers.ScreenProbeAtlasCurrentPass!.Colors[0].Image!.Value.Texture);
        Assert.All(originalResources, resource => Assert.False(resource.IsDisposed));
        buffers.SwapRadianceBuffers();
        Assert.Same(currentFbo, buffers.ScreenProbeAtlasCurrentPass);
        Assert.Same(currentPass, buffers.ScreenProbeAtlasCurrentPass!);
        buffers.SwapRadianceBuffers();

        // Recreate at the same size, then resize again; each transition must retire the previously published set.
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
        // Assert disposal retires every resource in the final published set after the role swap.
        Assert.All(finalResources, resource => Assert.True(resource.IsDisposed));
    }

    #endregion

    #region Private
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
