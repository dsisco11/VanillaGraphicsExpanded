using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks selected direct quadrature, independent channels and ordered publication through actual GPU programs.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudLightingVolumeDirectTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Transmission follows the declared finite schedule, ignores light intensity and preserves diffuse/mask payloads.</summary>
    [Fact]
    public void SelectedSourcesMatchUniformMidpointsAndPreserveOtherChannels()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var work=new LightingVolumeWork(op=>LightingVolumeWorkProgram.Create(Programs.Api,op));
        using var direct=new LightingVolumeDirect(()=>LightingVolumeDirectProgram.Create(Programs.Api,false));
        using var pixels=new LightingVolumeReadback();
        using var map=DynamicTexture2D.Create(40,40,PixelInternalFormat.Rgba32f);
        float[] controls=MapData(40);map.UploadDataImmediate(controls);
        var input=Input(map,1);
        var lighting=Lighting(Vector3.UnitX*2,Vector3.UnitY*3,new Vector3(10),new Vector3(2));
        storage.Ensure(input,LightingVolumeEncoding.Create(100),()=>true);
        Assert.True(work.Update(storage,input,map,lighting,new Vector4[6],()=>true));
        Assert.False(direct.Update(storage,work,input with {Generation=99},map,()=>true));
        var nodes=new[]{(x:1,y:16,z:1),(x:6,y:17,z:6),(x:2,y:32,z:2),(x:8,y:16,z:2)};
        foreach(var p in nodes) storage.Diffuse.UploadDataImmediate(new float[]{3,4,5,9},p.x,p.y,p.z,1,1,1);
        Assert.True(direct.Update(storage,work,input,map,()=>true));
        Vector4[] retained=new Vector4[nodes.Length];
        for(int i=0;i<nodes.Length;i++)
        {
            var p=nodes[i];Vector3 q=storage.Domain!.FirstNode+new Vector3(p.x*100,p.y*25,p.z*100);
            Vector4 value=pixels.Node(storage.Direct,p.x,p.y,p.z);retained[i]=value;
            // This compares the specified 32-point finite algorithm, never a denser approximation oracle.
            AssertStoredTransmission(Expected(controls,40,q,Vector3.UnitX),value.X);
            AssertStoredTransmission(Expected(controls,40,q,Vector3.UnitY),value.Y);
            Assert.InRange(value.X,0,1);Assert.InRange(value.Y,0,1);
            Assert.Equal(new Vector4(3,4,5,15),pixels.Node(storage.Diffuse,p.x,p.y,p.z));
            int r=Region(storage.Domain,p.x,p.y,p.z);
            uint[] stamps=Read(storage.Regions,r*16,16);
            Assert.Equal(1u,stamps[2]);Assert.Equal(1u,stamps[3]);
            Assert.Equal(stamps[11],stamps[8]);Assert.Equal(stamps[12],stamps[9]);
        }
        Assert.Equal(0,pixels.Node(storage.Diffuse,0,16,1).W);
        // Intensity-only change leaves each already completed channel unchanged and does not re-date it.
        input=input with {Frame=2,Generation=2};lighting=lighting with {Solar=new Vector3(100),Lunar=new Vector3(20)};
        Assert.True(work.Update(storage,input,map,lighting,new Vector4[6],()=>true));
        Assert.True(direct.Update(storage,work,input,map,()=>true));
        for(int i=0;i<nodes.Length;i++)
        {
            var p=nodes[i];Assert.Equal(retained[i],pixels.Node(storage.Direct,p.x,p.y,p.z));
            Assert.Equal(1u,Read(storage.Regions,Region(storage.Domain!,p.x,p.y,p.z)*16+2,1)[0]);
        }
    }

    /// <summary>Inactive sources create no solver; compiled detail, channel activation and post-write failure retain truthful metadata.</summary>
    [Fact]
    public void InactiveSourcesSkipWorkAndFailedBatchCannotPublishCompletion()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var work=new LightingVolumeWork(op=>LightingVolumeWorkProgram.Create(Programs.Api,op));
        int creations=0;
        using var direct=new LightingVolumeDirect(()=>{creations++;return LightingVolumeDirectProgram.Create(Programs.Api,true);});
        using var pixels=new LightingVolumeReadback();
        using var map=DynamicTexture2D.Create(20,20,PixelInternalFormat.Rgba32f);
        map.UploadDataImmediate(MapData(20));var input=Input(map,1);
        var lighting=Lighting(Vector3.UnitX,Vector3.UnitY,Vector3.Zero,Vector3.Zero);
        storage.Ensure(input,LightingVolumeEncoding.Create(100),()=>true);
        Assert.True(work.Update(storage,input,map,lighting,new Vector4[6],()=>true));
        Assert.True(direct.Update(storage,work,input,map,()=>true));Assert.Equal(0,creations);
        Assert.Equal(0u,Read(storage.Scratch!,4,1)[0]);
        input=input with {Frame=2};lighting=lighting with {Solar=Vector3.One};
        Assert.True(work.Update(storage,input,map,lighting,new Vector4[6],()=>true));
        Assert.True(direct.Update(storage,work,input,map,()=>true));Assert.Equal(1,creations);
        Vector4 solar=pixels.Node(storage.Direct,2,16,2);
        Assert.InRange(solar.X,0,1);Assert.Equal(1,solar.Y);
        float mask=pixels.Node(storage.Diffuse,2,16,2).W;
        if(mask!=3) Assert.Fail($"Solar mask {mask}, T {solar}; region state: {string.Join(',',Read(storage.Regions,Region(storage.Domain!,2,16,2)*16,16))}");
        input=input with {Frame=3};lighting=lighting with {Solar=Vector3.Zero,Lunar=Vector3.One};
        Assert.True(work.Update(storage,input,map,lighting,new Vector4[6],()=>true));
        Assert.True(direct.Update(storage,work,input,map,()=>true));Assert.Equal(1,creations);
        Vector4 lunar=pixels.Node(storage.Direct,2,16,2);
        Assert.Equal(solar.X,lunar.X);Assert.InRange(lunar.Y,0,1);Assert.Equal(7,pixels.Node(storage.Diffuse,2,16,2).W);
        int region=Region(storage.Domain!,2,16,2);var records=storage.Regions;
        Assert.Equal(3u,Read(records,region*16+3,1)[0]);
        input=input with {Frame=4};lighting=lighting with {Moon=-Vector3.UnitY};
        Assert.True(work.Update(storage,input,map,lighting,new Vector4[6],()=>true));
        int certificates=0;
        Assert.False(direct.Update(storage,work,input,map,()=>++certificates<3));
        Assert.False(direct.Ready);Assert.False(storage.Ready);Assert.NotNull(direct.Failure);
        Assert.Equal(3u,Read(records,region*16+3,1)[0]);
        direct.Reset();work.Reset();storage.Ensure(input,LightingVolumeEncoding.Create(100),()=>true);
        Assert.True(work.Update(storage,input,map,lighting,new Vector4[6],()=>true));
        Assert.True(direct.Update(storage,work,input,map,()=>true));Assert.Equal(2,creations);
        Assert.Equal(4u,Read(storage.Regions,Region(storage.Domain!,2,16,2)*16+3,1)[0]);
    }
    #endregion
    #region Private
    #region Authored inputs and finite quadrature
    /// <summary>Authors varying native extinction, empty gaps and a rejected descriptor within finite height bounds.</summary>
    private static float[] MapData(int width)
    {
        var data=new float[width*width*4];
        for(int z=0;z<width;z++) for(int x=0;x<width;x++)
        {
            int i=(z*width+x)*4;
            data[i]=x is 8 or 9?0:(1+x%5)*.015625f;
            if(x==12) data[i]=float.NaN;
            if(x==15 && z==3) data[i]=float.MaxValue;
            data[i+2]=-50;data[i+3]=x==13?float.MaxValue:x==1?1:50;
        }
        return data;
    }
    /// <summary>Evaluates exactly the declared midpoint positions/weights before driver half-float conversion.</summary>
    private static float Expected(float[] data,int width,Vector3 q,Vector3 direction)
    {
        float length=1500;
        if(direction.X>0) length=Math.Min(length,(width*50-q.X)/direction.X);
        if(direction.Z>0) length=Math.Min(length,(width*50-q.Z)/direction.Z);
        float tau=0,step=length/32;
        for(int i=0;i<32;i++)
        {
            Vector3 p=q+direction*(length*(i+.5f)/32);
            int x=(int)MathF.Floor(p.X/50),z=(int)MathF.Floor(p.Z/50);
            if(x<0||z<0||x>=width||z>=width) continue;
            int index=(z*width+x)*4;float r=data[index];
            if(!float.IsFinite(r)||r<=0||!float.IsFinite(50*data[index+3])||p.Y<50*data[index+2]||p.Y>50*data[index+3]) continue;
            tau=Math.Min(1e30f,tau+(r/50)*step);
        }
        return MathF.Exp(-tau);
    }
    /// <summary>Allows one half-float storage interval plus float arithmetic roundoff for the specified finite formula.</summary>
    private static void AssertStoredTransmission(float expected,float actual)
    {
        Half rounded=(Half)expected;
        float interval=(float)Half.BitIncrement(rounded)-(float)rounded;
        Assert.InRange(Math.Abs(actual-expected),0,interval+1e-6f);
    }
    /// <summary>Defines independent physical source directions and illuminances for one frozen publication.</summary>
    private static AtmosphereLighting Lighting(Vector3 sun,Vector3 moon,Vector3 solar,Vector3 lunar)=>
        new(sun,solar,Vector3.One,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty){Moon=moon,Lunar=lunar};
    #endregion
    #region Identity and metadata
    /// <summary>Supplies stable ownership and allocation identity without retaining a weather owner.</summary>
    private static CloudMapInput Input(GpuTexture map,uint frame)=>new(new object(),1,1,1,1,1,frame,
        new(map.TextureId,2,3,4,map.Width,50,5),default);
    /// <summary>Maps absolute material node membership into the selected region ordering.</summary>
    private static int Region(LightingVolumeDomain domain,int x,int y,int z)
    {
        int rx=(x-1+(int)(domain.X-4*domain.RegionX))/4,ry=(y-1)/4,rz=(z-1+(int)(domain.Z-4*domain.RegionZ))/4;
        return (rz*8+ry)*domain.RegionSide+rx;
    }
    /// <summary>Reads only the required exact integer metadata slice through the existing buffer abstraction.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer,int firstWord,int count)
    {
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var data=buffer.MapScope<uint>(firstWord*4,count,MapBufferAccessMask.MapReadBit);return data.Span.ToArray();
    }
    #endregion
    #endregion
}
