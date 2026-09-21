using System.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks diagnostic outcomes against actual geometry, lighting, and cache trace results.</summary>
public sealed partial class LumOnLocalTraceFunctionalTests
{
    #region Recorded Outcomes
    /// <summary>Independent scene conditions produce distinct outcomes without changing ordinary trace flags.</summary>
    [Theory]
    [InlineData("solid", 1u, 1f)]
    [InlineData("lit", 2u, 1f)]
    [InlineData("dark", 3u, 1f)]
    [InlineData("unpublished", 4u, 0f)]
    [InlineData("budget", 4u, 0f)]
    [InlineData("cache", 5u, 1f)]
    [InlineData("sky", 6u, 0.25f)]
    public void TraceOutcome_RecordsActualSourceAndAvailability(string scenario, uint expectedOutcome, float confidence)
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
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
            localTracing: scenario != "sky", worldCache: scenario != "sky");
        for (int pixel = 0; pixel < result.Meta.Length / 2; pixel++)
        {
            uint flags = Flags(result.Meta[pixel * 2 + 1]);
            Assert.Equal(expectedOutcome, (flags >> 16) & 7u);
            // Screen exits retain the lower sky confidence while still recording sky provenance.
            float expectedConfidence = scenario == "sky" && (flags & (1u << 2)) != 0 ? 0.05f : confidence;
            Assert.Equal(expectedConfidence, result.Meta[pixel * 2]);
            Assert.Equal(expectedOutcome is 1 or 2 or 3, (flags & 1u) != 0);
            Assert.Equal(expectedOutcome == 5, (flags & (1u << 5)) != 0);
            if (scenario == "lit") Assert.InRange(result.Radiance[pixel * 4], 0.248f, 0.253f);
            if (scenario is "solid" or "dark" or "unpublished" or "budget") Assert.Equal(0f, result.Radiance[pixel * 4]);
        }
    }
    #endregion
}