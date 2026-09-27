using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares neutral displacement with the engine's biased terrain depth interpolation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainDisplacementDepthBiasTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Neutral depth identity
    /// <summary>Neutral height should not change an inclined receiver's depth merely because subdivision is enabled.</summary>
    [Fact]
    public void NeutralHeightPreservesBiasedDepthAcrossInclinedTriangle()
    {
        EnsureContextValid();
        using var scope=new TerrainDetailWorkload(depthBias:true);
        scope.Select("triangles");scope.Draw();float[] baseline=scope.Read();
        scope.Select("adaptiveNeutral");scope.Draw();float[] tessellated=scope.Read();
        float difference=0;
        // Interior samples exclude edge-coverage changes and isolate interpolated depth.
        for(int y=16;y<240;y++)for(int x=16;x<240;x++)
        {
            int index=(y*256+x)*4;
            difference=Math.Max(difference,Math.Abs(baseline[index]-tessellated[index]));
        }
        Assert.True(difference<.000001f,$"Neutral tessellation changed interior biased depth by {difference:R}.");
    }
    #endregion
}
