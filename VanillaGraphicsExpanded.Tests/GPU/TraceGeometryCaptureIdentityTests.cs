using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks bounded capture identity storage against signed world coordinates and source publication lifetime.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class TraceGeometryCaptureIdentityTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Published capture identity
    /// <summary>All face axes use the owning source voxels, ignore lighting bytes, and reject withdrawn or out-of-domain cells.</summary>
    [Fact]
    public void SignedPatchAxesRetainOnlyPublishedGeometry()
    {
        EnsureContextValid();
        var plan=TraceGeometryCoverage.Plan(new(-16,16,-16),false,64,256);
        using var geometry=new SharedTraceGeometryFixture(plan,new TraceGeometryMaterials(),
            (x,y,z)=>new(Word(x,y,z),17,23));
        geometry.Publish();
        var chunk=new VectorInt3(-1,0,-1);
        var expected=new uint[16];
        var actual=new uint[16];
        // Plane seven, U patch one and V patch two identify disjoint coordinates for each axis.
        const uint firstPatch=1+6*((7<<6)+(2<<3)+1);
        for(uint axis=0;axis<6;axis++)
        {
            for(int v=0;v<4;v++) for(int u=0;u<4;u++)
            {
                var point=axis switch
                {
                    0 or 1 =>new VectorInt3(-25,8+v,-28+u),
                    2 or 3 =>new VectorInt3(-28+u,7,-24+v),
                    _ =>new VectorInt3(-28+u,8+v,-25)
                };
                expected[(v<<2)+u]=Word(point.X,point.Y,point.Z);
            }
            Assert.True(geometry.Scene.TryReadCaptureIdentity(chunk,firstPatch+axis,actual));
            Assert.Equal(expected,actual);
        }
        long bytes=geometry.Scene.CaptureIdentityBytes;
        Assert.InRange(bytes,1,((long)geometry.Scene.Resolution*geometry.Scene.Resolution*geometry.Scene.Resolution)<<2);
        Assert.False(geometry.Scene.TryReadCaptureIdentity(new(-4,0,-1),firstPatch,actual));
        Assert.False(geometry.Scene.TryReadCaptureIdentity(chunk,0,actual));
        Assert.False(geometry.Scene.TryReadCaptureIdentity(chunk,12289,actual));
        Assert.False(geometry.Scene.TryReadCaptureIdentity(chunk,firstPatch,new uint[15]));
        geometry.Sample=(x,y,z)=>new(Word(x,y,z),91,127);
        geometry.Dirty();
        Assert.False(geometry.Scene.TryReadCaptureIdentity(chunk,firstPatch,actual));
        geometry.Publish();
        Assert.True(geometry.Scene.TryReadCaptureIdentity(chunk,firstPatch+5,actual));
        Assert.Equal(expected,actual);
        Assert.Equal(bytes,geometry.Scene.CaptureIdentityBytes);
    }

    /// <summary>Encodes distinct geometry identities without involving light payloads.</summary>
    private static uint Word(int x,int y,int z) => 1u|((uint)((x&31)|((y&31)<<5)|((z&31)<<10))<<2);
    #endregion
}
