using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Isolates cached outgoing-radiance hit semantics; it does not assemble downstream lighting passes.</summary>
public abstract class SurfaceLightingHitTestBase : NearFieldShaderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    protected SurfaceLightingHitTestBase(HeadlessGLFixture fixture) : base(fixture) { }

#region Hit execution
    /// <summary>Provides fixed rays for precise numerical and validity component assertions.</summary>
    private protected TraceImages RenderHits(SurfaceLightingEnclosureFixture room)
    {
        using var placeholder = new NearFieldVoxelFixture();
        // Retained GPU observations keep the encoded attachment; predicates decode only their RGB inputs.
        var result = Trace(placeholder, worldCache:false, shared:room.Geometry.Scene, surfaceLighting:room.Snapshot,
            anchorPosition:new(0,0,-3), worldOffset:new(0,32,0), matrixRemainder:new(4,4,7));
        return result;
    }
    #endregion

    #region Observations
    /// <summary>Checks finite RGB and exact darkness at a component boundary.</summary>
    private protected float AssertEnergy(TraceImages pixels,bool lit,string boundary)
    {
        var observation=ImageAssertions.Inspect(pixels.Radiance, channelMask:7);
        Assert.True(observation.NonFinite==0,boundary+": "+observation);
        float maximum=Math.Max(0,(float)observation.Maximum)*pixels.RgbDecode;
        if(!lit) ImageAssertions.Inspect(pixels.Radiance,-.0001f/pixels.RgbDecode,.0001f/pixels.RgbDecode,
            channelMask:7).AssertFiniteRange();
        if(lit) Assert.True(maximum>.001f,$"{boundary}: expected lighting, maximum={maximum}");
        return maximum;
    }

    /// <summary>Checks the isolated hit boundary without claiming gather or final-image coverage.</summary>
    private protected void AssertHits(TraceImages pixels,bool lit)
    {
        AssertEnergy(pixels,lit,"hit radiance");
        ImageAssertions.Inspect(pixels.Meta,0,1,channelMask:1).AssertFiniteRange();
    }
    #endregion
}
