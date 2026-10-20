using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks DDA coverage and progress on actual anisotropic circular textures without image-quality gates.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeTraversalTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Occupied and certified empty traversal cover the same clipped domain, including negative/tied axes and seams.</summary>
    [Fact]
    public void DdaRetainsCoverageAndRejectsFailedPrefixes()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        var domain=new CloudVolumeDomain(5,default,-100,100);
        storage.Request(domain,new(new object(),1,1),new(Vector3.One),Vector3.Zero,()=>true);
        while(!storage.InitializeNext(()=>true)){}
        Assert.True(storage.PublishPending(()=>true,()=>true));var fields=storage.Published;
        using var probe=CloudVolumeTraversalProbeProgram.Create(Programs.Api);
        using var output=GpuShaderStorageBuffer.Create();output.UploadData(new Vector4[3]);
        var values=new float[domain.Voxels];Array.Fill(values,1);fields.Density.UploadDataImmediate(values,0,0,0,domain.Size,66,domain.Size);
        var occupied=new uint[domain.Blocks];Array.Fill(occupied,1u);fields.Occupancy.UploadData(occupied);
        var cases=new[]{(new Vector3(0,0,125),Vector3.UnitX,250f),(new Vector3(250,0,125),-Vector3.UnitX,250f),
            (new Vector3(125,-400,125),Vector3.UnitY,800f),
            (new Vector3(0,0,0),Vector3.Normalize(new Vector3(1,0,1)),MathF.Sqrt(2)*250),
            (new Vector3(125,0,125),Vector3.Normalize(new Vector3(-1,.25f,-1)),100f),
            (new Vector3(125,0,125),Vector3.Normalize(new Vector3(-.14f,.294f,.945f)),100f)};
        foreach(var (origin,direction,length) in cases)
        foreach(float fraction in new[]{.5f,1f})
        {
            var result=Trace(probe,fields,output,origin,direction,length,fraction);
            Assert.Equal(1,result[1].X);Assert.Equal(0,result[1].Y);
            // Floating accumulation allowance checks interval coverage of a constant field, not cloud approximation.
            Assert.InRange(result[0].X,length*(1-32f/8388608),length*(1+32f/8388608));
            Assert.Equal(result[0].X,result[0].Z);Assert.InRange(result[0].W,1,2048);
            // This bounds individual intervals, not approximation quality. Subtracting absolute
            // ray parameters can add a few ULPs even when the declared step is respected.
            float rounding=8*(MathF.BitIncrement(length)-length);
            Assert.InRange(result[2].X,0,result[2].Y+rounding);
        }
        var failed=Trace(probe,fields,output,new(0,0,125),Vector3.UnitX,250,.5f,1);
        Assert.Equal(0,failed[1].X);Assert.Equal(1,failed[1].Y);
        Array.Clear(values);fields.Density.UploadDataImmediate(values,0,0,0,domain.Size,66,domain.Size);
        fields.Occupancy.UploadData(new uint[domain.Blocks]);
        var empty=Trace(probe,fields,output,new(0,0,125),Vector3.UnitX,250,.5f);
        Assert.Equal(1,empty[1].X);Assert.Equal(250,empty[0].X);Assert.Equal(0,empty[1].W);
        Assert.True(empty[1].Z>0);Assert.Equal(0,empty[0].Z);
        var miss=Trace(probe,fields,output,new(-1,0,125),-Vector3.UnitX,250,1);
        Assert.Equal(1,miss[1].X);Assert.Equal(0,miss[0].X);
        // An odd horizontal allocation has short edge blocks; shifted native keys place both
        // circular seams inside these rays, unlike the complete 8-texel blocks above.
        var shifted=new CloudVolumeDomain(6,default(VanillaGraphicsExpanded.PBR.Clouds.CloudPlacement) with{CenterX=-1,CenterZ=1},-100,100);
        storage.Request(shifted,new(new object(),1,1),new(Vector3.One),Vector3.Zero,()=>true);
        while(!storage.InitializeNext(()=>true)){}
        Assert.True(storage.PublishPending(()=>true,()=>true));fields=storage.Published;
        fields.Density.Clear(1);fields.Occupancy.UploadData(Enumerable.Repeat(1u,shifted.Blocks).ToArray());
        foreach(var (origin,direction) in new[]{(new Vector3(0,0,150),Vector3.UnitX),
            (new Vector3(300,0,150),-Vector3.UnitX),(new Vector3(150,0,0),Vector3.UnitZ),
            (new Vector3(150,0,300),-Vector3.UnitZ)})
        foreach(float fraction in new[]{.5f,1f})
        {
            var traced=Trace(probe,fields,output,origin,direction,300,fraction);
            Assert.Equal(1,traced[1].X);Assert.Equal(0,traced[1].Y);
            Assert.InRange(traced[0].X,300*(1-32f/8388608),300*(1+32f/8388608));
            Assert.Equal(traced[0].X,traced[0].Z);
        }
    }
    #endregion
    #region Private
    /// <summary>Submits only typed production resources and reads three bounded diagnostic records.</summary>
    private static Vector4[] Trace(CloudVolumeTraversalProbeProgram probe,CloudVolumeFields fields,GpuShaderStorageBuffer output,
        Vector3 origin,Vector3 direction,float distance,float fraction,int limit=0)
    {
        probe.Capture(fields.Domain,origin,direction,distance,fraction,limit);probe.Density=fields.Density;
        probe.Occupancy=fields.Occupancy;probe.Result=output;probe.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var read=output.MapScope<Vector4>(0,3,MapBufferAccessMask.MapReadBit);return read.Span.ToArray();
    }
    #endregion
}
