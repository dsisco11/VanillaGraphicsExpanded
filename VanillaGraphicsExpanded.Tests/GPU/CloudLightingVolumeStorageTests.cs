using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks initialized candidate resources, actual typed sampling, remaps and failed publication.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudLightingVolumeStorageTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Hardware interpolation uses exact channel masks, retains overlap and publishes RGB scale with its field.</summary>
    [Fact]
    public void InitializedFieldsRetainOverlapAndPublishScaleCoherently()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var probe=LightingVolumeProbeProgram.Create(Programs.Api,false);
        using var map=NativeMap(20);
        using var result=GpuShaderStorageBuffer.Create(); result.UploadData(new Vector4[2]);
        var input=Input(map); var encoding=LightingVolumeEncoding.Create(1000);
        storage.Ensure(input,encoding,()=>true);
        var first=storage.Domain!; uint generation=storage.Generation;
        Vector3 q=new(300,0,300);
        var fallback=Query(probe,storage,map,result,input.Placement,q);
        Assert.Equal(.01f,fallback[0].X,6); Assert.Equal(1,fallback[0].Y); Assert.Equal(1,fallback[0].Z);
        Assert.Equal(1.8f,fallback[1].X,5); Assert.Equal(3.6f,fallback[1].Y,5); Assert.Equal(5.4f,fallback[1].Z,5);
        Assert.Equal(TextureTarget.Texture3D,storage.Diffuse.TextureTarget);
        Assert.Equal(TextureFilterMode.Linear,storage.Diffuse.FilterMode);
        Assert.Equal(PixelInternalFormat.Rgba16f,storage.Diffuse.InternalFormat);
        Assert.Equal(PixelInternalFormat.Rg16f,storage.Direct.InternalFormat);
        Assert.Equal(1,storage.Diffuse.StorageMipLevels);

        // Author affine values: the expected trilinear result is analytic, not an approximation target.
        var diffuse=new float[first.Voxels*4]; var direct=new float[first.Voxels*2];
        for(int z=1;z<first.Size-1;z++) for(int y=1;y<33;y++) for(int x=1;x<first.Size-1;x++)
        {
            int index=(z*34+y)*first.Size+x;
            diffuse[index*4]=(x+2*y+4*z)/encoding.Scale;
            diffuse[index*4+1]=100/encoding.Scale; diffuse[index*4+2]=200/encoding.Scale;
            diffuse[index*4+3]=15;
            direct[index*2]=.25f; direct[index*2+1]=.75f;
        }
        storage.Diffuse.UploadDataImmediate(diffuse,0,0,0,first.Size,34,first.Size);
        storage.Direct.UploadDataImmediate(direct,0,0,0,first.Size,34,first.Size);
        var lit=Query(probe,storage,map,result,input.Placement,q);
        Vector3 node=(q-first.FirstNode)/new Vector3(100,25,100);
        Assert.Equal(.25f,lit[0].Y); Assert.Equal(.75f,lit[0].Z);
        Assert.Equal(node.X+2*node.Y+4*node.Z,lit[1].X,4);
        Assert.Equal(100,lit[1].Y); Assert.Equal(200,lit[1].Z);
        var regionWords=new uint[first.Regions*16]; regionWords[16+5]=123;
        storage.Regions.UploadData(regionWords);

        var oldField=storage.Diffuse;
        Assert.True(storage.CopyDiffuse(encoding.Grow(1e7),()=>true));
        Assert.NotSame(oldField,storage.Diffuse);
        Assert.Equal(lit,Query(probe,storage,map,result,input.Placement,q));
        // Move by a lighting cell; query the same unwrapped material position using its new native q.
        input=input with { Placement=input.Placement with { CenterX=2 } };
        storage.Ensure(input,encoding,()=>true);
        Assert.Equal(generation,storage.Generation);
        Assert.Equal(lit,Query(probe,storage,map,result,input.Placement,q-new Vector3(100,0,0)));
        using(var words=storage.Regions.MapScope<uint>(0,storage.Domain!.Regions*16,MapBufferAccessMask.MapReadBit))
            Assert.Equal(123u,words.Span[5]);
        var border=Query(probe,storage,map,result,input.Placement,new Vector3(990,0,300));
        Assert.Equal(1,border[0].Y); Assert.Equal(1,border[0].Z);
        Assert.Equal(fallback[1],border[1]);
        // A single invalid corner rejects the entire diffuse footprint, while direct stays valid.
        storage.Diffuse.UploadDataImmediate(new float[]{0,0,0,7},2,16,3,1,1,1);
        var partial=Query(probe,storage,map,result,input.Placement,q-new Vector3(100,0,0));
        Assert.Equal(fallback[1],partial[1]); Assert.Equal(.25f,partial[0].Y);
        // Width/owner changes revoke even when storage dimensions would happen to match.
        var retained=storage.Diffuse;
        input=input with { Owner=new object() }; storage.Ensure(input,encoding,()=>true);
        Assert.False(retained.IsValid); Assert.True(storage.Generation>generation);
        var stableField=storage.Diffuse; var stableEncoding=storage.Encoding;
        int certificates=0;
        Assert.False(storage.CopyDiffuse(stableEncoding.Grow(1e9),()=>++certificates==1));
        Assert.False(storage.Ready); Assert.Equal(stableEncoding,storage.Encoding);
        Assert.True(stableField.IsValid); Assert.Throws<InvalidOperationException>(()=>{_ = storage.Diffuse;});
        storage.Reset(); Assert.False(stableField.IsValid); Assert.Null(storage.Domain);
        storage.Ensure(input,encoding,()=>true); Assert.True(storage.Ready);
        var allocated=storage.Diffuse; certificates=0;
        Assert.Throws<InvalidOperationException>(()=>storage.Ensure(input with {Owner=new object()},encoding,()=>++certificates==1));
        Assert.False(storage.Ready); Assert.Null(storage.PreviousObservation); Assert.Null(storage.Scratch);
        Assert.False(allocated.IsValid);
    }

    /// <summary>Private density preserves units, connected faces, detail bounds and finite native support.</summary>
    [Fact]
    public void IsolatedDensityRespectsSupportAndCompiledDetail()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var map=NativeMap(20);
        using var result=GpuShaderStorageBuffer.Create(); result.UploadData(new Vector4[2]);
        var input=Input(map); storage.Ensure(input,LightingVolumeEncoding.Create(10),()=>true);
        foreach(bool detail in new[]{false,true})
        {
            using var probe=LightingVolumeProbeProgram.Create(Programs.Api,detail);
            foreach(var q in new[]{new Vector3(50,0,75),new Vector3(75,0,75),new Vector3(-1,0,75),new Vector3(75,501,75)})
            {
                float density=Query(probe,storage,map,result,input.Placement,q)[0].X;
                if(q.X<0||q.Y>500) Assert.Equal(0,density);
                else if(!detail) Assert.Equal(.01f,density,6);
                else Assert.InRange(density,.0085f,.0115f);
            }
            if(detail) Assert.Equal(0,Query(probe,storage,map,result,input.Placement,new Vector3(0,0,75))[0].X);
        }
    }
    #endregion
    #region Private
    /// <summary>Creates finite authored native columns without invoking the weather producer.</summary>
    private static DynamicTexture2D NativeMap(int width)
    {
        var map=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
        var data=new float[width*width*4];
        for(int i=0;i<width*width;i++) {data[4*i]=.5f;data[4*i+2]=-10;data[4*i+3]=10;}
        map.UploadDataImmediate(data);return map;
    }
    /// <summary>Supplies a stable allocation identity independently of content generation and frame.</summary>
    private static CloudMapInput Input(GpuTexture map)=>new(new object(),1,1,1,1,1,1,
        new CloudMapStorage(map.TextureId,2,3,4,map.Width,50,5),default);
    /// <summary>Publishes typed query inputs and reads only two bounded diagnostic records.</summary>
    private static Vector4[] Query(LightingVolumeProbeProgram probe,LightingVolumeStorage storage,GpuTexture map,
        GpuShaderStorageBuffer output,CloudPlacement placement,Vector3 q)
    {
        probe.Capture(storage.Domain!,placement,storage.Encoding,q);
        probe.Map=map;probe.Direct=storage.Direct;probe.Diffuse=storage.Diffuse;probe.Result=output;
        probe.Dispatch(1);GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit);
        using var read=output.MapScope<Vector4>(0,2,MapBufferAccessMask.MapReadBit);return read.Span.ToArray();
    }
    #endregion
}
