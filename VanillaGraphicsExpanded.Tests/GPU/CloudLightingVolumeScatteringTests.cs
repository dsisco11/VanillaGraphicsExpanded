using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the declared finite diffuse recurrence and coherent partial publication through typed GPU programs.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudLightingVolumeScatteringTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Six equally weighted rays gather directional source, previous diffuse and one environment boundary.</summary>
    [Fact]
    public void FiniteRecurrenceReadsOnlyPublishedSourceAndPreservesChannelMasks()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var work=new LightingVolumeWork(op=>LightingVolumeWorkProgram.Create(Programs.Api,op));
        using var solver=new LightingVolumeScattering(shadows=>LightingVolumeScatteringProgram.Create(Programs.Api,false,shadows));
        using var pixels=new LightingVolumeReadback();using var map=Map(20);
        var input=Input(map,1);var light=Light();
        var encoding=LightingVolumeEncoding.Create(1000);storage.Ensure(input,encoding,()=>true);
        Assert.True(work.Update(storage,input,map,light,new Vector4[6],()=>true));
        Seed(storage,new Vector3(2,4,8),15);
        var old=storage.Diffuse;
        Assert.True(solver.Update(storage,work,input,map,()=>true),solver.Failure);
        Assert.NotSame(old,storage.Diffuse);
        Vector3 expected=Expected(new Vector3(2,4,8),light);
        foreach(int x in new[]{5,6})
        {
            Vector4 value=pixels.Node(storage.Diffuse,x,16,5);
            AssertPhysical(expected,new Vector3(value.X,value.Y,value.Z)*storage.Encoding.Scale,storage.Encoding.Scale);
            Assert.Equal(15,value.W);
            uint[] record=Read(storage.Regions,Region(storage.Domain!,x,16,5)*16,16);
            Assert.Equal(1u,record[4]);Assert.Equal(record[13],record[10]);Assert.Equal(0u,record[15]&4u);
        }
        // A second call in the same frame cannot consume another iteration or swap fields again.
        var published=storage.Diffuse;Assert.True(solver.Update(storage,work,input,map,()=>true));Assert.Same(published,storage.Diffuse);
        Assert.Equal(0,pixels.Node(storage.Diffuse,0,16,5).W);
    }

    /// <summary>Scale growth preserves untouched regions and masks; failed publication retains the old field/scale and completion age.</summary>
    [Fact]
    public void PartialIterationRetainsUntouchedValuesAndFailedSpareIsNotPublished()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var work=new LightingVolumeWork(op=>LightingVolumeWorkProgram.Create(Programs.Api,op));
        int creations=0;
        using var solver=new LightingVolumeScattering(shadows=>{creations++;return LightingVolumeScatteringProgram.Create(Programs.Api,true,shadows);});
        using var pixels=new LightingVolumeReadback();using var map=Map(100);
        var input=Input(map,1);var light=Light() with {Solar=new Vector3(100000),Lunar=Vector3.Zero};
        storage.Ensure(input,LightingVolumeEncoding.Create(10),()=>true);
        Assert.True(work.Update(storage,input,map,light,new Vector4[6],()=>true,directEnabled:false,diffuseEnabled:false));
        var unchanged=storage.Diffuse;
        Assert.True(solver.Update(storage,work,input,map,()=>true));Assert.Equal(0,creations);Assert.Same(unchanged,storage.Diffuse);
        input=input with {Frame=2};
        Assert.True(work.Update(storage,input,map,light,new Vector4[6],()=>true,directEnabled:false));
        Seed(storage,new Vector3(1,2,3),7);
        var list=Read(storage.Scratch!,576,256).ToHashSet();
        int unselected=Enumerable.Range(0,storage.Domain!.Regions).First(r=>!list.Contains((uint)r)&&InteriorPoint(storage.Domain,r) is not null);
        var p=InteriorPoint(storage.Domain,unselected)!.Value;
        var previousEncoding=storage.Encoding;
        Assert.True(solver.Update(storage,work,input,map,()=>true),solver.Failure);
        Assert.True(storage.Encoding.Scale>previousEncoding.Scale);
        Vector4 retained=pixels.Node(storage.Diffuse,p.X,p.Y,p.Z);
        AssertPhysical(new Vector3(1,2,3),new Vector3(retained.X,retained.Y,retained.Z)*storage.Encoding.Scale,storage.Encoding.Scale);
        Assert.Equal(7,retained.W);Assert.Equal(0u,Read(storage.Regions,unselected*16+4,1)[0]);
        int selected=(int)list.First(r=>InteriorPoint(storage.Domain,(int)r) is not null);
        Assert.Equal(2u,Read(storage.Regions,selected*16+4,1)[0]);
        var oldField=storage.Diffuse;var oldEncoding=storage.Encoding;var records=storage.Regions;
        input=input with {Frame=3};light=light with {Environment=new Vector3(1e7f)};
        Assert.True(work.Update(storage,input,map,light,new Vector4[6],()=>true,directEnabled:false));
        var priorStamps=Read(records,0,storage.Domain.Regions*16);
        int checks=0;
        Assert.False(solver.Update(storage,work,input,map,()=>++checks<3));
        Assert.False(storage.Ready);Assert.False(solver.Ready);Assert.Equal(oldEncoding,storage.Encoding);Assert.True(oldField.IsValid);
        var after=Read(records,0,storage.Domain.Regions*16);
        for(int r=0;r<storage.Domain.Regions;r++) Assert.Equal(priorStamps[r*16+4],after[r*16+4]);
    }
    #endregion
    #region Private
    #region Authored finite transport
    /// <summary>Evaluates the nominated twelve-point recurrence with constant authored published lighting.</summary>
    private static Vector3 Expected(Vector3 diffuse,AtmosphereLighting light)
    {
        Vector3 total=Vector3.Zero;
        foreach(var d in new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ})
        {
            float c=Vector3.Dot(d,Vector3.UnitX),m=Vector3.Dot(d,Vector3.UnitY);
            Vector3 q=.9f*(light.Solar*(.25f*Phase(c))+light.Lunar*(.75f*Phase(m)));
            float t=1,step=MathF.Exp(-.001f*25);Vector3 ray=Vector3.Zero;
            for(int i=0;i<12;i++) {ray+=t*(1-step)*(q+diffuse);t*=step;}
            total+=(ray+t*light.Environment)*(.9f/6);
        }
        return total;
    }
    /// <summary>Evaluates the nominated normalized HG phase for the authored outgoing direction.</summary>
    private static float Phase(float cosine)=>.64f/(4*MathF.PI*MathF.Pow(1.36f-1.2f*cosine,1.5f));
    /// <summary>Seeds complete uniform fields; selected writes must never become inputs within the same iteration.</summary>
    private static void Seed(LightingVolumeStorage storage,Vector3 physical,int mask)
    {
        var d=storage.Domain!;var rgba=new float[d.Voxels*4];var rg=new float[d.Voxels*2];
        for(int z=1;z<d.Size-1;z++) for(int y=1;y<33;y++) for(int x=1;x<d.Size-1;x++)
        {
            int i=(z*34+y)*d.Size+x;
            rgba[4*i]=physical.X/storage.Encoding.Scale;rgba[4*i+1]=physical.Y/storage.Encoding.Scale;
            rgba[4*i+2]=physical.Z/storage.Encoding.Scale;rgba[4*i+3]=mask;rg[2*i]=.25f;rg[2*i+1]=.75f;
        }
        storage.Diffuse.UploadDataImmediate(rgba,0,0,0,d.Size,34,d.Size);
        storage.Direct.UploadDataImmediate(rg,0,0,0,d.Size,34,d.Size);
    }
    /// <summary>Allows float arithmetic and one half-float storage interval for the declared finite result.</summary>
    private static void AssertPhysical(Vector3 expected,Vector3 actual,float scale)
    {
        for(int i=0;i<3;i++)
        {
            Half value=(Half)(expected[i]/scale);
            float interval=((float)Half.BitIncrement(value)-(float)value)*scale;
            Assert.InRange(Math.Abs(expected[i]-actual[i]),0,interval+2e-5f);
        }
    }
    /// <summary>Supplies uniform native extinction with support extending beyond the lighting band.</summary>
    private static DynamicTexture2D Map(int width)
    {
        var map=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);var data=new float[width*width*4];
        for(int i=0;i<width*width;i++) {data[i*4]=.05f;data[i*4+2]=-50;data[i*4+3]=50;}
        map.UploadDataImmediate(data);return map;
    }
    /// <summary>Defines physical RGB illuminances and an already converted environment radiance.</summary>
    private static AtmosphereLighting Light()=>new(Vector3.UnitX,new Vector3(10,20,30),new Vector3(1,2,3),Vector3.Zero,
        Vector3.Zero,ImmutableArray<float>.Empty){Moon=Vector3.UnitY,Lunar=new Vector3(3,2,1)};
    #endregion
    #region Identity and readback
    /// <summary>Defines a stable native owner and exact current input identity.</summary>
    private static CloudMapInput Input(GpuTexture map,uint frame)=>new(new object(),1,1,1,1,1,frame,
        new(map.TextureId,2,3,4,map.Width,50,5),default);
    /// <summary>Maps an interior texture node to its absolute-key region slot.</summary>
    private static int Region(LightingVolumeDomain d,int x,int y,int z)=>
        (((z-1+(int)(d.Z-4*d.RegionZ))/4)*8+(y-1)/4)*d.RegionSide+(x-1+(int)(d.X-4*d.RegionX))/4;
    /// <summary>Finds a genuine interior node in one possibly padded region slot.</summary>
    private static (int X,int Y,int Z)? InteriorPoint(LightingVolumeDomain d,int r)
    {
        int x=Math.Max(1,(r%d.RegionSide)*4-(int)(d.X-4*d.RegionX)+1);
        int y=((r/d.RegionSide)%8)*4+1,z=Math.Max(1,(r/(d.RegionSide*8))*4-(int)(d.Z-4*d.RegionZ)+1);
        return x<d.Size-1&&z<d.Size-1?(x,y,z):null;
    }
    /// <summary>Reads only requested exact metadata through the managed buffer abstraction.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer,int first,int count)
    {
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var data=buffer.MapScope<uint>(first*4,count,MapBufferAccessMask.MapReadBit);return data.Span.ToArray();
    }
    #endregion
    #endregion
}
