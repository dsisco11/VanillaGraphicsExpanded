using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Checks production surface-albedo capture across target recreation and disposal.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnBufferOwnershipTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The production blit owner survives target recreation and leaves its engine source alive on teardown.</summary>
    [Fact]
    public void SurfaceAlbedoCaptureSurvivesRecreationAndDisposal()
    {
        EnsureContextValid();
        // Arrange an engine-owned color source and the production G-buffer/blit owners that observe its attachment.
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
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
            // Act: capture the initial source, recreate LumOn targets, and verify the replacement blitter reads the new source.
            buffers.EnsureBuffers(8, 8);
            Assert.True(buffers.EnsureBuffers(8, 8));
            source[0].UploadDataImmediate(Enumerable.Repeat(0.25f, 8 * 8 * 4).ToArray());
            buffers.CaptureSurfaceAlbedo();
            // Assert the first source is copied before replacing the target set.
            ImageAssertions.Comparisons.CompareConstant(buffers.SurfaceAlbedoTex!, 0.25f).AssertEqual();

            // Recreation must retire the scratch references before replacing the destination.
            var retired = buffers.SurfaceAlbedoTex;
            buffers.RequestRecreateBuffers("blit ownership regression");
            buffers.EnsureBuffers(8, 8);
            Assert.True(buffers.EnsureBuffers(8, 8));
            Assert.True(retired.IsDisposed);
            source[0].UploadDataImmediate(Enumerable.Repeat(0.5f, 8 * 8 * 4).ToArray());
            buffers.CaptureSurfaceAlbedo();
            // Assert recreation retired the old scratch target and the new target captures current source data.
            ImageAssertions.Comparisons.CompareConstant(buffers.SurfaceAlbedoTex!, 0.5f).AssertEqual();

            // Act: ordinary unload/load cycles must retain the wrapper without reporting a rebuild.
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
            // Assert ordinary engine load/unload cycles did not emit attachment-rebuild notifications.
            Assert.Equal(0, notifications);
            ImageAssertions.Comparisons.CompareConstant(buffers.SurfaceAlbedoTex!, 0.5f).AssertEqual();

            // Rebuild notification is authoritative even when dimensions and GL names
            // remain unchanged, and ordinary primary load/unload must not masquerade as one.
            // A resize notification is authoritative even at equal dimensions; subsequent setup must update the retained blitter.
            ScreenResourceManager.HandleScreenResize();
            // Assert the resize callback reports a rebuild even though the wrapper identity is stable.
            Assert.Equal(1, notifications);
            Assert.Same(wrapper, gbuffer.PrimaryFramebuffer);
            buffers.EnsureBuffers(8, 8);
            Assert.True(buffers.EnsureBuffers(8, 8));
            buffers.CaptureSurfaceAlbedo();
            ImageAssertions.Comparisons.CompareConstant(buffers.SurfaceAlbedoTex!, 0.5f).AssertEqual();
            // Replace the primary attachment owner and verify the next capture follows its new color data.
            using var replacement = new EngineTerrainBuffers(8, 8);
            framebuffers[(int)EnumFrameBuffer.Primary] = replacement.Primary;
            gbuffer.SetupGBuffers();
            Assert.Same(wrapper, gbuffer.PrimaryFramebuffer);
            Assert.Equal(2, notifications);
            replacement.Color.UploadDataImmediate(Enumerable.Repeat(0.75f, 8 * 8 * 4).ToArray());
            buffers.CaptureSurfaceAlbedo();
            // Assert the retained wrapper now exposes the replacement engine attachment.
            ImageAssertions.Comparisons.CompareConstant(buffers.SurfaceAlbedoTex!, 0.75f).AssertEqual();
            framebuffers[(int)EnumFrameBuffer.Primary] = terrain.Primary;
            gbuffer.SetupGBuffers();
        }
        // Assert disposal of the LumOn blit owner left the engine's original source alive.
        Assert.True(GL.IsFramebuffer(source.FboId));
        Assert.True(GL.IsTexture(source[0].TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }

    #endregion

}
