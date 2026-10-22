using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the declared diffuse recurrence and coherent partial publication without convergence/image gates.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeDiffuseTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Gathers a constant physical source, preserves untouched cells through rescaling, and rejects failed destinations.</summary>
    [Fact]
    public void DiffuseTransportPreservesCoherentHistoryAndCommitsOnlySuccessfulBatches()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        var d=new CloudVolumeDomain(5,default,-100,100);var initial=new CloudVolumeEncoding(new Vector3(4));
        var environment=new Vector3(2,3,4);
        storage.Request(d,new(new object(),1,1),initial,environment,()=>true);
        while(!storage.InitializeNext(()=>true)){}
        Assert.True(storage.PublishPending(()=>true,()=>true));var fields=storage.Published;
        var density=Enumerable.Repeat(.0078125f,d.Voxels).ToArray();
        fields.Density.UploadDataImmediate(density,0,0,0,d.Size,66,d.Size);
        fields.Occupancy.UploadData(Enumerable.Repeat(1u,d.Blocks).ToArray());
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,MapBufferAccessMask.MapWriteBit))membership.Span[0]=1;
        fields.Direct.UploadDataImmediate(Enumerable.Repeat(float.NaN,d.Voxels*2).ToArray(),0,0,0,d.Size,66,d.Size);
        using var work=new CloudVolumeWork((op,large,shared)=>CloudVolumeWorkProgram.Create(Programs.Api,op,large,shared));
        using var solver=new CloudVolumeDiffuse(shadows=>CloudVolumeDiffuseProgram.Create(Programs.Api,shadows));
        var light=new AtmosphereLighting(Vector3.UnitY,Vector3.Zero,environment,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty);
        var planes=Enumerable.Repeat(new Vector4(0,0,0,1),6).ToArray();
        Assert.True(work.Select(storage,light,1,planes,()=>true,directEnabled:false,diffuseEnabled:false));
        var unchanged=fields.Diffuse;
        using(var disabled=new CloudVolumeDiffuse(_=>throw new InvalidOperationException("Disabled diffuse compiled a program.")))
        {Assert.True(disabled.Update(storage,work,()=>true));Assert.Equal(0,disabled.LastCopyBytes);}
        Assert.Same(unchanged,fields.Diffuse);
        using var probe=CloudVolumeProbeProgram.Create(Programs.Api);
        using var result=GpuShaderStorageBuffer.Create();result.UploadData(new Vector4[2]);
        var selectedPoint=Point(d,3,2,3);var untouchedPoint=Point(d,9,64,9);
        Assert.True(work.Select(storage,light,2,planes,()=>true,directEnabled:false));
        Assert.True(solver.Update(storage,work,()=>true,deferStatus:true),solver.Failure);
        Assert.Same(unchanged,fields.Diffuse);Assert.Equal(initial,fields.Encoding);Assert.Null(fields.LightingBounds);
        work.SubmitCompletion(storage,()=>true);
        using(var fence=GpuFence.Insert())Assert.NotEqual(WaitSyncStatus.WaitFailed,fence.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(solver.Complete(storage,work,()=>true),solver.Failure);
        Assert.Equal(32,solver.LastJobs);Assert.Equal(d.CopyBytes,solver.LastCopyBytes);
        Assert.NotSame(unchanged,fields.Diffuse);Assert.True(fields.Encoding.Scale>initial.Scale);
        // For Q=0 and D_read=Environment=C, each ray is C*(1-T)+T*C. The declared update is exactly a*C.
        var value=Query(probe,fields,result,selectedPoint);var encoded=fields.Encoding.Encode(.9f*environment);
        for(int i=0;i<3;i++)
        {
            float actual=value[i]/fields.Encoding.Scale;
            int expectedBits=BitConverter.HalfToUInt16Bits((Half)encoded[i]);
            Assert.InRange(Math.Abs((int)BitConverter.HalfToUInt16Bits((Half)actual)-expectedBits),0,2);
        }
        Assert.Equal(0,value.W);Assert.Equal(new Vector4(environment,0),Query(probe,fields,result,untouchedPoint));
        var coherent=fields.Diffuse;
        Assert.True(solver.Update(storage,work,()=>true));Assert.Same(coherent,fields.Diffuse);
        light=light with{Environment=Vector3.One,Extinction=new Vector3(.2f),Horizon=new Vector3(5)};
        Assert.True(work.Select(storage,light,3,planes,()=>true,directEnabled:false));
        Assert.Equal(value,Query(probe,fields,result,selectedPoint)); // Admission leaves old compatible energy intact.
        Assert.True(solver.Update(storage,work,()=>true),solver.Failure);
        Assert.Equal(environment,fields.LightingBounds!.Environment);Assert.True(fields.Encoding.Cap.X>=18);
        // The old ping-pong source still used the initial scale, so both transition batches
        // require a full re-encode. Subsequent batches repair only the last successful blocks.
        Assert.Equal(d.CopyBytes,solver.LastCopyBytes);
        Assert.True(work.Select(storage,light,4,planes,()=>true,directEnabled:false));
        var previous=ReadWords(fields,CloudVolumeWork.RepairListWord,32);
        var next=ReadWords(fields,CloudVolumeWork.DiffuseListWord,32);
        int retained=(int)previous.First(r=>!next.Contains(r));
        int rx=retained%d.BlocksX*8+1,ry=(retained/d.BlocksX)%d.BlocksY*4+1,rz=retained/(d.BlocksX*d.BlocksY)*8+1;
        var retainedPoint=Point(d,Math.Min(rx,d.Size-2),Math.Min(ry,64),Math.Min(rz,d.Size-2));
        var retainedValue=Query(probe,fields,result,retainedPoint);
        long repairBytes=previous.Sum(r=>
        {
            int x=(int)r%d.BlocksX*8,y=((int)r/d.BlocksX)%d.BlocksY*4,z=(int)r/(d.BlocksX*d.BlocksY)*8;
            return 16L*Math.Min(8,d.Size-x)*Math.Min(4,66-y)*Math.Min(8,d.Size-z);
        });
        Assert.True(solver.Update(storage,work,()=>true),solver.Failure);
        Assert.Equal(repairBytes,solver.LastCopyBytes);Assert.InRange(repairBytes,1,131072);
        Assert.Equal(retainedValue,Query(probe,fields,result,retainedPoint));
        fields.Density.UploadDataImmediate(new float[d.Voxels],0,0,0,d.Size,66,d.Size);
        fields.Occupancy.UploadData(new uint[d.Blocks]);
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,MapBufferAccessMask.MapWriteBit))membership.Span[0]=1;
        var repairHistory=ReadWords(fields,CloudVolumeWork.RepairListWord,32);
        Assert.True(work.Select(storage,light with{Environment=new Vector3(1000)},5,planes,()=>true,directEnabled:false));
        coherent=fields.Diffuse;var encoding=fields.Encoding;
        Assert.True(work.TryBatch(storage,out var empty));
        using(var args=empty!.Commands.MapScope<uint>(48,3,MapBufferAccessMask.MapReadBit))Assert.Equal(0u,args.Span[0]);
        // Poison a block covered by repair history: empty work must not repair or publish it.
        int poison=(int)repairHistory[0];
        fields.DiffuseSpare.UploadDataImmediate(new[]{float.NaN,float.NaN,float.NaN,0},
            poison%d.BlocksX*8,(poison/d.BlocksX)%d.BlocksY*4,poison/(d.BlocksX*d.BlocksY)*8,1,1,1);
        Assert.True(solver.Update(storage,work,()=>true,deferStatus:true));
        work.SubmitCompletion(storage,()=>true);
        using(var fence=GpuFence.Insert())Assert.NotEqual(WaitSyncStatus.WaitFailed,fence.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(solver.Complete(storage,work,()=>true));Assert.Equal(0,solver.LastCopyBytes);
        Assert.Same(coherent,fields.Diffuse);Assert.Equal(encoding,fields.Encoding);
        Assert.Equal(repairHistory,ReadWords(fields,CloudVolumeWork.RepairListWord,32));
        Assert.Equal(32u,ReadWords(fields,CloudVolumeWork.RepairCountWord,1)[0]);
        Assert.True(solver.Update(storage,work,()=>true));Assert.Same(coherent,fields.Diffuse);
        fields.Density.UploadDataImmediate(density,0,0,0,d.Size,66,d.Size);
        fields.Occupancy.UploadData(Enumerable.Repeat(1u,d.Blocks).ToArray());
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,MapBufferAccessMask.MapWriteBit))membership.Span[0]=1;
        // Exercise the actual compiled cached-shadow path after real ordered direct completion.
        light=light with{Solar=Vector3.One,Lunar=Vector3.One,Moon=-Vector3.UnitX};
        using var direct=new CloudVolumeDirect(op=>CloudVolumeDirectProgram.Create(Programs.Api,op));
        Assert.True(work.Select(storage,light,6,planes,()=>true));
        Assert.True(direct.Update(storage,work,()=>true),direct.Failure);
        Assert.True(solver.Update(storage,work,()=>true),solver.Failure);
        Assert.True(fields.LightingBounds.DirectCap.X>0);
        Assert.True(work.Select(storage,light,7,planes,()=>true));
        coherent=fields.Diffuse;encoding=fields.Encoding;uint revision=fields.Revision;
        var bounds=fields.LightingBounds;var stamps=Completed(fields);
        repairHistory=ReadWords(fields,CloudVolumeWork.RepairListWord,32);
        Assert.True(direct.Update(storage,work,()=>true,deferStatus:true),direct.Failure);
        Array.Fill(density,float.NaN);fields.Density.UploadDataImmediate(density,0,0,0,d.Size,66,d.Size);
        Assert.True(solver.Update(storage,work,()=>true,deferStatus:true));
        work.SubmitCompletion(storage,()=>true);
        using(var fence=GpuFence.Insert())Assert.NotEqual(WaitSyncStatus.WaitFailed,fence.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(solver.Complete(storage,work,()=>true));Assert.False(storage.Ready);
        Assert.Same(coherent,fields.Diffuse);Assert.Equal(encoding,fields.Encoding);Assert.Equal(revision,fields.Revision);
        Assert.Same(bounds,fields.LightingBounds);Assert.Equal(stamps,Completed(fields));
        Assert.Equal(repairHistory,ReadWords(fields,CloudVolumeWork.RepairListWord,32));
    }
    #endregion
    #region Private
    /// <summary>Reads a bounded existing list or counter after ordered test dispatches.</summary>
    private static uint[] ReadWords(CloudVolumeFields fields,int offset,int count)
    {
        using var read=fields.Scratch.MapScope<uint>(offset*4,count,MapBufferAccessMask.MapReadBit);
        return read.Span.ToArray();
    }
    /// <summary>Transforms an authored physical texel centre into native-local coordinates.</summary>
    private static Vector3 Point(CloudVolumeDomain d,int x,int y,int z)
    {
        int lx=(x-d.SlotX+d.Size)%d.Size,lz=(z-d.SlotZ+d.Size)%d.Size;
        return new((float)((d.FirstX+lx+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorX),
            (float)(d.Bottom+(y-.5)*d.VerticalCell),(float)((d.FirstZ+lz+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorZ));
    }
    /// <summary>Reads one hardware-filtered physical diffuse value through the existing diagnostic program.</summary>
    private static Vector4 Query(CloudVolumeProbeProgram p,CloudVolumeFields fields,GpuShaderStorageBuffer result,Vector3 point)
    {
        p.Capture(fields,point);p.Density=fields.Density;p.Direct=fields.Direct;p.Diffuse=fields.Diffuse;p.Result=result;p.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var read=result.MapScope<Vector4>(0,2,MapBufferAccessMask.MapReadBit);return read.Span[1];
    }
    /// <summary>Inspects exact completed frames to detect any publication of a failed prefix.</summary>
    private static uint[] Completed(CloudVolumeFields fields)
    {
        using var read=fields.Regions.MapScope<uint>(0,fields.Domain.Blocks*16,MapBufferAccessMask.MapReadBit);
        var values=new uint[fields.Domain.Blocks*3];
        for(int i=0;i<fields.Domain.Blocks;i++)
        {values[i*3]=read.Span[i*16+10];values[i*3+1]=read.Span[i*16+11];values[i*3+2]=read.Span[i*16+15];}
        return values;
    }
    #endregion
}
