using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Exercises real migrated owners through generated accessors without allocating GL objects.</summary>
public sealed class ProductionShaderAccessorTests
{
    #region Production settings
    /// <summary>Trace settings validate before scheduling, suppress identical writes and retain inactive numeric choices.</summary>
    [Fact]
    public void TraceAccessorsPreserveSelectedValuesAndScheduleChanges()
    {
        var trace = new ObservedTrace();
        Assert.Equal(10, trace.RaySteps);
        trace.RaySteps = 10;
        Assert.Equal(0, trace.ReloadRequests);
        trace.RaySteps = 24;
        Assert.Equal(1, trace.ReloadRequests);
        trace.WorldProbeResolution = 27;
        Assert.False(trace.WorldProbes);
        trace.WorldProbes = true;
        trace.WorldProbes = false;
        Assert.Equal(27, trace.WorldProbeResolution);
        trace.WorldProbes = true;
        Assert.Equal(27, trace.WorldProbeResolution);
        int requests = trace.ReloadRequests;
        Assert.Throws<ArgumentException>(() => trace.RayMaxDistance = float.NaN);
        Assert.Equal(requests, trace.ReloadRequests);
        Assert.Equal(4f, trace.RayMaxDistance);
        Assert.Equal(0, new ObservedTrace().WorldProbeResolution);
    }

    /// <summary>PBR legacy aliases and generated properties share one normalized selection.</summary>
    [Fact]
    public void CompositeAliasAndTypedAccessorsAgree()
    {
        var composite = new PBRCompositeShaderProgram();
        Assert.True(composite.EnableShortRangeAo);
        composite.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_ENABLE_BENT_NORMAL"] = "0" });
        Assert.False(composite.EnableShortRangeAo);
        composite.EnableShortRangeAo = true;
        Assert.True(composite.EnableShortRangeAo);
        composite.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_ENABLE_BENT_NORMAL"] = null });
        Assert.True(composite.EnableShortRangeAo);
        Assert.True(new PBRCompositeShaderProgram().EnableShortRangeAo);
    }

    /// <summary>PIS overrides leave exploration selections intact while inactive and reject nonfinite values.</summary>
    [Fact]
    public void PisAccessorsRetainInactiveExplorationSelections()
    {
        var mask = new LumOnProbeAtlasPisMaskShaderProgram { ExploreCount = 7, ExploreFraction = 0.4f };
        mask.ImportanceSampling = true;
        mask.BatchSlicing = true;
        mask.UniformMask = true;
        Assert.Equal(7, mask.ExploreCount);
        mask.BatchSlicing = false;
        mask.UniformMask = false;
        Assert.Equal(7, mask.ExploreCount);
        Assert.Equal(0.4f, mask.ExploreFraction);
        Assert.Throws<ArgumentException>(() => mask.WeightEpsilon = float.PositiveInfinity);
        Assert.Equal(-1, new LumOnProbeAtlasPisMaskShaderProgram().ExploreCount);
    }
    #endregion

    #region Scheduling observation
    /// <summary>Observes the real trace owner's generated setters at the existing scheduling boundary.</summary>
    private sealed class ObservedTrace : LumOnScreenProbeAtlasTraceShaderProgram
    {
        public int ReloadRequests { get; private set; }
        /// <summary>Counts preparation requests without initializing a game API.</summary>
        protected override void RequestRecompile() => ReloadRequests++;
    }
    #endregion
}
