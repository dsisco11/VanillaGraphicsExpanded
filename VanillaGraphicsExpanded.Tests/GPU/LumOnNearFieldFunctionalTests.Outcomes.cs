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
        ObserveTrace(result, """
            float expectedConfidence=multipliers.w!=0.0 && (flags&4u)!=0u ? 0.05 : multipliers.y;
            bool valid=((flags>>16)&7u)==uint(multipliers.x) && b.r==expectedConfidence
                && ((flags&1u)!=0u)==(multipliers.z!=0.0)
                && ((flags&32u)!=0u)==(uint(multipliers.x)==5u);
            if(uint(multipliers.x)!=5u && uint(multipliers.x)!=6u)valid=valid && a.r==0.0;
            return bvec2(valid,false);
            """, parameters:new(expectedOutcome,confidence,scenario is "solid" or "lit" or "dark" ? 1 : 0,scenario=="sky" ? 1 : 0)).AssertEqual();
    }
    #endregion
}
