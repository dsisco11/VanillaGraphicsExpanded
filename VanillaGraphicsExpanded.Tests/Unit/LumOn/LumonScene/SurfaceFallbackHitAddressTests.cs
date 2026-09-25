using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Checks delayed cache-hit addressing independently of floating world positions.</summary>
public sealed class SurfaceFallbackHitAddressTests
{
    #region Address mapping
    /// <summary>Every signed axis preserves its four-block patch identity across positive and negative chunks.</summary>
    [Theory]
    [InlineData(1,0,0,8335)] [InlineData(-1,0,0,8336)]
    [InlineData(0,1,0,8721)] [InlineData(0,-1,0,8722)]
    [InlineData(0,0,1,9107)] [InlineData(0,0,-1,9108)]
    public void SignedChunkCoordinatesPreserveAllFaceAddresses(int nx,int ny,int nz,uint expected)
    {
        foreach(int sign in new[]{-1,1})
        {
            var origin=new VectorInt3(sign*64,sign*32,sign*96);
            var query=new SurfaceLightingQuery(new(origin.X+21,origin.Y+22,origin.Z+23),new(nx,ny,nz),Vector3.Zero);
            Assert.True(SurfaceFallbackHitAddress.TryResolve(query,out var chunk,out uint patch));
            Assert.Equal(new VectorInt3(sign*2,sign,sign*3),chunk);Assert.Equal(expected,patch);
        }
    }

    /// <summary>Nonaxial or invalid normals cannot become a cache lifetime ticket.</summary>
    [Theory]
    [InlineData(0,0,0)] [InlineData(1,1,0)] [InlineData(int.MinValue,0,0)]
    public void InvalidNormalsAreRejected(int nx,int ny,int nz)
    {
        var query=new SurfaceLightingQuery(default,new(nx,ny,nz),Vector3.Zero);
        Assert.False(SurfaceFallbackHitAddress.TryResolve(query,out _,out _));
    }
    #endregion
}
