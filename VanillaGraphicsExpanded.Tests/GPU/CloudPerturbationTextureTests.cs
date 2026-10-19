using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual hardware volume filtering, normalized octave combination and cloud-owned retirement.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudPerturbationTextureTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Authored negative, boundary and fractional coordinates match independent wrapped trilinear arithmetic.</summary>
    [Fact]
    public void FilteredOctavesWrapAndResourceLifetimeIsStable()
    {
        EnsureShaderTestAvailable();
        using var probe=new CloudTraversalProbe(Programs.Api);
        using var map=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgba32f);
        using var volume=new CloudVolumeParameters();
        volume.CaptureMap(1,default,1,CloudVolumeSettings.Default);
        byte[] bytes=CloudPerturbationAsset.Load().Pixels;
        // Half-node fractions are exactly representable in the hardware interpolation clock;
        // arbitrary fractions also involve implementation-dependent fixed-point filter rounding.
        foreach(Vector3 material in new[]{Vector3.Zero,new Vector3(-.78125f,99.21875f,100.78125f),new Vector3(100,0,-100),new Vector3(12800,0,-12800)})
        {
            Vector4[] result=probe.Run(volume,map,material,Vector3.UnitX,0,21);
            Vector4 expected=Sample(bytes,material/100f*64f);
            for(int c=0;c<4;c++)Assert.InRange(Math.Abs(result[0][c]-expected[c]),0,2e-4f);
            Assert.InRange(Math.Abs(result[402].X-Vector4.Dot(expected,new Vector4(8,4,2,1)/15)),0,2e-4f);
            Assert.InRange(result[402].X,0,1);Assert.Equal(128,result[402].Y);
        }
        using var owner=new CloudVolumeReconstruction(Moq.Mock.Of<Vintagestory.API.Client.ICoreClientAPI>());
        var first=owner.PreparePerturbation();Assert.Same(first,owner.PreparePerturbation());
        Assert.Equal((64,64,64),(first.Width,first.Height,first.Depth));
        Assert.Equal(PixelInternalFormat.Rgba8,first.InternalFormat);Assert.Equal(1,first.StorageMipLevels);
        owner.Reset();Assert.False(first.IsValid);
        var second=owner.PreparePerturbation();Assert.NotSame(first,second);Assert.True(second.IsValid);
        owner.Dispose();Assert.False(second.IsValid);
        Assert.Throws<ObjectDisposedException>(()=>owner.PreparePerturbation());
    }
    #endregion
    #region Private
    /// <summary>Interpolates the declared byte nodes without sampling the GPU or invoking production coordinate helpers.</summary>
    private static Vector4 Sample(byte[] bytes,Vector3 point)
    {
        int x=(int)MathF.Floor(point.X),y=(int)MathF.Floor(point.Y),z=(int)MathF.Floor(point.Z);
        Vector3 f=point-new Vector3(x,y,z);Vector4 value=Vector4.Zero;
        // Each channel is an independent field. Hardware interpolation must wrap spatial
        // neighbors only, with no interpolation into another logical octave.
        for(int dz=0;dz<2;dz++)for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
        {
            int index=((((z+dz)&63)*64+((y+dy)&63))*64+((x+dx)&63))*4;
            float weight=(dx==0?1-f.X:f.X)*(dy==0?1-f.Y:f.Y)*(dz==0?1-f.Z:f.Z)/255f;
            value+=new Vector4(bytes[index],bytes[index+1],bytes[index+2],bytes[index+3])*weight;
        }
        return value;
    }
    #endregion
}
