using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks the retained camera and material-motion reprojection certificate independently of cloud transport.</summary>
public sealed class CloudTemporalHistoryTests
{
    #region Public API
    /// <summary>Consecutive updates retain identity while lifecycle, camera and content discontinuities reject it.</summary>
    [Theory]
    [InlineData("none")][InlineData("owner")][InlineData("world")][InlineData("allocation")]
    [InlineData("upload")][InlineData("shader")][InlineData("gap")][InlineData("content")]
    [InlineData("resize")][InlineData("cut")][InlineData("missing")][InlineData("calibration")]
    public void AdmitsOnlyCompatibleConsecutiveHistory(string change)
    {
        var history=new CloudReprojectionHistory();var input=Input(10);
        using var prior=Camera(10);using var temporal=new CloudTemporalParameters();history.Commit(input,prior);
        var next=input with{Frame=11,Generation=100,Upload=2};
        if(change=="owner")next=next with{Owner=new object()};
        if(change=="world")next=next with{World=2};
        if(change=="allocation")next=next with{Allocation=2};
        if(change=="upload")next=next with{Upload=0};
        if(change=="shader")next=next with{Shader=2};
        if(change=="gap")next=next with{Frame=13};
        if(change=="missing")history.Invalidate();
        using var camera=Camera(next.Frame,change=="resize"?10:9,change=="cut");
        if(change=="calibration")camera.SetLightingCalibration(VanillaGraphicsExpanded.PBR.LightingCalibration.Default with{BlockReferenceLux=123});
        Assert.Equal(change=="none",history.Capture(next,camera,5,4,temporal,change!="content"));
        Assert.Equal(change=="none"?1:0,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[24]);
        Assert.Equal(change=="none"?1:0,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[26]);
    }
    /// <summary>Grid recentering is not motion; committed and residual wind form a continuous single material translation.</summary>
    [Fact]
    public void ReprojectsCameraAndWindWithoutDoubleRecentering()
    {
        var h=new CloudReprojectionHistory(); var a=Input(10);
        using var old=Camera(10); using var current=Camera(11); using var t=new CloudTemporalParameters();
        h.Commit(a,old);
        var b=a with { Frame=11,Generation=999,Upload=2,Placement=a.Placement with { CenterX=1,Offset=new(50,1,0) } };
        Assert.True(h.Capture(b,current,5,4,t,true));
        var values=MemoryMarshal.Cast<byte,float>(t.Bytes);
        Assert.Equal(0,values[16]); Assert.Equal(-1,values[17]); Assert.Equal(0,values[18]);
        b=b with { Placement=b.Placement with { WindTileX=1,WindX=-49,Offset=new(-1,1,0) } };
        Assert.True(h.Capture(b,current,5,4,t,true));
        Assert.Equal(1,MemoryMarshal.Cast<byte,float>(t.Bytes)[16]);
        Assert.Equal(0,MemoryMarshal.Cast<byte,float>(t.Bytes)[26]); // Wind requires ordinary material reprojection.
    }
    /// <summary>Previous cloud clip coordinates rotate camera-relative points without applying eye translation twice.</summary>
    [Fact]
    public void CapturedTranslatedViewUsesCameraRelativeCloudProjection()
    {
        float[] projection=[.52166903f,0,0,0,0,1.3032254f,0,0,0,0,-1.0000666f,-1,0,0,-.20000666f,0];
        float[] capturedView=[-.931172f,-.012012768f,-.36438215f,0,0,.999457f,-.032949593f,0,
            .36458012f,-.03068174f,-.9306664f,0,0,-1.6982961f,.05598857f,1];
        foreach(bool distant in new[]{false,true})
        {
            var view=(float[])capturedView.Clone();
            if(distant){view[12]=12345.5f;view[13]=-500;view[14]=-9876.25f;}
            float[] inverseProjection=new float[16],inverseView=new float[16],fullProjection=new float[16];
            Mat4f.Invert(inverseProjection,projection);Mat4f.Invert(inverseView,view);Mat4f.Mul(fullProjection,projection,view);
            using var prior=new VgeFrameUniformBuffer();using var current=new VgeFrameUniformBuffer();
            prior.Capture(projection,view,inverseProjection,inverseView,fullProjection,fullProjection,new(3440,1377),0,10,Vector3.Zero,Vector3.Zero,0);
            current.Capture(projection,view,inverseProjection,inverseView,fullProjection,fullProjection,new(3440,1377),0,11,Vector3.Zero,Vector3.Zero,0);
            var history=new CloudReprojectionHistory();var input=Input(10);
            using var temporal=new CloudTemporalParameters();
            history.Commit(input,prior);
            var next=input with {Frame=11,Generation=12};
            Assert.True(history.Capture(next,current,1720,689,temporal,true));
            Assert.Equal(1,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[26]);
            float[] actual=MemoryMarshal.Cast<byte,float>(temporal.Bytes[..64]).ToArray();
            // Build the expected matrix independently from rotation-only view algebra, rather
            // than copying the production matrix's replacement-column implementation.
            var rotation=(float[])view.Clone();rotation[12]=rotation[13]=rotation[14]=0;
            float[] expected=new float[16];Mat4f.Mul(expected,projection,rotation);
            Assert.Equal(expected,actual);
            foreach(var uv in new[]{new Vector2(.15f,.8f),new Vector2(.5f,.5f),new Vector2(.85f,.7f)})
            {
                Vector4 point=Transform(inverseProjection,new(uv*2-Vector2.One,1,1));point/=point.W;
                Vector4 rotated=Transform(inverseView,new(point.X,point.Y,point.Z,0));
                Vector3 direction=Vector3.Normalize(new(rotated.X,rotated.Y,rotated.Z));
                Vector4 clip=Transform(actual,new(direction*1000,1));
                Assert.InRange(Math.Abs(clip.X/clip.W*.5f+.5f-uv.X),0,2e-6f);
                Assert.InRange(Math.Abs(clip.Y/clip.W*.5f+.5f-uv.Y),0,2e-6f);
            }
            // A real camera rotation must not use the exact stationary pixel address.
            var turnedView=(float[])view.Clone();var turnedInverse=new float[16];var turnedProjection=new float[16];
            Mat4f.RotateY(turnedView,turnedView,.001f);Mat4f.Invert(turnedInverse,turnedView);
            Mat4f.Mul(turnedProjection,projection,turnedView);
            using(var turned=new VgeFrameUniformBuffer())
            {
                turned.Capture(projection,turnedView,inverseProjection,turnedInverse,fullProjection,turnedProjection,
                    new(3440,1377),0,11,Vector3.Zero,Vector3.Zero,0);
                Assert.True(history.Capture(next,turned,1720,689,temporal,true));
                Assert.Equal(0,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[26]);
            }
            // Native material movement remains explicit after the camera convention is fixed.
            next=next with {Placement=next.Placement with {CenterX=1,Offset=new(50,1,0)}};
            Assert.True(history.Capture(next,current,1720,689,temporal,true));
            Assert.Equal(new Vector3(0,-1,0),MemoryMarshal.Read<Vector3>(temporal.Bytes.Slice(64,12)));
            Assert.Equal(0,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[26]);
            history.Invalidate();
            Assert.False(history.Capture(next,current,1720,689,temporal,true));
            Assert.Equal(expected,MemoryMarshal.Cast<byte,float>(temporal.Bytes[..64]).ToArray());
        }
    }
    #endregion
    #region Private
    /// <summary>Creates a coherent numerical content certificate without allocating native resources.</summary>
    private static CloudMapInput Input(uint frame)=>new(new object(),1,1,1,1,1,frame,new(1,2,3,4,20,50,5),
        new(0,0,0,0,0,0,0,0,Vector3.Zero,0));
    /// <summary>Authors a complete main-view camera with explicit frame and discontinuity identity.</summary>
    private static VgeFrameUniformBuffer Camera(uint frame,int width=9,bool cut=false)
    {
        var camera=new VgeFrameUniformBuffer(); var identity=Mat4f.Create();
        camera.Capture(identity,identity,identity,identity,identity,identity,new(width,7),0,frame,Vector3.Zero,Vector3.Zero,0,cameraCut:cut);
        return camera;
    }
    /// <summary>Applies column-major shader matrices independently of the history implementation.</summary>
    private static Vector4 Transform(float[] matrix,Vector4 value)=>new(
        matrix[0]*value.X+matrix[4]*value.Y+matrix[8]*value.Z+matrix[12]*value.W,
        matrix[1]*value.X+matrix[5]*value.Y+matrix[9]*value.Z+matrix[13]*value.W,
        matrix[2]*value.X+matrix[6]*value.Y+matrix[10]*value.Z+matrix[14]*value.W,
        matrix[3]*value.X+matrix[7]*value.Y+matrix[11]*value.Z+matrix[15]*value.W);
    #endregion
}
