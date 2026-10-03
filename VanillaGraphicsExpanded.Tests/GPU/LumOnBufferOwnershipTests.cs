using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises temporal-role swaps and ownership across production target recreation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnBufferOwnershipTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Lifecycle tests

    /// <summary>The production blit owner survives target recreation and leaves its engine source alive on teardown.</summary>
    [Fact]
    public void SurfaceAlbedoCaptureSurvivesRecreationAndDisposal()
    {
        EnsureContextValid();
        using var scope = StateCache.Current.BindFramebufferScope();
        using var assets = new BinaryShaderApiFixture();
        using var terrain = new EngineTerrainBuffers(8, 8);
        var source = terrain.Output;
        var events = new RuntimeRenderEvents();
        var framebuffers = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(value => (int)value) + 1).ToList();
        framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
        var render = RuntimeEngineServices.Render(8, framebuffers, () => new float[16], () => new float[16], () => { });
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name switch
        {
            "get_Render" => render, "get_Event" => events.Api,
            _ => method.Invoke(assets.Api, args)
        });
        using var gbuffer = new GBufferManager(api);
        gbuffer.SetupGBuffers();
        var config = new VgeConfig();
        config.LumOn.ProbeSpacingPx = 8;
        using (var buffers = new LumOnBufferManager(api, config))
        {
            buffers.EnsureBuffers(8, 8);
            Assert.True(buffers.EnsureBuffers(8, 8));
            source[0].UploadDataImmediate(Enumerable.Repeat(0.25f, 8 * 8 * 4).ToArray());
            buffers.CaptureSurfaceAlbedo();
            Assert.All(buffers.SurfaceAlbedoTex!.ReadPixels(), value => Assert.Equal(0.25f, value));

            // Recreation must retire the scratch references before replacing the destination.
            var retired = buffers.SurfaceAlbedoTex;
            buffers.RequestRecreateBuffers("blit ownership regression");
            buffers.EnsureBuffers(8, 8);
            Assert.True(buffers.EnsureBuffers(8, 8));
            Assert.True(retired.IsDisposed);
            source[0].UploadDataImmediate(Enumerable.Repeat(0.5f, 8 * 8 * 4).ToArray());
            buffers.CaptureSurfaceAlbedo();
            Assert.All(buffers.SurfaceAlbedoTex!.ReadPixels(), value => Assert.Equal(0.5f, value));

            // An engine rebuild can replace attachment names at the same dimensions.
            // Its resize callback updates the same wrapper consumed by the retained blitter.
            var wrapper = gbuffer.PrimaryFramebuffer;
            int notifications = 0;
            wrapper.AttachmentsChanged += () => notifications++;
            for (int frame = 0; frame < 3; frame++)
            {
                gbuffer.SetupGBuffers();
                source.Bind();
                gbuffer.UnloadGBuffer(EnumFrameBuffer.Primary);
                source.Bind();
                gbuffer.LoadGBuffer(EnumFrameBuffer.Primary);
                buffers.CaptureSurfaceAlbedo();
            }
            Assert.Equal(0, notifications);
            Assert.All(buffers.SurfaceAlbedoTex!.ReadPixels(), value => Assert.Equal(0.5f, value));

            // Rebuild notification is authoritative even when dimensions and GL names
            // remain unchanged, and ordinary primary load/unload must not masquerade as one.
            ScreenResourceManager.HandleScreenResize();
            Assert.Equal(1, notifications);
            Assert.Same(wrapper, gbuffer.PrimaryFramebuffer);
            buffers.EnsureBuffers(8, 8);
            Assert.True(buffers.EnsureBuffers(8, 8));
            buffers.CaptureSurfaceAlbedo();
            Assert.All(buffers.SurfaceAlbedoTex!.ReadPixels(), value => Assert.Equal(0.5f, value));
            using var replacement = new EngineTerrainBuffers(8, 8);
            framebuffers[(int)EnumFrameBuffer.Primary] = replacement.Primary;
            gbuffer.SetupGBuffers();
            Assert.Same(wrapper, gbuffer.PrimaryFramebuffer);
            Assert.Equal(2, notifications);
            replacement.Color.UploadDataImmediate(Enumerable.Repeat(0.75f, 8 * 8 * 4).ToArray());
            buffers.CaptureSurfaceAlbedo();
            Assert.All(buffers.SurfaceAlbedoTex!.ReadPixels(), value => Assert.Equal(0.75f, value));
            framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
            gbuffer.SetupGBuffers();
        }
        Assert.True(GL.IsFramebuffer(source.FboId));
        Assert.True(GL.IsTexture(source[0].TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

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
