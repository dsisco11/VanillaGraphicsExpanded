using VanillaGraphicsExpanded.Rendering.Profiling;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies stable names for the installed engine render stages.</summary>
public sealed class EngineRenderStageMetadataTests
{
    #region Public API
    /// <summary>Every installed stage has a stable name, including new stages omitted by the old allowlist.</summary>
    [Fact]
    public void AllStagesAreNamed()
    {
        // Arrange and act: enumerate every engine stage and obtain its profiling name.
        foreach (var stage in Enum.GetValues<EnumRenderStage>())
        {
            // Assert: the metadata name is stable for each installed value.
            Assert.Equal($"VS.{stage}", EngineRenderScopes.StageName(stage));
        }
    }
    #endregion
}
