using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks diagnostic outcomes against actual geometry, lighting, and cache trace results.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    #region Recorded Outcomes
    /// <summary>Independent scene conditions produce distinct outcomes without changing ordinary trace flags.</summary>
    [Theory]
    [InlineData("solid", 1u, 0f)]
    [InlineData("lit", 4u, 0f)]
    [InlineData("dark", 4u, 0f)]
    [InlineData("unpublished", 4u, 0f)]
    [InlineData("budget", 4u, 0f)]
    [InlineData("cache", 5u, 1f)]
    [InlineData("sky", 6u, 0.25f)]
    public void TraceOutcome_RecordsActualSourceAndAvailability(string scenario, uint expectedOutcome, float confidence)
    {
        EnsureShaderTestAvailable();
        // Arrange: Construct only the selected physical scene state and publication state for this outcome.
        using var fixture = new NearFieldVoxelFixture();
        var world = new ControlledVoxelWorld();
        if (scenario is "lit" or "dark")
        {
            world.AddRoom((-3, -3, -8), (3, 3, -2));
            float lighting = scenario == "lit" ? 0.25f : 0f;
            world.FillLight((-2, -2, -7), (2, 2, -3), new Vector4(lighting, lighting, lighting, 0));
        }
        if (scenario == "solid")
        {
            // Cover every initial cell reached by the normal-offset anchor.
            for (int x = -1; x <= 0; x++)
            for (int y = -1; y <= 0; y++)
                world.SetBlock(x, y, -5, new Vintagestory.API.Common.Block { BlockId = 1 });
        }
        if (scenario != "unpublished") fixture.Publish(world);

        // Act: Dispatch the configured trace with the selected traversal and cache modes.
        var result = Trace(fixture, budget: scenario == "budget" ? 1 : 256,
            nearFieldTracing: scenario != "sky", worldCache: scenario != "sky");

        // Assert: Compare the recorded GPU source, availability, and confidence with the scenario's contract.
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsOutcomes_TraceOutcome_RecordsActualSourceAndAvailability, parameters:new(expectedOutcome,confidence,scenario is "solid" or "lit" or "dark" ? 1 : 0,scenario=="sky" ? 1 : 0)).AssertEqual();
    }
    #endregion
    #endregion
}
