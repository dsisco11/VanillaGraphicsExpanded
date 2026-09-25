using System.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks per-direction publication validity independently of probe-level metadata.</summary>
public sealed partial class LumOnDirectWorldProbeVisibilityTests
{
    #region Directional readiness
    /// <summary>Unpublished angular texels contribute neither bright stale RGB nor confidence in real consumers.</summary>
    [Theory]
    [InlineData(31)] [InlineData(32)] [InlineData(33)] [InlineData(-1)] [InlineData(-2)]
    public void UnpublishedDirectionsRejectStaleColorAndConfidence(int consumer)
    {
        EnsureShaderTestAvailable();
        using var local=new NearFieldVoxelFixture();local.Publish(new ControlledVoxelWorld());
        var atlas=CreateUniformCache();
        for(int i=3;i<atlas.Radiance.Length;i+=4) atlas.Radiance[i]=0;
        var result=RenderDirectVisibility(atlas,local.Scene,new Vector3(.5f,.5f,-5),new Vector3(-7.5f),16,consumer);
        AssertLighting(result,consumer,false);
    }

    /// <summary>Resolved darkness remains available to confidence consumers instead of being mistaken for missing data.</summary>
    [Fact]
    public void ValidBlackDirectionsRetainConfidence()
    {
        EnsureShaderTestAvailable();
        using var local=new NearFieldVoxelFixture();local.Publish(new ControlledVoxelWorld());
        var atlas=CreateUniformCache();
        for(int i=0;i<atlas.Radiance.Length;i++) if((i&3)!=3) atlas.Radiance[i]=0;
        AssertLighting(RenderDirectVisibility(atlas,local.Scene,new Vector3(.5f,.5f,-5),new Vector3(-7.5f),16,33),33,true);
        AssertLighting(RenderDirectVisibility(atlas,local.Scene,new Vector3(.5f,.5f,-5),new Vector3(-7.5f),16,31),31,false);
    }
    #endregion
}
