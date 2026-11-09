using VanillaGraphicsExpanded.PBR.SceneColor;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Verifies the stage and framebuffer rules for selecting scene color.</summary>
public sealed class SceneColorBindingTests
{
    #region Public API
    /// <summary>Every stage and destination combination has an explicit scene or offscreen convention.</summary>
    [Fact]
    public void SceneConventionRequiresMatchingSceneTarget()
    {
        // Arrange: distinguish the primary scene target, OIT target, and an unrelated target.
        var primary = new FrameBufferRef { FboId = 11 };
        var oit = new FrameBufferRef { FboId = 12 };
        var other = new FrameBufferRef { FboId = 13 };
        // Act and assert: each installed stage accepts only its matching scene destination.
        foreach (EnumRenderStage stage in Enum.GetValues<EnumRenderStage>())
        {
            Assert.Equal(stage is EnumRenderStage.Opaque or EnumRenderStage.AfterOIT,
                SceneColorProgramBindings.SelectScene(stage, primary, primary, oit));
            Assert.Equal(stage == EnumRenderStage.OIT,
                SceneColorProgramBindings.SelectScene(stage, oit, primary, oit));
            Assert.False(SceneColorProgramBindings.SelectScene(stage, other, primary, oit));
            Assert.False(SceneColorProgramBindings.SelectScene(stage, null, primary, oit));
        }
    }
    #endregion
}
