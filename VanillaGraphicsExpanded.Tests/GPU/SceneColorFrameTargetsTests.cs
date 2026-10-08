using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.Client.NoObf;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates borrowed scene storage through actual texture allocations and publication metadata.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorFrameTargetsTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>HDR storage accepts floating RGBA while rejecting normalized and integer scene images.</summary>
    [Theory]
    [InlineData(PixelInternalFormat.Rgba16f, true)]
    [InlineData(PixelInternalFormat.Rgba32f, true)]
    [InlineData(PixelInternalFormat.Rgba8, false)]
    [InlineData(PixelInternalFormat.Rgba32ui, false)]
    public void RequiresFloatingColorStorage(PixelInternalFormat format, bool expected)
    {
        EnsureContextValid();
        using var image = DynamicTexture2D.Create(2, 2, format);
        var frames = CreatePublications(image);
        var targets = new SceneColorFrameTargets();
        Assert.Equal(expected, targets.Prepare(frames, out _));
        Assert.Equal(expected, targets.Prepare(frames, out _));
        targets.Invalidate();
        Assert.True(image.IsValid);
    }

    /// <summary>Missing attachments and dimensions reject a frame; an explicit invalidation reimports resized borrowed storage.</summary>
    [Fact]
    public void MissingAndResizedPublicationsRequireCurrentMetadata()
    {
        EnsureContextValid();
        using var image = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        var frames = CreatePublications(image);
        var targets = new SceneColorFrameTargets();
        Assert.True(targets.Prepare(frames, out _));
        var primary = frames[(int)EnumFrameBuffer.Primary];
        frames[(int)EnumFrameBuffer.Primary] = null!;
        Assert.False(targets.Prepare(frames, out _));
        frames[(int)EnumFrameBuffer.Primary] = primary;
        primary.ColorTextureIds = [];
        Assert.False(targets.Prepare(frames, out _));
        primary.ColorTextureIds = [image.TextureId];
        primary.Width = 0;
        Assert.False(targets.Prepare(frames, out _));
        primary.Width = 2;

        // Engine publication follows the actual resize, and the owner explicitly
        // withdraws cached metadata before accepting that new generation.
        image.Resize(3, 3);
        targets.Invalidate();
        Assert.False(targets.Prepare(frames, out _));
        foreach (var frame in frames) { frame.Width = 3; frame.Height = 3; }
        Assert.True(targets.Prepare(frames, out _));
        Assert.True(image.IsValid);
    }
    /// <summary>The installed low-resolution allocation omits wrapper dimensions while creating valid native storage.</summary>
    [Theory]
    [InlineData(PixelInternalFormat.Rgba16f, true)]
    [InlineData(PixelInternalFormat.Rgba8, false)]
    public void InstalledLowResolutionAttachmentUsesNativeDimensions(PixelInternalFormat format, bool expected)
    {
        EnsureContextValid();
        var setup = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers));
        var instructions = PatchProcessor.GetOriginalInstructions(setup).ToArray();
        var setter = AccessTools.PropertySetter(typeof(List<FrameBufferRef>), "Item");
        int begin = Array.FindIndex(instructions, instruction => instruction.LoadsConstant((int)EnumFrameBuffer.BlurVerticalLowRes)
            && instruction.opcode == System.Reflection.Emit.OpCodes.Ldc_I4_8);
        Assert.True(begin >= 0);
        int end = Array.FindIndex(instructions, begin + 1, instruction => instruction.Calls(setter));
        Assert.True(end > begin);
        Assert.DoesNotContain(instructions[begin..end], instruction => instruction.operand is FieldInfo field
            && field.DeclaringType == typeof(FrameBufferRef) && (field.Name == nameof(FrameBufferRef.Width) || field.Name == nameof(FrameBufferRef.Height)));

        using var primaryImage = DynamicTexture2D.Create(128, 128, PixelInternalFormat.Rgba16f);
        var frames = CreatePublications(primaryImage);
        var low = new FrameBufferRef { FboId = GL.GenFramebuffer(), ColorTextureIds = [GL.GenTexture()] };
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(platform);
        try
        {
            // Execute the installed helper with the caller's quarter-size arguments and
            // FboId-only metadata, preserving the actual driver allocation behavior.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, low.FboId);
            AccessTools.Method(typeof(ClientPlatformWindows), "setupAttachment").Invoke(platform,
                [low, 128 / 4, 128 / 4, 0, PixelFormat.Rgba, format]);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int height);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureInternalFormat, out int actualFormat);
            Assert.Equal((32, 32, (int)format), (width, height, actualFormat));
            Assert.Equal((0, 0), (low.Width, low.Height));
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            frames[(int)EnumFrameBuffer.BlurVerticalLowRes] = low;
            var targets = new SceneColorFrameTargets();
            Assert.Equal(expected, targets.Prepare(frames, out string? failure));
            if (!expected) Assert.Contains(nameof(PixelInternalFormat.Rgba8), failure);
            Assert.Equal(expected, targets.Prepare(frames, out _));
            Assert.Equal((0, 0), (low.Width, low.Height));
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.DeleteFramebuffer(low.FboId);
            GL.DeleteTexture(low.ColorTextureIds[0]);
            StateCache.Current.InvalidateAll();
        }
    }

    #endregion

    #region Private
    /// <summary>Provides a complete engine slot list sharing one image so tests isolate storage validation.</summary>
    private static List<FrameBufferRef> CreatePublications(DynamicTexture2D image)
        => Enumerable.Range(0, Enum.GetValues<EnumFrameBuffer>().Max(value => (int)value) + 1)
            .Select(_ => new FrameBufferRef
            {
                Width = image.Width, Height = image.Height, ColorTextureIds = [image.TextureId]
            }).ToList();
    #endregion
}
