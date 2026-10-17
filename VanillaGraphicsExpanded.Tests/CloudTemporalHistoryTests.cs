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
    /// <summary>Reusable handles cannot carry history across independent content, lifecycle or view changes.</summary>
    [Theory]
    [InlineData("owner")][InlineData("world")][InlineData("allocation")][InlineData("upload")][InlineData("shader")]
    [InlineData("gap")][InlineData("lighting")][InlineData("quality")][InlineData("medium")]
    [InlineData("resize")][InlineData("cut")][InlineData("missing")][InlineData("calibration")]
    public void RejectsIncompatibleHistory(string change)
    {
        var h=new CloudTemporalHistory(); var light=Lighting(); var a=Input(10); var b=a with { Frame=11,Generation=12 };
        using var parameters=new CloudVolumeParameters(); parameters.CaptureMap(20,a.Placement,1,CloudVolumeSettings.Default);
        parameters.CaptureLighting(light,0,Vector4.Zero,Vector3.Zero);
        using var prior=Camera(10); using var temporal=new CloudTemporalParameters();
        h.Commit(a,prior,CloudVolumeSettings.Default,light,parameters);
        if(change=="owner") b=b with { Owner=new object() };
        if(change=="world") b=b with { World=2 }; if(change=="allocation") b=b with { Allocation=2 };
        if(change=="upload") b=b with { Upload=0 }; if(change=="shader") b=b with { Shader=2 };
        if(change=="gap") b=b with { Frame=13 }; if(change=="lighting") light=light with { Solar=new(20) };
        var quality=change=="quality"?CloudVolumeSettings.Default with { ViewSamples=8 }:CloudVolumeSettings.Default;
        if(change=="medium") parameters.CaptureLighting(light,1,new(.1f,0,0,0),Vector3.Zero);
        if(change=="missing") h.Invalidate();
        using var current=Camera(b.Frame,change=="resize"?10:9,change=="cut");
        if(change=="calibration") current.SetLightingCalibration(LightingCalibration.Default with { BlockReferenceLux=123 });
        Assert.False(h.Capture(b,current,quality,light,parameters,5,4,temporal));
        Assert.Equal(0,MemoryMarshal.Cast<byte,float>(temporal.Bytes)[24]);
    }
    /// <summary>Grid recentering is not motion; committed and residual wind form a continuous single material translation.</summary>
    [Fact]
    public void ReprojectsCameraAndWindWithoutDoubleRecentering()
    {
        var h=new CloudTemporalHistory(); var light=Lighting(); var a=Input(10);
        using var p=new CloudVolumeParameters(); p.CaptureMap(20,a.Placement,1,CloudVolumeSettings.Default); p.CaptureLighting(light,0,Vector4.Zero,Vector3.Zero);
        using var old=Camera(10); using var current=Camera(11); using var t=new CloudTemporalParameters();
        h.Commit(a,old,CloudVolumeSettings.Default,light,p);
        var b=a with { Frame=11,Generation=999,Upload=2,Placement=a.Placement with { CenterX=1,Offset=new(50,1,0) } };
        Assert.True(h.Capture(b,current,CloudVolumeSettings.Default,light,p,5,4,t));
        var values=MemoryMarshal.Cast<byte,float>(t.Bytes);
        Assert.Equal(0,values[16]); Assert.Equal(-1,values[17]); Assert.Equal(0,values[18]);
        b=b with { Placement=b.Placement with { WindTileX=1,WindX=-49,Offset=new(-1,1,0) } };
        Assert.True(h.Capture(b,current,CloudVolumeSettings.Default,light,p,5,4,t));
        Assert.Equal(1,MemoryMarshal.Cast<byte,float>(t.Bytes)[16]);
        Assert.Equal(1,MemoryMarshal.Cast<byte,float>(t.Bytes)[26]); // prior phase y=(10>>1)&1
    }
    /// <summary>Ceil-half dimensions cover odd views and reject excessive storage before GPU allocation.</summary>
    [Fact]
    public void BoundsHistoryStorageAndPersistsApprovedDefault()
    {
        Assert.Equal((5,4),CloudVolumeSurface.Dimensions(9,7)); Assert.Equal((1,1),CloudVolumeSurface.Dimensions(1,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CloudVolumeSurface.Dimensions(0,7));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CloudVolumeSurface.Dimensions(16384,16384));
        var config=JsonConvert.DeserializeObject<VgeConfig>("{\"Clouds\":{\"ViewSamples\":999,\"LightSamples\":0,\"DetailAmplitude\":100}}")!;
        config.Sanitize(); Assert.False(config.Clouds.FullResolutionReference);
        var quality=config.Clouds.Capture(); Assert.Equal(16,quality.ViewSamples); Assert.Equal(1,quality.LightSamples); Assert.Equal(.95f,quality.DetailAmplitude);
        config.Clouds.FullResolutionReference=true;
        Assert.True(JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!.Clouds.FullResolutionReference);
        config.Clouds.EdgeWidth=float.NaN; config.Clouds.NoiseScale=float.PositiveInfinity;
        config.Clouds.Capture().Validate(); Assert.Equal(2,config.Clouds.EdgeWidth); Assert.Equal(.08f,config.Clouds.NoiseScale);
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
    /// <summary>Supplies an immutable completed physical atmosphere for history-identity checks.</summary>
    private static AtmosphereLighting Lighting()=>new(Vector3.UnitY,new(10),new(1),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
    { Moon=-Vector3.UnitY,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale };
    #endregion
}
