using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks DDA coverage and progress on actual anisotropic circular textures without image-quality gates.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeTraversalTests(HeadlessGLFixture fixture,Xunit.ITestOutputHelper testOutput):LumOnShaderFunctionalTestBase(fixture)
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
            (new Vector3(125,-400,125),new Vector3(float.Epsilon,1,0),800f),
            (new Vector3(125,-400,125),new Vector3(-32*float.Epsilon,1,0),800f),
            (new Vector3(0,0,0),Vector3.Normalize(new Vector3(1,0,1)),MathF.Sqrt(2)*250),
            (new Vector3(125,0,125),Vector3.Normalize(new Vector3(-1,.25f,-1)),100f),
            (new Vector3(125,0,125),Vector3.Normalize(new Vector3(-.14f,.294f,.945f)),100f)};
        foreach(var (origin,direction,length) in cases)
        {
            var result=Trace(probe,fields,output,origin,direction,length);
            testOutput.WriteLine($"Constant-cell ray {origin} / {direction}, length {length}: sampled cells {result[1].W}, operations {result[0].W}, empty spans {result[1].Z}.");
            Assert.Equal(1,result[1].X);Assert.Equal(0,result[1].Y);
            Assert.Equal(0,result[2].Z);Assert.Equal(0,result[2].W);
            // Floating accumulation allowance checks interval coverage of a constant field, not cloud approximation.
            Assert.InRange(result[0].X,length*(1-32f/8388608),length*(1+32f/8388608));
            Assert.Equal(result[0].X,result[0].Z);Assert.InRange(result[0].W,1,2048);
            // This bounds individual intervals, not approximation quality. Subtracting absolute
            // ray parameters can add a few ULPs even when the declared step is respected.
            float rounding=8*(MathF.BitIncrement(length)-length);
            Assert.InRange(result[2].X,0,result[2].Y+rounding);
        }
        var failed=Trace(probe,fields,output,new(0,0,125),Vector3.UnitX,250,1);
        Assert.Equal(0,failed[1].X);Assert.Equal(1,failed[1].Y);
        // An exact constant-per-cell field distinguishes voxel lookup from interpolated samples.
        for(int z=0;z<domain.Size;z++)for(int y=0;y<66;y++)for(int x=0;x<domain.Size;x++)
            values[(z*66+y)*domain.Size+x]=(x-domain.SlotX+domain.Size)%domain.Size+1;
        fields.Density.UploadDataImmediate(values,0,0,0,domain.Size,66,domain.Size);
        double first=domain.FirstX*CloudVolumeDomain.HorizontalCell-domain.AnchorX,tau=0;int cells=0;
        for(int x=0;x<domain.Size;x++)
        {
            double length=Math.Max(0,Math.Min(250,first+(x+1)*CloudVolumeDomain.HorizontalCell)-Math.Max(0,first+x*CloudVolumeDomain.HorizontalCell));
            if(length>0){tau+=(x+1)*length;cells++;}
        }
        foreach(var (origin,direction) in new[]{(new Vector3(0,0,125),Vector3.UnitX),(new Vector3(250,0,125),-Vector3.UnitX)})
        {
            var traced=Trace(probe,fields,output,origin,direction,250);
            testOutput.WriteLine($"Axis field {origin} / {direction}: {string.Join("; ",traced)}.");
            Assert.Equal(1,traced[1].X);
            // Original-ray plane division and float32 summation may round the analytic integral.
            Assert.InRange(traced[0].Z,(float)tau*(1-32f/8388608),(float)tau*(1+32f/8388608));
            Assert.Equal(cells,traced[1].W);
        }
        // Cross the circular seam from a certified-empty physical block into occupied cells,
        // then reverse it. This exercises both scale changes without a filtered-support assumption.
        for(int z=0;z<domain.Size;z++)for(int y=0;y<66;y++)for(int x=8;x<domain.Size;x++)
            values[(z*66+y)*domain.Size+x]=0;
        fields.Density.UploadDataImmediate(values,0,0,0,domain.Size,66,domain.Size);
        fields.Occupancy.UploadData(Enumerable.Range(0,domain.Blocks).Select(r=>r%domain.BlocksX==0?1u:0u).ToArray());
        tau=0;cells=0;
        for(int x=0;x<domain.Size;x++)
        {
            double length=Math.Max(0,Math.Min(250,first+(x+1)*CloudVolumeDomain.HorizontalCell)-Math.Max(0,first+x*CloudVolumeDomain.HorizontalCell));
            if(length>0&&(x+domain.SlotX)%domain.Size<8){tau+=(x+1)*length;cells++;}
        }
        foreach(var (origin,direction) in new[]{(new Vector3(0,0,125),Vector3.UnitX),(new Vector3(250,0,125),-Vector3.UnitX)})
        {
            var traced=Trace(probe,fields,output,origin,direction,250);
            testOutput.WriteLine($"Mixed seam {origin} / {direction}: {string.Join("; ",traced)}.");
            Assert.Equal(0,traced[2].Z);Assert.Equal(0,traced[2].W);
            Assert.Equal(1,traced[1].X);Assert.True(traced[1].Z>0);Assert.Equal(cells,traced[1].W);
            Assert.InRange(traced[0].X,250*(1-32f/8388608),250*(1+32f/8388608));
            Assert.InRange(traced[0].Z,(float)tau*(1-32f/8388608),(float)tau*(1+32f/8388608));
        }
        Array.Clear(values);fields.Density.UploadDataImmediate(values,0,0,0,domain.Size,66,domain.Size);
        fields.Occupancy.UploadData(new uint[domain.Blocks]);
        var empty=Trace(probe,fields,output,new(0,0,125),Vector3.UnitX,250);
        Assert.Equal(1,empty[1].X);Assert.Equal(250,empty[0].X);Assert.Equal(0,empty[1].W);
        Assert.True(empty[1].Z>0);Assert.Equal(0,empty[0].Z);
        var miss=Trace(probe,fields,output,new(-1,0,125),-Vector3.UnitX,250);
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
        {
            var traced=Trace(probe,fields,output,origin,direction,300);
            Assert.Equal(1,traced[1].X);Assert.Equal(0,traced[1].Y);
            Assert.InRange(traced[0].X,300*(1-32f/8388608),300*(1+32f/8388608));
            Assert.Equal(traced[0].X,traced[0].Z);
        }
    }
    #endregion
    #region Private
    /// <summary>Submits only typed production resources and reads three bounded diagnostic records.</summary>
    private static Vector4[] Trace(CloudVolumeTraversalProbeProgram probe,CloudVolumeFields fields,GpuShaderStorageBuffer output,
        Vector3 origin,Vector3 direction,float distance,int limit=0)
    {
        probe.Capture(fields.Domain,origin,direction,distance,limit);probe.Density=fields.Density;
        probe.Occupancy=fields.Occupancy;probe.Result=output;probe.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var read=output.MapScope<Vector4>(0,3,MapBufferAccessMask.MapReadBit);return read.Span.ToArray();
    }
    #endregion
}
