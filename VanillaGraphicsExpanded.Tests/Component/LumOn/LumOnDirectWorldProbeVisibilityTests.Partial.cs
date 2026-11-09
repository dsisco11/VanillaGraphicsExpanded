using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using System.Numerics;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks per-direction publication validity independently of probe-level metadata.</summary>
public sealed partial class LumOnDirectWorldProbeVisibilityTests
{

    #region Public API
    #region Directional readiness
    /// <summary>Unpublished angular texels contribute neither bright stale RGB nor confidence in real consumers.</summary>
    [Theory]
    [InlineData(31)] [InlineData(-1)] [InlineData(-2)]
    public void UnpublishedDirectionsRejectStaleColorAndConfidence(int consumer)
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var local=new NearFieldVoxelFixture();local.Publish(new ControlledVoxelWorld());
        var atlas=CreateUniformCache();
        for(int i=3;i<atlas.Radiance.Length;i+=4) atlas.Radiance[i]=0;
        // Act
        using var result = RenderDirectVisibility(atlas,local.Scene,new Vector3(.5f,.5f,-5),new Vector3(-7.5f),16,consumer);
        // Assert
        AssertLighting(result,consumer,false);
    }

    /// <summary>Resolved darkness remains available to confidence consumers instead of being mistaken for missing data.</summary>
    [Fact]
    public void ValidBlackDirectionsRetainConfidence()
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var local=new NearFieldVoxelFixture();local.Publish(new ControlledVoxelWorld());
        var atlas=CreateUniformCache();
        for(int i=0;i<atlas.Radiance.Length;i++) if((i&3)!=3) atlas.Radiance[i]=0;
        // Act
        using var confidence = RenderDirectVisibility(atlas,local.Scene,new Vector3(.5f,.5f,-5),new Vector3(-7.5f),16,33);
        // Assert
        AssertLighting(confidence,33,true);
        // Act
        using var darkness = RenderDirectVisibility(atlas,local.Scene,new Vector3(.5f,.5f,-5),new Vector3(-7.5f),16,31);
        // Assert
        AssertLighting(darkness,31,false);
    }
    #endregion
    #endregion

}
