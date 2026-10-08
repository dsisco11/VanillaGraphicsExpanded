using Vintagestory.Client;
using VanillaGraphicsExpanded.PBR.SceneColor;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises installed engine publication and deletion ordering with real OpenGL textures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineFramebufferRebuildTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private static List<FrameBufferRef>? replacement;
    private static int allocationCalls;

    #region Engine lifecycle
    /// <summary>Startup replaces pre-mod normalized storage before borrowing and refuses an incompatible replacement.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void SceneColorStartupRebuildsBeforeBorrowing(bool compatibleReplacement, bool suppressed)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(platform);
        var previousPlatform = ScreenManager.Platform;
        bool previousSuppression = ShaderRegistry.SupressShaderAndBufferReloads;
        var frameField = AccessTools.Field(typeof(ClientPlatformWindows), "frameBuffers");
        var oldFrames = CreateSceneFrames(false);
        int oldColor = oldFrames[(int)EnumFrameBuffer.Primary].ColorTextureIds[0];
        frameField.SetValue(platform, oldFrames);
        var render = new Mock<IRenderAPI>();
        render.SetupGet(value => value.FrameBuffers).Returns(() => (List<FrameBufferRef>)frameField.GetValue(platform)!);
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);
        replacement = CreateSceneFrames(compatibleReplacement);
        allocationCalls = 0;
        var harmony = new Harmony("VGE.Tests.SceneColorStartupRebuild");
        try
        {
            ScreenManager.Platform = platform;
            ShaderRegistry.SupressShaderAndBufferReloads = suppressed;
            // Retain the installed replacement/publication/deletion body; only native
            // window allocation is supplied by the same real-image fixture as rebuild tests.
            harmony.Patch(AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers)),
                prefix: new HarmonyMethod(typeof(EngineFramebufferRebuildTests), nameof(SupplyFramebuffers)));
            if (compatibleReplacement && !suppressed)
            {
                SceneColorPipeline.InitializeStorage(api.Object);
                Assert.Equal(1, allocationCalls);
                using var manager = new GBufferManager(api.Object);
                manager.SetupGBuffers();
                Assert.Equal(replacement[(int)EnumFrameBuffer.Primary].ColorTextureIds[3], manager.PositionTextureId);
                Assert.NotEqual(oldColor, replacement[(int)EnumFrameBuffer.Primary].ColorTextureIds[0]);
                SceneColorPipeline.InitializeStorage(api.Object);
                Assert.Equal(1, allocationCalls);
            }
            else
            {
                var error = Assert.Throws<InvalidOperationException>(() => SceneColorPipeline.InitializeStorage(api.Object));
                Assert.Contains("could not initialize scene storage", error.Message);
                Assert.Contains("Primary", error.Message);
                Assert.Contains("Rgba8", error.Message);
                Assert.Equal(suppressed ? 0 : 1, allocationCalls);
            }
            Assert.Same(suppressed ? oldFrames : replacement, render.Object.FrameBuffers);
            Assert.Equal(!suppressed, oldFrames[(int)EnumFrameBuffer.Primary].Disposed);
            Assert.Equal(suppressed, GL.IsTexture(oldColor));
            AssertNoGLError("scene-color startup rebuild");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            ScreenManager.Platform = previousPlatform;
            ShaderRegistry.SupressShaderAndBufferReloads = previousSuppression;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            AccessTools.Method(typeof(ClientPlatformWindows), "DisposeFrameBuffers").Invoke(platform, [frameField.GetValue(platform)]);
            if (suppressed) AccessTools.Method(typeof(ClientPlatformWindows), "DisposeFrameBuffers").Invoke(platform, [replacement]);
            replacement = null;
            StateCache.Current.InvalidateAll();
        }
    }


    /// <summary>All direct-light inputs must survive the real engine rebuild, including equal-sized replacement.</summary>
    [Theory]
    [InlineData(true, 2)]
    [InlineData(true, 4)]
    [InlineData(false, 2)]
    [InlineData(false, 4)]
    public void RebuildPublishesLiveDirectLightingInputs(bool enginePosition, int size)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(platform);
        var frameField = AccessTools.Field(typeof(ClientPlatformWindows), "frameBuffers");
        var oldFrames = CreateFrames(2, enginePosition);
        frameField.SetValue(platform, oldFrames);
        var render = new Mock<IRenderAPI>();
        render.SetupGet(value => value.FrameBuffers).Returns(() => (List<FrameBufferRef>)frameField.GetValue(platform)!);
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);
        using var manager = new GBufferManager(api.Object);
        manager.SetupGBuffers();
        int oldPosition = manager.PositionTextureId;
        using var direct = new DirectLightingBufferManager(api.Object);
        Assert.True(direct.EnsureBuffers(2, 2));
        int[] originalOutputs = [direct.DirectDiffuseTextureId, direct.DirectSpecularTextureId, direct.EmissiveTextureId];
        replacement = CreateFrames(size, enginePosition);
        var harmony = new Harmony("VGE.Tests.EngineFramebufferRebuild");
        try
        {
            // Only replace native window-dependent allocation. Publication and retirement run installed engine IL.
            harmony.Patch(AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers)),
                prefix: new HarmonyMethod(typeof(EngineFramebufferRebuildTests), nameof(SupplyFramebuffers)));
            harmony.CreateClassProcessor(typeof(GBufferHooks)).Patch();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            AccessTools.Method(typeof(ClientPlatformWindows), "RebuildFrameBuffers").Invoke(platform, null);
            Assert.Same(replacement, render.Object.FrameBuffers);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var primary = replacement[(int)EnumFrameBuffer.Primary];
            (string Name, int Unit, int Texture)[] inputs =
            [
                ("primary color", 0, primary.ColorTextureIds[0]),
                ("primary depth", 1, primary.DepthTextureId),
                ("G-buffer normal", 2, manager.NormalTextureId),
                ("G-buffer position", 6, manager.PositionTextureId),
                ("G-buffer environment", 7, manager.EnvironmentTextureId),
                ("G-buffer material", 3, manager.MaterialTextureId),
                ("near shadow depth", 4, replacement[(int)EnumFrameBuffer.ShadowmapNear].DepthTextureId),
                ("far shadow depth", 5, replacement[(int)EnumFrameBuffer.ShadowmapFar].DepthTextureId)
            ];
            // Attribute each production binding independently; these are the same texture objects consumed by direct lighting.
            var failures = new List<string>();
            for (int index = 0; index < inputs.Length; index++)
            {
                StateCache.Current.BindTexture(TextureTarget.Texture2D, inputs[index].Unit, inputs[index].Texture);
                var error = GL.GetError();
                if (error != ErrorCode.NoError) failures.Add($"{inputs[index].Name}: texture {inputs[index].Texture}, {error}");
            }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
            if (enginePosition)
            {
                Assert.NotEqual(oldPosition, primary.ColorTextureIds[3]);
                Assert.Equal(primary.ColorTextureIds[3], manager.PositionTextureId);
            }
            GL.GetTextureLevelParameter(manager.PositionTextureId, 0, GetTextureParameter.TextureWidth, out int width);
            Assert.Equal(size, width);
            Assert.True(oldFrames[(int)EnumFrameBuffer.Primary].Disposed);
            Assert.Equal(4, primary.ColorTextureIds.Length);
            Assert.Equal(originalOutputs, new[] { direct.DirectDiffuseTextureId, direct.DirectSpecularTextureId, direct.EmissiveTextureId });
            foreach (int output in originalOutputs)
            {
                GL.GetTextureLevelParameter(output, 0, GetTextureParameter.TextureWidth, out int outputWidth);
                GL.GetTextureLevelParameter(output, 0, GetTextureParameter.TextureHeight, out int outputHeight);
                Assert.Equal(size, outputWidth);
                Assert.Equal(size, outputHeight);
            }
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, primary.FboId);
            int[] attachedTextures = [manager.PositionTextureId, manager.NormalTextureId, manager.MaterialTextureId, manager.PatchIdTextureId, manager.EnvironmentTextureId];
            for (int slot = 3; slot <= 7; slot++)
            {
                GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + slot,
                    FramebufferParameterName.FramebufferAttachmentObjectName, out int attachedTexture);
                Assert.Equal(attachedTextures[slot - 3], attachedTexture);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            for (int index = 0; index < 8; index++) GL.BindTextureUnit(index, 0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            AccessTools.Method(typeof(ClientPlatformWindows), "DisposeFrameBuffers").Invoke(platform, [frameField.GetValue(platform)]);
            replacement = null;
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Allocation fixture
    /// <summary>Returns real test allocations while preserving all production Harmony postfixes.</summary>
    private static bool SupplyFramebuffers(ref List<FrameBufferRef> __result)
    {
        allocationCalls++;
        __result = replacement!;
        return false;
    }

    /// <summary>Allocates engine-owned primary and shadow attachments without a game or native window lifecycle.</summary>
    private static List<FrameBufferRef> CreateFrames(int size, bool enginePosition)
    {
        var frames = Enumerable.Range(0, Enum.GetValues<EnumFrameBuffer>().Max(value => (int)value) + 1)
            .Select(_ => new FrameBufferRef { ColorTextureIds = [] }).ToList();
        foreach (var kind in new[] { EnumFrameBuffer.Primary, EnumFrameBuffer.ShadowmapNear, EnumFrameBuffer.ShadowmapFar })
        {
            var frame = frames[(int)kind];
            frame.Width = frame.Height = size;
            frame.FboId = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, frame.FboId);
            frame.DepthTextureId = CreateTexture(size, true);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, frame.DepthTextureId, 0);
            if (kind != EnumFrameBuffer.Primary) continue;
            frame.ColorTextureIds = new int[4];
            for (int slot = 0; slot < (enginePosition ? 4 : 3); slot++)
            {
                frame.ColorTextureIds[slot] = CreateTexture(size, false);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + slot, TextureTarget.Texture2D, frame.ColorTextureIds[slot], 0);
            }
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        StateCache.Current.InvalidateAll();
        return frames;
    }

    /// <summary>Creates every mandatory scene publication with optional pre-mod normalized primary storage.</summary>
    private static List<FrameBufferRef> CreateSceneFrames(bool hdr)
    {
        var frames = CreateFrames(2, true);
        var primary = frames[(int)EnumFrameBuffer.Primary];
        if (!hdr)
        {
            GL.DeleteTexture(primary.ColorTextureIds[0]);
            primary.ColorTextureIds[0] = CreateTexture(2, false, SizedInternalFormat.Rgba8);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, primary.FboId);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, primary.ColorTextureIds[0], 0);
        }
        foreach (var kind in new[] { EnumFrameBuffer.Luma, EnumFrameBuffer.FindBright,
            EnumFrameBuffer.BlurHorizontalMedRes, EnumFrameBuffer.BlurVerticalMedRes,
            EnumFrameBuffer.BlurHorizontalLowRes, EnumFrameBuffer.BlurVerticalLowRes, EnumFrameBuffer.GodRays })
        {
            var frame = frames[(int)kind];
            frame.Width = frame.Height = 2;
            frame.FboId = GL.GenFramebuffer();
            frame.ColorTextureIds = [CreateTexture(2, false)];
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, frame.FboId);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, frame.ColorTextureIds[0], 0);
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        StateCache.Current.InvalidateAll();
        return frames;
    }

    /// <summary>Creates a texture object with complete immutable storage for engine deletion and production binding.</summary>
    private static int CreateTexture(int size, bool depth, SizedInternalFormat colorFormat = SizedInternalFormat.Rgba16f)
    {
        int texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.TexStorage2D(TextureTarget2d.Texture2D, 1, depth ? SizedInternalFormat.DepthComponent32f : colorFormat, size, size);
        return texture;
    }
    #endregion
}



