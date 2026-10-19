using System.Numerics;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks material identity, finite allocation and physical half-float encoding contracts.</summary>
public sealed class CloudLightingVolumeDomainTests
{
    #region Public API
    /// <summary>Native recentering preserves material keys while snapped coverage and dimensions remain bounded.</summary>
    [Fact]
    public void NativePlacementUsesWindOnceAndKeepsStableDimensions()
    {
        var placement = new CloudPlacement(123,-501,0,0,7,-3,.25,.75,new Vector3(12,20,37),0);
        foreach (int width in new[] { 1, 21, 199, 200 })
        {
            var a = new LightingVolumeDomain(width,placement);
            var b = new LightingVolumeDomain(width,placement with { CenterX=124 });
            Assert.Equal(a.Size,b.Size);
            Assert.True(a.Size<=103); Assert.Equal(34,LightingVolumeDomain.Height);
            Assert.InRange(a.AnchorX-100*a.X,0,99.99999);
            var q=a.NativePosition(new Vector3(3,5,7),placement);
            Assert.Equal(new Vector3(3,5,7)-placement.Offset+new Vector3(25*width,0,25*width),q);
            // Opposite changes to native center and wind tile cancel exactly in material identity.
            var same=new LightingVolumeDomain(width,placement with { CenterX=124,WindTileX=8,WindX=90 });
            Assert.Equal(a.X,same.X); Assert.Equal(a.FirstNode,same.FirstNode);
            Assert.True(a.Overlaps(same));
        }
        var extreme=new LightingVolumeDomain(200,placement with {CenterX=int.MaxValue,WindTileX=int.MinValue});
        Assert.InRange(extreme.FirstNode.X,-150,-50);
        Assert.Equal(-1,LightingVolumeDomain.FloorDiv(-1,4));
        Assert.Equal(-2,LightingVolumeDomain.FloorDiv(-5,4));
    }

    /// <summary>Selected formats and all spares fit the declared maximum payload; unsupported contexts reject admission.</summary>
    [Fact]
    public void PayloadAndCapabilityRejectionAreExact()
    {
        var domain=new LightingVolumeDomain(200,default);
        Assert.Equal(360706,domain.Voxels); Assert.Equal(5408,domain.Regions);
        Assert.Equal(10694704,domain.Bytes);
        Assert.Throws<NotSupportedException>(()=>LightingVolumeStorage.Validate(domain,new GraphicsCapabilities()));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LightingVolumeDomain(201,default));
    }

    /// <summary>Encoding retains a power-of-two physical scale with representable reciprocal and monotonic growth.</summary>
    [Fact]
    public void EncodingReservesRangeWithoutFollowingExposure()
    {
        foreach(double cap in new[]{0,1,32752,1e6,1e20,(double)float.MaxValue})
        {
            var encoding=LightingVolumeEncoding.Create(cap);
            Assert.True(float.IsFinite(encoding.Scale)); Assert.True(float.IsFinite(1/encoding.Scale));
            Assert.InRange((double)encoding.Cap/encoding.Scale,0,32752.01);
            Assert.Equal(1.0,Math.Pow(2,Math.Log2(encoding.Scale)%1),8);
            Assert.Equal(encoding,encoding.Grow(0));
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>LightingVolumeEncoding.Create(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(()=>LightingVolumeEncoding.Create(double.MaxValue));
        foreach(float power in new[]{128f,MathF.ScaleB(1,100)})
        {
            Assert.True(new LightingVolumeEncoding(1,power).IsValid);
            Assert.False(new LightingVolumeEncoding(1,MathF.BitIncrement(power)).IsValid);
            Assert.False(new LightingVolumeEncoding(1,MathF.BitDecrement(power)).IsValid);
        }
    }

    /// <summary>Lifetime source bounds retain earlier maxima, use physical source units and reject unrepresentable admission.</summary>
    [Fact]
    public void DiffuseBoundsRetainPhysicalSourceEnvelopesUntilReset()
    {
        var bounds=new LightingVolumeBounds();
        var light=new AtmosphereLighting(Vector3.UnitY,new Vector3(10,2,1),new Vector3(3,4,5),Vector3.Zero,Vector3.Zero,
            ImmutableArray<float>.Empty){Lunar=new Vector3(2),ReconstructionMaximumNits=7};
        float maximum=bounds.Observe(light,3);
        double q=.9*(1+.6)/(4*Math.PI*Math.Pow(1-.6,2))*12;
        double d=.9*(q+5)/.1;
        Assert.InRange(bounds.SourceCap,q,q+1e-5);
        Assert.InRange(bounds.Encoding.Cap,d,d+1e-4);
        Assert.True(maximum>=q+d+10);
        var retained=bounds.Encoding;
        Assert.Equal(maximum,bounds.Observe(light with {Solar=Vector3.Zero,Lunar=Vector3.Zero,Environment=Vector3.Zero},3));
        Assert.Equal(retained,bounds.Encoding);
        Assert.Throws<ArgumentOutOfRangeException>(()=>bounds.Observe(light with {Solar=new Vector3(float.MaxValue)}));
        Assert.Equal(retained,bounds.Encoding);
        bounds.Reset();bounds.Observe(light with {Solar=Vector3.Zero,Lunar=Vector3.Zero,Environment=Vector3.Zero});
        Assert.Equal(0,bounds.Encoding.Cap);Assert.Equal(0,bounds.SourceCap);
    }
    #endregion
}
