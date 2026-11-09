using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks that only owned scene-color attachments are promoted to floating-point storage.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SceneColorFormatPolicyTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API

    /// <summary>Engine postprocess, OIT metadata and SSAO keep their native formats because scene intermediates are owned.</summary>
    [Theory]
    [InlineData(EnumFrameBuffer.FindBright, false)]
    [InlineData(EnumFrameBuffer.Luma, false)]
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
        // Arrange: Select an engine attachment role and its expected ownership from the theory row.
        var allocation = new SceneColorAllocation();
        allocation.Begin(kind);
        // Act and assert: Only scene-color-owned RGBA8 promotes; RGB8 remains unchanged for every role.
        Assert.Equal(sceneColor ? TextureFormat.Rgba16f : TextureFormat.Rgba8,
            allocation.Select(TextureFormat.Rgba8));
        Assert.Equal(TextureFormat.Rgb8, allocation.Select(TextureFormat.Rgb8));
    }
    #endregion
}
