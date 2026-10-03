using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the raw engine framebuffer boundary against VGE's cached primary blend policy.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GBufferExternalFramebufferTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region External state ownership
    /// <summary>An external OIT framebuffer bind must not let primary-only blend repair disable accumulation attachments.</summary>
    [Theory]
    [InlineData("CurrentFrameBuffer")]
    [InlineData("CurrentFrameBufferKeepVw")]
    public void ExternalFramebufferBindDoesNotAlterOitBlend(string property)
    {
        EnsureContextValid();
        using var framework = new ShaderTestFramework();
        using var primary = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        using var oit = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f, 6);
        using var assets = new BinaryShaderApiFixture();
        var render = new Mock<IRenderAPI>();
        var frames = Enumerable.Range(0, (int)EnumFrameBuffer.Primary + 1).Select(_ => new FrameBufferRef()).ToList();
        frames[(int)EnumFrameBuffer.Primary].FboId = primary.FboId;
        render.SetupGet(value => value.FrameBuffers).Returns(frames);
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);
        using var manager = new GBufferManager(api.Object);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GBufferManager).GetField("isInitialized", fields)!.SetValue(manager, true);
        typeof(GBufferManager).GetField("isInjected", fields)!.SetValue(manager, true);
        var harmony = new Harmony("VGE.Tests.FramebufferBoundary");
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(platform);
        var setter = AccessTools.PropertySetter(typeof(ClientPlatformWindows), property);
        try
        {
            harmony.CreateClassProcessor(typeof(FramebufferBindingHook)).Patch();
            primary.BindWithViewport();
            // Invoke the installed engine setter: its raw GL bind must be observed by the production hook.
            setter.Invoke(platform, [new FrameBufferRef { FboId = oit.FboId, Width = 1, Height = 1 }]);
            Assert.Equal(oit.FboId, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(oit.FboId, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(oit.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            GL.Enable(IndexedEnableCap.Blend, 4); GL.Enable(IndexedEnableCap.Blend, 5);
            GL.BlendFunc(4, BlendingFactorSrc.One, BlendingFactorDest.One);
            GL.BlendFunc(5, BlendingFactorSrc.One, BlendingFactorDest.One);
            manager.ReapplyGBufferBlendState();
            foreach (int index in new[] { 4, 5 })
            {
                Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, index), "Primary-only repair disabled OIT accumulation.");
                GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, index, out int source);
                GL.GetInteger((GetIndexedPName)GetPName.BlendDstRgb, index, out int destination);
                Assert.Equal((int)BlendingFactorSrc.One, source); Assert.Equal((int)BlendingFactorDest.One, destination);
            }
            setter.Invoke(platform, [frames[(int)EnumFrameBuffer.Primary]]);
            manager.ReapplyGBufferBlendState();
            Assert.False(GL.IsEnabled(IndexedEnableCap.Blend, 4));
            Assert.False(GL.IsEnabled(IndexedEnableCap.Blend, 5));
            setter.Invoke(platform, [null]);
            Assert.Equal(0, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(0, StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(0, GL.GetInteger(GetPName.DrawFramebufferBinding));
        }
        finally { harmony.UnpatchAll(harmony.Id); GpuFramebuffer.Unbind(); }
    }
    /// <summary>Receiver storage is reused without transferring owned fallback textures into the engine deletion list.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReceiverPositionPreservesTextureOwnership(bool enginePosition)
    {
        EnsureContextValid();
        using var framework = new ShaderTestFramework();
        using var primary = framework.CreateTestGBuffer(2, 2, PixelInternalFormat.Rgba16f, 4);
        using var assets = new BinaryShaderApiFixture();
        var frames = Enumerable.Range(0, (int)EnumFrameBuffer.Primary + 1).Select(_ => new FrameBufferRef()).ToList();
        var frame = frames[(int)EnumFrameBuffer.Primary];
        frame.FboId = primary.FboId;
        frame.Width = frame.Height = 2;
        frame.ColorTextureIds = new int[4];
        for (int slot = 0; slot < 3; slot++) frame.ColorTextureIds[slot] = primary[slot].TextureId;
        int borrowed = primary[3].TextureId;
        frame.ColorTextureIds[3] = enginePosition ? borrowed : 0;
        var render = new Mock<IRenderAPI>();
        render.SetupGet(value => value.FrameBuffers).Returns(frames);
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);
        int receiver;
        using (var manager = new GBufferManager(api.Object))
        {
            manager.SetupGBuffers();
            receiver = manager.PositionTextureId;
            Assert.NotEqual(0, receiver);
            Assert.Equal(enginePosition ? borrowed : 0, frame.ColorTextureIds[3]);
            manager.SetupGBuffers();
            Assert.Equal(receiver, manager.PositionTextureId);
            if (enginePosition) Assert.Equal(borrowed, receiver);
            else
            {
                frame.Width = frame.Height = 4;
                manager.SetupGBuffers();
                receiver = manager.PositionTextureId;
                GL.GetTextureLevelParameter(receiver, 0, GetTextureParameter.TextureWidth, out int width);
                GL.GetTextureLevelParameter(receiver, 0, GetTextureParameter.TextureHeight, out int height);
                Assert.Equal(4, width);
                Assert.Equal(4, height);
                Assert.Equal(0, frame.ColorTextureIds[3]);
            }
        }
        Assert.Equal(enginePosition, GL.IsTexture(receiver));
        Assert.True(GL.IsTexture(borrowed));
        GpuFramebuffer.Unbind();
    }

    /// <summary>Primary teardown must not transfer VGE-owned attachments to the engine's deletion list.</summary>
    [Fact]
    public void UnloadPrimaryRemovesVgeTexturesFromEngineBookkeeping()
    {
        EnsureContextValid();
        using var framework = new ShaderTestFramework();
        using var primary = framework.CreateTestGBuffer(2, 2, PixelInternalFormat.Rgba16f, 4);
        using var assets = new BinaryShaderApiFixture();
        var frames = Enumerable.Range(0, (int)EnumFrameBuffer.Primary + 1).Select(_ => new FrameBufferRef()).ToList();
        var frame = frames[(int)EnumFrameBuffer.Primary];
        frame.FboId = primary.FboId;
        frame.Width = frame.Height = 2;
        frame.ColorTextureIds = Enumerable.Range(0, 4).Select(slot => primary[slot].TextureId).ToArray();
        var render = new Mock<IRenderAPI>();
        render.SetupGet(value => value.FrameBuffers).Returns(frames);
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);

        using var manager = new GBufferManager(api.Object);
        manager.SetupGBuffers();
        int[] owned = [manager.NormalTextureId, manager.MaterialTextureId, manager.PatchIdTextureId, manager.EnvironmentTextureId];

        Assert.Equal(4, frame.ColorTextureIds.Length);
        Assert.DoesNotContain(owned, texture => frame.ColorTextureIds.Contains(texture));

        manager.UnloadGBuffer(EnumFrameBuffer.Primary);

        Assert.Equal(4, frame.ColorTextureIds.Length);
        Assert.All(owned, texture => Assert.True(GL.IsTexture(texture)));
        GpuFramebuffer.Unbind();
    }

    /// <summary>Resize recreation safely abandons attachment names already deleted by engine teardown.</summary>
    [Fact]
    public void ResizeAfterExternalTextureDeletionDoesNotDeleteStaleNames()
    {
        EnsureContextValid();
        using var framework = new ShaderTestFramework();
        using var primary = framework.CreateTestGBuffer(2, 2, PixelInternalFormat.Rgba16f, 4);
        using var assets = new BinaryShaderApiFixture();
        var frames = Enumerable.Range(0, (int)EnumFrameBuffer.Primary + 1).Select(_ => new FrameBufferRef()).ToList();
        var frame = frames[(int)EnumFrameBuffer.Primary];
        frame.FboId = primary.FboId;
        frame.Width = frame.Height = 2;
        frame.ColorTextureIds = Enumerable.Range(0, 4).Select(slot => primary[slot].TextureId).ToArray();
        var render = new Mock<IRenderAPI>();
        render.SetupGet(value => value.FrameBuffers).Returns(frames);
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);

        using var manager = new GBufferManager(api.Object);
        manager.SetupGBuffers();
        int[] stale = [manager.NormalTextureId, manager.MaterialTextureId, manager.PatchIdTextureId, manager.EnvironmentTextureId];
        GL.DeleteTextures(stale.Length, stale);
        frame.Width = frame.Height = 4;

        manager.SetupGBuffers();

        Assert.Equal(ErrorCode.NoError, GL.GetError());
        Assert.All([manager.NormalTextureId, manager.MaterialTextureId, manager.PatchIdTextureId, manager.EnvironmentTextureId],
            texture => Assert.True(GL.IsTexture(texture)));
        GpuFramebuffer.Unbind();
    }

    /// <summary>Direct-light buffer recreation does not restore a texture binding deleted by the engine.</summary>
    [Fact]
    public void DirectLightingResizeInvalidatesExternallyDeletedTextureBinding()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var frames = Enumerable.Repeat<FrameBufferRef>(null!, Enum.GetValues<EnumFrameBuffer>().Max(value => (int)value) + 1).ToList();
        frames[(int)EnumFrameBuffer.Primary] = new FrameBufferRef { Width = 4, Height = 4 };
        var render = new Mock<IRenderAPI>();
        render.SetupGet(value => value.FrameBuffers).Returns(frames);
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);
        using var buffers = new DirectLightingBufferManager(api.Object);
        Assert.True(buffers.EnsureBuffers(2, 2));
        using var stale = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8);
        StateCache.Current.BindTexture(TextureTarget.Texture2D, 0, stale.TextureId);
        GL.DeleteTexture(stale.ReleaseHandle().ToInt32());
        GBufferHooks.RebuildFrameBuffers_Hook();

        Assert.True(buffers.EnsureBuffers(4, 4));

        Assert.Equal(ErrorCode.NoError, GL.GetError());
        Assert.True(GL.IsTexture(buffers.DirectDiffuseTextureId));
        Assert.True(GL.IsTexture(buffers.DirectSpecularTextureId));
        Assert.True(GL.IsTexture(buffers.EmissiveTextureId));
    }
    #endregion
}
