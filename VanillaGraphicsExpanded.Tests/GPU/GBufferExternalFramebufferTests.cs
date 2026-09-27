using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.HarmonyPatches;
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
            Assert.Equal(oit.FboId, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(oit.FboId, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
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
            Assert.Equal(0, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
            Assert.Equal(0, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(0, GL.GetInteger(GetPName.DrawFramebufferBinding));
        }
        finally { harmony.UnpatchAll(harmony.Id); GpuFramebuffer.Unbind(); }
    }
    #endregion
}
