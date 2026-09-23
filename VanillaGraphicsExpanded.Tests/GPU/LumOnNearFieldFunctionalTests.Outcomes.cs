using System.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks diagnostic outcomes against actual geometry, lighting, and cache trace results.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
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
        var result = Trace(fixture, budget: scenario == "budget" ? 1 : 256,
            nearFieldTracing: scenario != "sky", worldCache: scenario != "sky");
        for (int pixel = 0; pixel < result.Meta.Length / 2; pixel++)
        {
            uint flags = Flags(result.Meta[pixel * 2 + 1]);
            Assert.Equal(expectedOutcome, (flags >> 16) & 7u);
            // Screen exits retain the lower sky confidence while still recording sky provenance.
            float expectedConfidence = scenario == "sky" && (flags & (1u << 2)) != 0 ? 0.05f : confidence;
            Assert.Equal(expectedConfidence, result.Meta[pixel * 2]);
            Assert.Equal(scenario is "solid" or "lit" or "dark", (flags & 1u) != 0);
            Assert.Equal(expectedOutcome == 5, (flags & (1u << 5)) != 0);
            if (scenario == "lit") Assert.Equal(0,result.Radiance[pixel * 4]);
            if (scenario is "solid" or "dark" or "unpublished" or "budget") Assert.Equal(0f, result.Radiance[pixel * 4]);
        }
    }
    #endregion
}
