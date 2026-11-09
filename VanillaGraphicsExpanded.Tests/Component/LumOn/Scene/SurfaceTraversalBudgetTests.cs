using System.Numerics;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.Scene;

/// <summary>Checks persisted traversal limits and the collision fallback's full-distance geometry outcomes.</summary>
public sealed class SurfaceTraversalBudgetTests
{
    #region Public API
    /// <summary>One full supported segment reaches distance or proven sky without the old diagonal cell-budget failure.</summary>
    [Theory]
    [InlineData(false,1024,1024,(int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(true,1024,1024,(int)WorldProbeTraceOutcome.DistanceLimit)]
    [InlineData(true,1024,512,(int)WorldProbeTraceOutcome.BudgetExhausted)]
    [InlineData(false,400,1024,(int)WorldProbeTraceOutcome.Sky)]
    [InlineData(true,240,1024,(int)WorldProbeTraceOutcome.Sky)]
    public void FullSegmentPreservesOutcomeMeaning(bool diagonal,int height,int steps,int expected)
    {
        // Arrange
        var world=new ControlledVoxelWorld{MapSizeY=height};
        var scene=new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(world),false,steps);
        var direction=diagonal?Vector3.Normalize(new(1,.99f,.98f)):Vector3.UnitY;
        // Act and Assert
        Assert.Equal((WorldProbeTraceOutcome)expected,scene.Trace(new(.25,.5,.75),direction,512,CancellationToken.None,out _));
        Assert.Empty(world.LightQueries);
    }
    #endregion
}











