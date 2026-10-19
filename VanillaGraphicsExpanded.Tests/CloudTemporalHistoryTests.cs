using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks semantic history admission, material advection and bounded persisted default/reference controls.</summary>
public sealed class CloudTemporalHistoryTests
{
    #region Public API
    #region Shader contracts
    /// <summary>Cloud clipping uses exact shared hierarchy mip zero in both trace/reference transport and reconstruction.</summary>
    [Fact]
    public void SharedDepthClippingUsesExplicitMipZero()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"assets/shaders");
        string depth=File.ReadAllText(Path.Combine(root,"includes/cloud_depth.glsl"));
        Assert.Contains("textureLod(sceneDepth,uv,0.0)",depth);
        Assert.Contains("textureLod(visibilityDepth,uv,0.0)",depth);
        foreach(string path in new[]{"includes/cloud_volume_transport.glsl","cloud_volume_resolve.fsh"})
        {
            string source=File.ReadAllText(Path.Combine(root,path));
            Assert.Contains("cloud_depth.glsl",source);
            Assert.Contains("CloudOpaqueDepth(uv)",source);
            Assert.Contains("textureLod(liquidDepth,uv,0.0)",source);
            Assert.DoesNotContain("texture(sceneDepth",source);
        }
    }
    /// <summary>Full-view reconstruction consumes reduced payloads without importing a density or lighting march.</summary>
    [Fact]
    public void FullViewResolveCannotRetraceCloudTransport()
    {
        string source=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/shaders/cloud_volume_resolve.fsh"));
        Assert.DoesNotContain("CloudVisibility",source);
        Assert.DoesNotContain("CloudIntegrate",source);
        Assert.DoesNotContain("CloudLightDepth",source);
        Assert.DoesNotContain("cloudMap",source);
        Assert.DoesNotContain("cloud_volume_transport.glsl",source);
        Assert.DoesNotContain("cloud_volume_visibility.glsl",source);
        Assert.DoesNotContain("cloud_volume_density.glsl",source);
    }
    #endregion
    #region History admission
    /// <summary>Routine atmosphere publication and continuous local-medium evolution preserve consecutive cloud history.</summary>
    [Fact]
    public void ContinuousAtmosphereUpdatesPreserveHistory()
    {
        var history=new CloudTemporalHistory();var input=Input(10);var lighting=Lighting();
        using var parameters=new CloudVolumeParameters();using var temporal=new CloudTemporalParameters();
        parameters.CaptureMap(20,input.Placement,1,CloudVolumeSettings.Default);
        parameters.CaptureLighting(lighting,0,Vector4.Zero,Vector3.Zero);
        using var prior=Camera(10);history.Commit(input,prior,CloudVolumeSettings.Default,parameters);
        // Fresh bundles and smoothly changing fog, altitude, colors and directions occur during
        // normal play. They must reach the GPU's bounded temporal filter without a CPU reset.
        foreach(float underwater in new[]{0f,.6f,.7f})
        {
            input=input with {Frame=input.Frame+1,Generation=input.Generation+1,Upload=input.Upload+1};
            lighting=lighting with {Solar=lighting.Solar+new Vector3(2),Lunar=new(.01f),
                Environment=lighting.Environment+new Vector3(.1f),Sun=Vector3.Normalize(new(.1f,1,.2f)),
                Moon=Vector3.Normalize(new(-.2f,-1,.1f)),Altitude=lighting.Altitude+.01f,
                HorizonElevation=lighting.HorizonElevation+.001f,Horizon=lighting.Horizon+new Vector3(.02f)};
            parameters.CaptureLighting(lighting,underwater,new(.1f,.2f,.3f,.001f*input.Frame),new(.4f,.3f,.2f));
            using var camera=Camera(input.Frame);
            Assert.False(parameters.UsesUnderwaterTransport);
            Assert.True(history.Capture(input,camera,CloudVolumeSettings.Default,parameters,5,4,temporal));
            Assert.Equal(1,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[24]);
            history.Commit(input,camera,CloudVolumeSettings.Default,parameters);
        }
    }
    /// <summary>Reusable handles cannot carry history across independent content, lifecycle or view changes.</summary>
    [Theory]
    [InlineData("owner")][InlineData("world")][InlineData("allocation")][InlineData("upload")][InlineData("shader")]
    [InlineData("gap")][InlineData("quality")][InlineData("medium")]
    [InlineData("primary-max")][InlineData("primary-min")][InlineData("primary-distance")]
    [InlineData("light-max")][InlineData("shadow-distance")][InlineData("termination")][InlineData("trace-scale")]
    [InlineData("resize")][InlineData("cut")][InlineData("missing")][InlineData("calibration")]
    public void RejectsIncompatibleHistory(string change)
    {
        var h=new CloudTemporalHistory(); var light=Lighting(); var a=Input(10); var b=a with { Frame=11,Generation=12 };
        using var parameters=new CloudVolumeParameters(); parameters.CaptureMap(20,a.Placement,1,CloudVolumeSettings.Default);
        parameters.CaptureLighting(light,0,Vector4.Zero,Vector3.Zero);
        using var prior=Camera(10); using var temporal=new CloudTemporalParameters();
        h.Commit(a,prior,CloudVolumeSettings.Default,parameters);
        if(change=="owner") b=b with { Owner=new object() };
        if(change=="world") b=b with { World=2 }; if(change=="allocation") b=b with { Allocation=2 };
        if(change=="upload") b=b with { Upload=0 }; if(change=="shader") b=b with { Shader=2 };
        if(change=="gap") b=b with { Frame=13 };
        var quality=change=="quality"?CloudVolumeSettings.Default with { DetailAmplitude=.2f }:CloudVolumeSettings.Default;
        if(change=="primary-max") quality=quality with { ViewRaySampleMax=192 };
        if(change=="primary-min") quality=quality with { ViewRaySampleMin=3 };
        if(change=="primary-distance") quality=quality with { ViewBudgetDistance=1500 };
        if(change=="light-max") quality=quality with { LightRaySampleMax=12 };
        if(change=="shadow-distance") quality=quality with { ShadowDistance=3000 };
        if(change=="termination") quality=quality with { StopTransmittance=0 };
        if(change=="trace-scale") quality=quality with {TraceScale=CloudTraceScale.Half};
        if(change=="medium") parameters.CaptureLighting(light,1,new(.1f,0,0,0),Vector3.Zero);
        if(change=="missing") h.Invalidate();
        using var current=Camera(b.Frame,change=="resize"?10:9,change=="cut");
        if(change=="calibration") current.SetLightingCalibration(LightingCalibration.Default with { BlockReferenceLux=123 });
        Assert.False(h.Capture(b,current,quality,parameters,5,4,temporal));
        Assert.Equal(0,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[24]);
    }
    /// <summary>Grid recentering is not motion; committed and residual wind form a continuous single material translation.</summary>
    [Fact]
    public void ReprojectsCameraAndWindWithoutDoubleRecentering()
    {
        var h=new CloudTemporalHistory(); var light=Lighting(); var a=Input(10);
        using var p=new CloudVolumeParameters(); p.CaptureMap(20,a.Placement,1,CloudVolumeSettings.Default); p.CaptureLighting(light,0,Vector4.Zero,Vector3.Zero);
        using var old=Camera(10); using var current=Camera(11); using var t=new CloudTemporalParameters();
        h.Commit(a,old,CloudVolumeSettings.Default,p);
        var b=a with { Frame=11,Generation=999,Upload=2,Placement=a.Placement with { CenterX=1,Offset=new(50,1,0) } };
        Assert.True(h.Capture(b,current,CloudVolumeSettings.Default,p,5,4,t));
        var values=MemoryMarshal.Cast<byte,float>(t.Bytes);
        Assert.Equal(0,values[16]); Assert.Equal(-1,values[17]); Assert.Equal(0,values[18]);
        b=b with { Placement=b.Placement with { WindTileX=1,WindX=-49,Offset=new(-1,1,0) } };
        Assert.True(h.Capture(b,current,CloudVolumeSettings.Default,p,5,4,t));
        Assert.Equal(1,MemoryMarshal.Cast<byte,float>(t.Bytes)[16]);
        Assert.Equal(1,MemoryMarshal.Cast<byte,float>(t.Bytes)[26]); // prior phase y=(10>>1)&1
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
            var history=new CloudTemporalHistory();var light=Lighting();var input=Input(10);
            using var parameters=new CloudVolumeParameters();using var temporal=new CloudTemporalParameters();
            parameters.CaptureMap(20,input.Placement,1,CloudVolumeSettings.Default);parameters.CaptureLighting(light,0,Vector4.Zero,Vector3.Zero);
            history.Commit(input,prior,CloudVolumeSettings.Default,parameters);
            var next=input with {Frame=11,Generation=12};
            Assert.True(history.Capture(next,current,CloudVolumeSettings.Default,parameters,1720,689,temporal));
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
            // Native material movement remains explicit after the camera convention is fixed.
            next=next with {Placement=next.Placement with {CenterX=1,Offset=new(50,1,0)}};
            Assert.True(history.Capture(next,current,CloudVolumeSettings.Default,parameters,1720,689,temporal));
            Assert.Equal(new Vector3(0,-1,0),MemoryMarshal.Read<Vector3>(temporal.Bytes.Slice(64,12)));
            history.Invalidate();
            Assert.False(history.Capture(next,current,CloudVolumeSettings.Default,parameters,1720,689,temporal));
            Assert.Equal(expected,MemoryMarshal.Cast<byte,float>(temporal.Bytes[..64]).ToArray());
        }
    }
    #endregion
    #region Settings
    /// <summary>Ceil-half dimensions cover odd views and reject excessive storage before GPU allocation.</summary>
    [Fact]
    public void BoundsHistoryStorageAndPersistsApprovedDefault()
    {
        Assert.Equal((5,4),CloudVolumeStorage.Dimensions(9,7)); Assert.Equal((1,1),CloudVolumeStorage.Dimensions(1,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CloudVolumeStorage.Dimensions(0,7));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CloudVolumeStorage.Dimensions(16384,16384));
        var config=JsonConvert.DeserializeObject<VgeConfig>("{\"Clouds\":{\"ViewRaySampleMax\":999,\"LightRaySampleMax\":0,\"DetailAmplitude\":100}}")!;
        config.Sanitize(); Assert.False(config.Clouds.FullResolutionReference);
        var quality=config.Clouds.Capture(); Assert.Equal(384,quality.ViewRaySampleMax); Assert.Equal(2,quality.LightRaySampleMax); Assert.Equal(.95f,quality.DetailAmplitude);
        config.Clouds.FullResolutionReference=true;
        Assert.True(JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!.Clouds.FullResolutionReference);
        config.Clouds.EdgeWidth=float.NaN; config.Clouds.NoiseScale=float.PositiveInfinity;
        config.Clouds.Capture().Validate(); Assert.Equal(2,config.Clouds.EdgeWidth); Assert.Equal(.08f,config.Clouds.NoiseScale);
    }
    #endregion
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
    /// <summary>Supplies an immutable completed physical atmosphere for history-identity checks.</summary>
    private static AtmosphereLighting Lighting()=>new(Vector3.UnitY,new(10),new(1),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
    { Moon=-Vector3.UnitY,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale };
    /// <summary>Applies column-major shader matrices independently of the history implementation.</summary>
    private static Vector4 Transform(float[] matrix,Vector4 value)=>new(
        matrix[0]*value.X+matrix[4]*value.Y+matrix[8]*value.Z+matrix[12]*value.W,
        matrix[1]*value.X+matrix[5]*value.Y+matrix[9]*value.Z+matrix[13]*value.W,
        matrix[2]*value.X+matrix[6]*value.Y+matrix[10]*value.Z+matrix[14]*value.W,
        matrix[3]*value.X+matrix[7]*value.Y+matrix[11]*value.Z+matrix[15]*value.W);
    #endregion
}
