using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Exercises real migrated owners through generated accessors without allocating GL objects.</summary>
public sealed class ProductionShaderAccessorTests
{
    #region Public API
    #region Production settings
    /// <summary>Trace settings validate before scheduling, suppress identical writes and retain inactive numeric choices.</summary>
    [Fact]
    public void TraceAccessorsPreserveSelectedValuesAndScheduleChanges()
    {
        // Arrange: Observe the production trace owner through its existing preparation hook.
        var trace = new ObservedTrace();
        // Assert: The owner starts from its declared defaults.
        Assert.Equal(10, trace.RaySteps);
        // Act: Write the unchanged value.
        trace.RaySteps = 10;
        // Assert: Unchanged writes do not schedule preparation.
        Assert.Equal(0, trace.ReloadRequests);
        trace.RaySteps = 24;
        // Assert: Changed values schedule while inactive numeric choices remain retained.
        Assert.Equal(1, trace.ReloadRequests);
        // Act: Change and then disable the controlling feature while keeping the numeric selection.
        trace.WorldProbeResolution = 27;
        Assert.False(trace.WorldProbes);
        trace.WorldProbes = true;
        trace.WorldProbes = false;
        Assert.Equal(27, trace.WorldProbeResolution);
        trace.WorldProbes = true;
        Assert.Equal(27, trace.WorldProbeResolution);
        // Act: Capture the request count, then submit an invalid value.
        int requests = trace.ReloadRequests;
        Assert.Throws<ArgumentException>(() => trace.RayMaxDistance = float.NaN);
        // Assert: Rejection preserves both the stored value and request count.
        Assert.Equal(requests, trace.ReloadRequests);
        Assert.Equal(4f, trace.RayMaxDistance);
        Assert.Equal(0, new ObservedTrace().WorldProbeResolution);
    }

    /// <summary>PBR legacy aliases and generated properties share one normalized selection.</summary>
    [Fact]
    public void CompositeAliasAndTypedAccessorsAgree()
    {
        // Arrange: Start from the production PBR composite owner's declared defaults.
        var composite = new PBRCompositeShaderProgram();
        // Assert: The typed property exposes its default.
        Assert.True(composite.EnableShortRangeAo);
        // Act: Apply a legacy define.
        composite.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_ENABLE_BENT_NORMAL"] = "0" });
        // Assert: The alias and generated property expose the same normalized setting.
        Assert.False(composite.EnableShortRangeAo);
        // Act: Set the canonical property, then reset the legacy alias.
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
        // Arrange: Seed numeric exploration selections while their controlling features are inactive.
        var mask = new LumOnProbeAtlasPisMaskShaderProgram { ExploreCount = 7, ExploreFraction = 0.4f };
        // Act: Toggle the controlling features and submit a nonfinite value.
        mask.ImportanceSampling = true;
        mask.BatchSlicing = true;
        mask.UniformMask = true;
        // Assert: Inactive numeric choices survive enabling the controlling features.
        Assert.Equal(7, mask.ExploreCount);
        // Act: Disable selectors, then submit a nonfinite weight value.
        mask.BatchSlicing = false;
        mask.UniformMask = false;
        Assert.Equal(7, mask.ExploreCount);
        Assert.Equal(0.4f, mask.ExploreFraction);
        // Assert: Inactive numeric choices persist and the invalid weight is rejected.
        Assert.Throws<ArgumentException>(() => mask.WeightEpsilon = float.PositiveInfinity);
        Assert.Equal(-1, new LumOnProbeAtlasPisMaskShaderProgram().ExploreCount);
    }
    #endregion

    #endregion
    #region Private
    #region Scheduling observation
    /// <summary>Observes the real trace owner's generated setters at the existing scheduling boundary.</summary>
    private sealed class ObservedTrace : LumOnScreenProbeAtlasTraceShaderProgram
    {
        #region Public API
        public int ReloadRequests { get; private set; }
        #endregion

        #region Protected
        /// <summary>Counts preparation requests without initializing a game API.</summary>
        protected override void RequestRecompile() => ReloadRequests++;
        #endregion
    }
    #endregion
    #endregion
}
