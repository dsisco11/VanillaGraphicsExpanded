using System.Numerics;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Checks persisted traversal limits and the collision fallback's full-distance geometry outcomes.</summary>
public sealed class SurfaceTraversalBudgetTests
{
    #region Configuration
    /// <summary>Missing configuration fields receive a sufficient traversal limit and finite distance.</summary>
    [Fact]
    public void DefaultsCoverFiniteDistance()
    {
        var config=Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>("{\"LumOn\":{\"LumonScene\":{}}}")!;
        Assert.Equal(512,config.LumOn.LumonScene.RelightMaxTraceDistance);
        Assert.Equal(1024,config.LumOn.LumonScene.RelightMaxDdaSteps);
    }

    /// <summary>Persisted values retain supported custom limits and clamp invalid ranges.</summary>
    [Theory]
    [InlineData(-1,-1,1,0)] [InlineData(0,0,1,0)] [InlineData(96,64,96,64)] [InlineData(999,9999,512,1024)]
    public void LimitsRoundTripAndSanitize(int distance,int steps,int expectedDistance,int expectedSteps)
    {
        var config=new VgeConfig();config.LumOn.LumonScene.RelightMaxTraceDistance=distance;
        config.LumOn.LumonScene.RelightMaxDdaSteps=steps;
        var copy=Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(config))!;
        copy.Sanitize();Assert.Equal(expectedDistance,copy.LumOn.LumonScene.RelightMaxTraceDistance);
        Assert.Equal(expectedSteps,copy.LumOn.LumonScene.RelightMaxDdaSteps);
    }
    #endregion

    #region CPU routes
    /// <summary>One full supported segment reaches distance or proven sky without the old diagonal cell-budget failure.</summary>
    [Theory]
    [InlineData(false,1024,1024,(int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(true,1024,1024,(int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(true,1024,512,(int)WorldProbeTraceOutcome.BudgetExhausted)]
    [InlineData(false,400,1024,(int)WorldProbeTraceOutcome.Sky)]
    [InlineData(true,240,1024,(int)WorldProbeTraceOutcome.Sky)]
    public void FullSegmentPreservesOutcomeMeaning(bool diagonal,int height,int steps,int expected)
    {
        var world=new ControlledVoxelWorld{MapSizeY=height};
        var scene=new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false,steps);
        var direction=diagonal?Vector3.Normalize(new(1,.99f,.98f)):Vector3.UnitY;
        Assert.Equal((WorldProbeTraceOutcome)expected,scene.Trace(new(.25,.5,.75),direction,512,CancellationToken.None,out _));
        Assert.Empty(world.LightQueries);
    }
    #endregion
}
