using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.GameContent;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Verifies engine draw states are admitted for water capture only under the published policy.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterRefractionCapturePolicyTests
{
    #region Public API
    /// <summary>Capture eligibility excludes shadows, other players, body modes and unrelated stages.</summary>
    [Fact]
    public void CapturePolicyPreservesAllEngineDrawPaths()
    {
        // Arrange: enumerate the independent engine draw conditions and supported render modes.
        // Act and Assert: only an enabled first-person opaque draw by this player may be captured.
        foreach (bool enabled in new[] { false, true })
            foreach (bool self in new[] { false, true })
                foreach (bool shadow in new[] { false, true })
                    foreach (EnumRenderStage stage in Enum.GetValues<EnumRenderStage>())
                        foreach (RenderMode mode in Enum.GetValues<RenderMode>())
                            Assert.Equal(enabled && self && !shadow && stage == EnumRenderStage.Opaque && mode == RenderMode.FirstPerson,
                                WaterRefractionCapture.ShouldCapture(enabled, stage, shadow, self, mode));
    }
    #endregion
}
