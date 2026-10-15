using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Isolates cached outgoing-radiance hit semantics; it does not assemble downstream lighting passes.</summary>
public abstract class SurfaceLightingHitTestBase : NearFieldShaderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    protected SurfaceLightingHitTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Observes only the hit consumer's directional radiance and confidence.</summary>
    protected sealed record HitPixels(float[] Trace, float[] Meta);

    #region Hit execution
    /// <summary>Provides fixed rays for precise numerical and validity component assertions.</summary>
    private protected HitPixels RenderHits(SurfaceLightingEnclosureFixture room)
    {
        using var placeholder = new NearFieldVoxelFixture();
        // Trace decodes its returned RGB after any encoded attachment consumers have run.
        var result = Trace(placeholder, worldCache:false, shared:room.Geometry.Scene, surfaceLighting:room.Snapshot,
            anchorPosition:new(0,0,-3), worldOffset:new(0,32,0), matrixRemainder:new(4,4,7));
        return new(result.Radiance, result.Meta);
    }
    #endregion

    #region Observations
    /// <summary>Checks finite RGB and exact darkness at a component boundary.</summary>
    protected static float AssertEnergy(float[] pixels,bool lit,string boundary)
    {
        Assert.NotEmpty(pixels);
        float maximum=0;
        for(int i=0;i<pixels.Length;i+=4) for(int c=0;c<3;c++)
        {
            Assert.True(float.IsFinite(pixels[i+c]),boundary+": nonfinite RGB");
            maximum=Math.Max(maximum,pixels[i+c]);
            if(!lit) Assert.InRange(Math.Abs(pixels[i+c]),0,.0001f);
        }
        if(lit) Assert.True(maximum>.001f,$"{boundary}: expected lighting, maximum={maximum}");
        return maximum;
    }

    /// <summary>Checks the isolated hit boundary without claiming gather or final-image coverage.</summary>
    protected static void AssertHits(HitPixels pixels,bool lit)
    {
        AssertEnergy(pixels.Trace,lit,"hit radiance");
        for (int i=0;i<pixels.Meta.Length;i+=2) Assert.InRange(pixels.Meta[i],0f,1f);
    }
    #endregion
}
