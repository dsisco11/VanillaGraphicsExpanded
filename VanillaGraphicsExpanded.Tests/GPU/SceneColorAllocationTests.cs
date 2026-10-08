using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks scene format selection against installed allocation IL and real driver storage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorAllocationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The installed primary sequence allocates scene color before glow and optional SSAO data.</summary>
    [Fact]
    public void InstalledPrimaryOrderingAndTranspilerRemainCompatible()
    {
        var method = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers));
        var original = PatchProcessor.GetOriginalInstructions(method).ToArray();
        var setter = AccessTools.PropertySetter(typeof(List<FrameBufferRef>), "Item");
        int begin = Array.FindIndex(original, instruction => instruction.Calls(setter));
        int end = Array.FindIndex(original, begin + 1, instruction => instruction.Calls(setter));
        var primary = original[begin..end];
        // Extract the internal format from each real TexImage2D argument sequence. The format
        // is the third argument: target, level, format precede dimensions and upload metadata.
        var formats = new List<int>();
        for (int index = 0; index < primary.Length; index++)
        {
            if (primary[index].operand is not MethodInfo call || call.Name != "TexImage2D") continue;
            int target = index - 1;
            while (target >= 0 && !primary[target].LoadsConstant((int)TextureTarget.Texture2D)) target--;
            Assert.True(target >= 0);
            Assert.True(primary[target + 1].LoadsConstant(0));
            formats.Add(Convert.ToInt32(primary[target + 2].operand));
        }
        Assert.Equal(new[] { (int)PixelInternalFormat.DepthComponent32, (int)PixelInternalFormat.Rgba8,
            (int)PixelInternalFormat.Rgba8, (int)PixelInternalFormat.Rgba16f, (int)PixelInternalFormat.Rgba16f }, formats);
        // Attachment calls independently establish that the first RGBA8 is color zero and
        // the second is glow one; both optional SSAO attachments occur afterwards.
        var attachmentConstants = primary.Where(instruction => instruction.opcode == OpCodes.Ldc_I4)
            .Select(instruction => (int)instruction.operand).Where(value => value >= 36064 && value <= 36067).ToArray();
        Assert.Equal(new[] { 36064, 36065, 36066, 36067 }, attachmentConstants.Take(4));
        var rewritten = SceneColorAllocationHook.Transpiler(original).ToArray();
        var selector = AccessTools.Method(typeof(SceneColorAllocationHook), nameof(SceneColorAllocationHook.SelectFormat));
        var assign = AccessTools.Method(typeof(SceneColorAllocationHook), nameof(SceneColorAllocationHook.Assign));
        Assert.Equal(original.Count(instruction => instruction.LoadsConstant((int)PixelInternalFormat.Rgba8)), rewritten.Count(instruction => instruction.Calls(selector)));
        Assert.Equal(original.Count(instruction => instruction.Calls(setter)), rewritten.Count(instruction => instruction.Calls(assign)));
        Assert.DoesNotContain(rewritten, instruction => instruction.Calls(setter));
        var harmony = new Harmony("VGE.Tests.SceneColorAllocation");
        try
        {
            harmony.CreateClassProcessor(typeof(SceneColorAllocationHook)).Patch();
            Assert.Contains(Harmony.GetPatchInfo(method)!.Transpilers, patch => patch.owner == harmony.Id);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Scene storage retains values above one while non-color formats keep their exact allocations.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryStorageRetainsRadianceAndDataFormats(bool ssao)
    {
        EnsureContextValid();
        var allocation = new SceneColorAllocation();
        allocation.Begin(EnumFrameBuffer.Primary);
        Assert.Equal(PixelInternalFormat.DepthComponent32, allocation.Select(PixelInternalFormat.DepthComponent32));
        var formats = new List<PixelInternalFormat> { allocation.Select(PixelInternalFormat.Rgba8), allocation.Select(PixelInternalFormat.Rgba8) };
        if (ssao) formats.AddRange(new[] { allocation.Select(PixelInternalFormat.Rgba16f), allocation.Select(PixelInternalFormat.Rgba16f) });
        Assert.Equal(PixelInternalFormat.Rgba16f, formats[0]);
        Assert.Equal(PixelInternalFormat.Rgba8, formats[1]);
        int texture = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, formats[0], 1, 1, 0, PixelFormat.Rgba, PixelType.Float, new[] { 8f, 4f, 2f, 1f });
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureInternalFormat, out int actual);
            Assert.Equal((int)PixelInternalFormat.Rgba16f, actual);
            float[] result = new float[4];
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, result);
            Assert.Equal(new[] { 8f, 4f, 2f, 1f }, result);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindTexture(TextureTarget.Texture2D, 0); GL.DeleteTexture(texture); StateCache.Current.InvalidateAll(); }
        foreach (var kind in Enum.GetValues<EnumFrameBuffer>())
        {
            allocation.Begin(kind);
            Assert.Equal(PixelInternalFormat.Rgb8, allocation.Select(PixelInternalFormat.Rgb8));
            Assert.Equal(PixelInternalFormat.DepthComponent32, allocation.Select(PixelInternalFormat.DepthComponent32));
            Assert.Equal(PixelInternalFormat.Rgba16f, allocation.Select(PixelInternalFormat.Rgba16f));
        }
    }

    /// <summary>Only retained luma is promoted; menu glare, OIT metadata and SSAO keep their native data formats.</summary>
    [Theory]
    [InlineData(EnumFrameBuffer.FindBright, false)]
    [InlineData(EnumFrameBuffer.Luma, true)]
    [InlineData(EnumFrameBuffer.BlurHorizontalMedRes, false)]
    [InlineData(EnumFrameBuffer.BlurVerticalMedRes, false)]
    [InlineData(EnumFrameBuffer.BlurHorizontalLowRes, false)]
    [InlineData(EnumFrameBuffer.BlurVerticalLowRes, false)]
    [InlineData(EnumFrameBuffer.GodRays, false)]
    [InlineData(EnumFrameBuffer.Transparent, false)]
    [InlineData(EnumFrameBuffer.SSAO, false)]
    [InlineData(EnumFrameBuffer.SSAOBlurHorizontal, false)]
    [InlineData(EnumFrameBuffer.SSAOBlurVertical, false)]
    [InlineData(EnumFrameBuffer.SSAOBlurHorizontalHalfRes, false)]
    [InlineData(EnumFrameBuffer.SSAOBlurVerticalHalfRes, false)]
    public void AttachmentRoleControlsRgbaPromotion(EnumFrameBuffer kind, bool sceneColor)
    {
        var allocation = new SceneColorAllocation();
        allocation.Begin(kind);
        Assert.Equal(sceneColor ? PixelInternalFormat.Rgba16f : PixelInternalFormat.Rgba8,
            allocation.Select(PixelInternalFormat.Rgba8));
        Assert.Equal(PixelInternalFormat.Rgb8, allocation.Select(PixelInternalFormat.Rgb8));
    }

    /// <summary>Nested setup restores the parent allocation sequence after inner cleanup.</summary>
    [Fact]
    public void NestedSetupRestoresPrimaryGlowPosition()
    {
        var frames = Enumerable.Range(0, 32).Select(_ => new FrameBufferRef()).ToList();
        SceneColorAllocationHook.Prefix(out var outer);
        try
        {
            SceneColorAllocationHook.Assign(frames, (int)EnumFrameBuffer.Primary, new FrameBufferRef());
            Assert.Equal(PixelInternalFormat.Rgba16f, SceneColorAllocationHook.SelectFormat(PixelInternalFormat.Rgba8));
            SceneColorAllocationHook.Prefix(out var inner);
            try
            {
                SceneColorAllocationHook.Assign(frames, (int)EnumFrameBuffer.Primary, new FrameBufferRef());
                Assert.Equal(PixelInternalFormat.Rgba16f, SceneColorAllocationHook.SelectFormat(PixelInternalFormat.Rgba8));
            }
            finally { SceneColorAllocationHook.Finalizer(inner); }
            Assert.Equal(PixelInternalFormat.Rgba8, SceneColorAllocationHook.SelectFormat(PixelInternalFormat.Rgba8));
        }
        finally { SceneColorAllocationHook.Finalizer(outer); }
    }
    #endregion
}
