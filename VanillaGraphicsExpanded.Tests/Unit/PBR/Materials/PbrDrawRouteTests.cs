using VanillaGraphicsExpanded.HarmonyPatches;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Protects lighting ownership at engine draw-stage boundaries.</summary>
public sealed class PbrDrawRouteTests
{
    #region Public API
    /// <summary>Only supported scene stages opt into deferred or forward PBR.</summary>
    [Fact]
    public void EveryEngineStageHasExplicitExpectedOwnership()
    {
        // Arrange: provide distinct scene targets and enumerate every engine render stage.
        var primary = new FrameBufferRef { FboId = 11 };
        var transparent = new FrameBufferRef { FboId = 12 };
        var offscreen = new FrameBufferRef { FboId = 13 };
        // Act and assert: each stage routes only to its permitted scene target.
        foreach (EnumRenderStage stage in Enum.GetValues<EnumRenderStage>())
        {
            int expected = stage == EnumRenderStage.Opaque ? 1
                : stage is EnumRenderStage.OIT or EnumRenderStage.AfterOIT ? 2 : 0;
            FrameBufferRef target = stage == EnumRenderStage.OIT ? transparent : primary;
            FrameBufferRef wrongTarget = stage == EnumRenderStage.OIT ? primary : transparent;
            Assert.Equal(expected, PbrDrawRouteHook.Route(stage, target, primary, transparent));
            Assert.Equal(0, PbrDrawRouteHook.Route(stage, wrongTarget, primary, transparent));
            Assert.Equal(0, PbrDrawRouteHook.Route(stage, offscreen, primary, transparent));
            Assert.Equal(0, PbrDrawRouteHook.Route(stage, null, primary, transparent));
            Assert.Equal(0, PbrDrawRouteHook.Route(stage, target, null, null));
        }
    }

    /// <summary>Target wrappers may differ while referring to the same scene framebuffer.</summary>
    [Fact]
    public void MatchingFramebufferIdentitySelectsSceneOwnership()
    {
        // Arrange, act, and assert: distinct wrappers with matching framebuffer IDs retain scene ownership.
        var primary = new FrameBufferRef { FboId = 11 };
        var transparent = new FrameBufferRef { FboId = 12 };
        Assert.Equal(1, PbrDrawRouteHook.Route(EnumRenderStage.Opaque, new FrameBufferRef { FboId = 11 }, primary, transparent));
        Assert.Equal(2, PbrDrawRouteHook.Route(EnumRenderStage.OIT, new FrameBufferRef { FboId = 12 }, primary, transparent));
        Assert.Equal(2, PbrDrawRouteHook.Route(EnumRenderStage.AfterOIT, new FrameBufferRef { FboId = 11 }, primary, transparent));
    }
    #endregion
}
