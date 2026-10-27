using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks isolation and exact dirty-block publication without image-quality or timing thresholds.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumePublicationTests(HeadlessGLFixture fixture,Xunit.ITestOutputHelper output):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Unpublished writes cannot affect the drawable snapshot, and publication copies only flagged channels/blocks.</summary>
    [Fact]
    public void DrawableSnapshotIsIsolatedAndPublishesOnlyChangedBlocks()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        var d=new CloudVolumeDomain(5,default,-100,100);
        storage.Request(d,new(new object(),1,1),new(new Vector3(100)),new Vector3(4),()=>true);
        while(!storage.InitializeNext(()=>true)){}Assert.True(storage.PublishPending(()=>true,()=>true));
        var fields=storage.Published;
        fields.Density.UploadDataImmediate(Enumerable.Repeat(1f,d.Voxels).ToArray(),0,0,0,d.Size,66,d.Size);
        using var publication=new CloudVolumePublication((op,shared)=>CloudVolumePublishProgram.Create(Programs.Api,op,shared));
        Complete();publication.Publish(fields,0,storage.StorageBytes);
        var old=publication.Published!;Assert.NotSame(fields.Density,old.Density);Assert.NotSame(fields.Regions,old.Regions);
        using var probe=CloudVolumeProbeProgram.Create(Programs.Api);
        using var result=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();result.UploadData(new Vector4[2]);
        var before=Sample(probe,result,old,Point(d,4));Assert.Equal(1,before[0].X);Assert.Equal(1,before[0].Y);
        fields.Density.UploadDataImmediate(Enumerable.Repeat(2f,d.Voxels).ToArray(),0,0,0,d.Size,66,d.Size);
        fields.Direct.UploadDataImmediate(Enumerable.Repeat(.5f,d.Voxels*2).ToArray(),0,0,0,d.Size,66,d.Size);
        var records=new uint[d.Blocks*16];records[0]=4096;fields.Regions.UploadData(records);
        Complete();Assert.Equal(before,Sample(probe,result,old,Point(d,4)));
        publication.Publish(fields,0,storage.StorageBytes);
        var after=Sample(probe,result,publication.Published!,Point(d,4));
        Assert.Equal(2,after[0].X);Assert.Equal(before[0].Y,after[0].Y);Assert.Equal(before[1],after[1]);
        Assert.Equal(1,Sample(probe,result,publication.Published!,Point(d,12))[0].X);
        using(var flags=fields.Regions.MapScope<uint>(0,1,BufferMapAccess.Read))Assert.Equal(0u,flags.Span[0]);
        Assert.Equal(old.Revision+1,publication.Published!.Revision);
        Assert.Equal(new uint[]{1,1,1,1},Arguments(publication));Assert.Equal(0,publication.FullChannels);
        // A metadata-only publication must update every word and occupancy without touching images.
        Array.Clear(records);records[16]=2;records[20]=77;fields.Regions.UploadData(records);
        var occupancy=Enumerable.Repeat(1u,d.Blocks).ToArray();occupancy[1]=0;fields.Occupancy.UploadData(occupancy);
        using(var persistent=fields.Scratch.MapScope<uint>(0,40,BufferMapAccess.Write))
        {persistent.Span[5]=17;persistent.Span[18]=19;persistent.Span[19]=23;persistent.Span[38]=1;persistent.Span[39]=29;}
        using(var repair=fields.Scratch.MapScope<uint>(17280*4,1,BufferMapAccess.Write))repair.Span[0]=31;
        publication.Publish(fields,0,storage.StorageBytes);Complete();
        Assert.Equal(new uint[]{0,1,1,0},Arguments(publication));
        using(var read=publication.Published!.Regions.MapScope<uint>(0,records.Length,BufferMapAccess.Read))Assert.Equal(records,read.Span.ToArray());
        using(var read=publication.Published!.Occupancy.MapScope<uint>(0,d.Blocks,BufferMapAccess.Read))Assert.Equal(occupancy,read.Span.ToArray());
        Assert.Equal(after,Sample(probe,result,publication.Published!,Point(d,4)));
        using(var persistent=fields.Scratch.MapScope<uint>(0,40,BufferMapAccess.Read))
        {Assert.Equal(17u,persistent.Span[5]);Assert.Equal(19u,persistent.Span[18]);Assert.Equal(23u,persistent.Span[19]);Assert.Equal(1u,persistent.Span[38]);Assert.Equal(29u,persistent.Span[39]);}
        using(var repair=fields.Scratch.MapScope<uint>(17280*4,1,BufferMapAccess.Read))Assert.Equal(31u,repair.Span[0]);
        // More than one indirect row, with a short final Y block, exercises uniform padded exits.
        fields.Density.Clear(3);fields.Direct.Clear(.25f,.75f);fields.Diffuse.Clear(5,5,5);
        for(int r=0;r<d.Blocks;r++)records[r*16]=4096|8192|16384;
        fields.Regions.UploadData(records);publication.Publish(fields,0,storage.StorageBytes);Complete();
        Assert.Equal(new uint[]{64,(uint)((d.Blocks+63)/64),1,(uint)d.Blocks},Arguments(publication));
        var edge=Sample(probe,result,publication.Published!,Point(d,d.Size-1,65,d.Size-1));
        Assert.Equal(new Vector4(3,.25f,.75f,0),edge[0]);Assert.Equal(new Vector4(new Vector3(5*fields.Encoding.Scale),0),edge[1]);
        using(var flags=fields.Regions.MapScope<uint>(0,records.Length,BufferMapAccess.Read))
            for(int r=0;r<d.Blocks;r++)Assert.Equal(0u,flags.Span[r*16]&(4096u|8192u|16384u));
        // Exceptional masks copy their whole channel and still honor other dirty channels.
        Array.Clear(records);records[0]=4096;fields.Regions.UploadData(records);
        fields.Density.Clear(4);fields.Direct.Clear(.5f,.5f);
        publication.Publish(fields,1,storage.StorageBytes);Assert.Equal(2,publication.FullChannels);
        Assert.Equal(new Vector4(4,.5f,.5f,0),Sample(probe,result,publication.Published!,Point(d,4))[0]);
        Assert.Equal(3,Sample(probe,result,publication.Published!,Point(d,12))[0].X);
        fields.Encoding=new(new Vector3(1000));fields.Diffuse.Clear(7,7,7);
        publication.Publish(fields,0,storage.StorageBytes);Assert.Equal(4,publication.FullChannels);
        Assert.Equal(new Vector4(new Vector3(7*fields.Encoding.Scale),0),Sample(probe,result,publication.Published!,Point(d,12))[1]);
        // A changed domain and a new working owner both require a complete coherent publication.
        fields.Domain=new CloudVolumeDomain(5,default(VanillaGraphicsExpanded.PBR.Clouds.CloudPlacement) with{CenterX=1},-100,100);
        publication.Publish(fields,0,storage.StorageBytes);Assert.Equal(7,publication.FullChannels);
        using(var replacement=new CloudVolumeFields(d,fields.Encoding))
        {
            replacement.Density.Clear(6);replacement.Direct.Clear(1,1);replacement.Diffuse.Clear();
            publication.Publish(replacement,0,replacement.Domain.Bytes);Assert.Equal(7,publication.FullChannels);
            Assert.Equal(6,Sample(probe,result,publication.Published!,Point(d,12))[0].X);
        }
        var maximum=new CloudVolumeDomain(200,default,-100,100);
        Assert.True(maximum.Bytes*2+CloudVolumePublication.Payload(maximum)+16L*200*200<CloudVolumeDomain.PeakLimit);
        var retired=publication.Published.Density;publication.Dispose();Assert.Null(publication.Published);Assert.False(retired.IsValid);
        MeasurePublicationCost();
    }
    #endregion
    #region Private
    /// <summary>Records owner publication cost with allocation, uploads and test waits outside timing.</summary>
    private void MeasurePublicationCost()
    {
        var domain=new CloudVolumeDomain(200,default,-100,100);
        var shifted=new CloudVolumeDomain(200,default(VanillaGraphicsExpanded.PBR.Clouds.CloudPlacement) with{CenterX=1},-100,100);
        using var fields=new CloudVolumeFields(domain,new(Vector3.One));
        fields.Density.Clear(.005f);fields.Direct.Clear(.75f,.5f);fields.Diffuse.Clear(.25f,.5f,.75f);
        fields.Occupancy.UploadData(Enumerable.Repeat(1u,domain.Blocks).ToArray());
        using var owner=new CloudVolumePublication((op,shared)=>CloudVolumePublishProgram.Create(Programs.Api,op,shared));
        owner.Publish(fields,0,domain.Bytes);Complete();
        var records=new uint[domain.Blocks*16];
        int[] dirty=Enumerable.Range(0,32).ToArray();
        long dirtyVoxels=dirty.Sum(r=>(long)Math.Min(8,domain.Size-r%domain.BlocksX*8)*
            Math.Min(4,66-(r/domain.BlocksX)%domain.BlocksY*4)*
            Math.Min(8,domain.Size-r/(domain.BlocksX*domain.BlocksY)*8));
        using var timer=GpuTimerQuery.Create("Tests.Cloud.Publication");
        output.WriteLine($"GPU: {fixture.GLRenderer}. Maximum {domain.Size}x66x{domain.Size}; game remains running, contention uncontrolled. Owner.Publish includes metadata and image work; allocation, uploads, domain changes and test waits outside timing. GPU brackets include submission gaps; no live FPS claim.");
        foreach(string scenario in new[]{"32 dirty blocks/all channels","metadata only","required full domain change"})
        {
            var gpu=new List<double>();var cpu=new List<double>();
            for(int sample=0;sample<4;sample++)
            {
                Array.Clear(records);
                if(scenario=="32 dirty blocks/all channels")foreach(int r in dirty)records[r*16]=4096|8192|16384;
                for(int r=0;r<domain.Blocks;r++)records[r*16+1]=(uint)(sample+1);
                fields.Regions.UploadData(records);
                fields.Domain=scenario=="required full domain change"?(sample%2==0?shifted:domain):domain;
                Complete();timer.Begin();long started=System.Diagnostics.Stopwatch.GetTimestamp();
                owner.Publish(fields,0,domain.Bytes);
                double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;timer.End();
                double milliseconds=timer.GetResultNanoseconds()/1e6;
                if(sample>0){gpu.Add(milliseconds);cpu.Add(elapsed);}
            }
            long bytes=scenario=="metadata only"?0:28L*(scenario=="required full domain change"?domain.Voxels:dirtyVoxels);
            output.WriteLine($"Publication {scenario}: GPU samples={string.Join(", ",gpu.Select(x=>x.ToString("F4")))} ms, median={gpu.Order().ElementAt(1):F4} ms; CPU samples={string.Join(", ",cpu.Select(x=>x.ToString("F4")))} ms, median={cpu.Order().ElementAt(1):F4} ms; logical channel read+write={bytes} bytes, metadata input/output={domain.Blocks*136L} bytes; one warmup plus three samples.");
            uint[] arguments=Arguments(owner);output.WriteLine($"Publication arguments XYZ/count={string.Join(",",arguments)}, full channels={owner.FullChannels}.");
        }
        // Exercise the maximum compact-list bound and last partial corner using already allocated fields.
        for(int r=0;r<domain.Blocks;r++)records[r*16]=4096|8192|16384;
        fields.Regions.UploadData(records);owner.Publish(fields,0,domain.Bytes);Complete();
        Assert.Equal(new uint[]{64,1123,1,71825},Arguments(owner));
    }
    /// <summary>Waits explicitly in this small GPU fixture; live publication uses zero-timeout polling.</summary>
    private static void Complete()
    {
        using var fence=RHICommandContext.Current.InsertFence();
        Assert.Contains(RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(1)),new[]{CompletionStatus.Ready,CompletionStatus.Ready});
    }
    /// <summary>Places a lookup exactly at the center of a selected physical texel.</summary>
    private static Vector3 Point(CloudVolumeDomain d,int x,int y=2,int z=4)=>new(
        (float)((d.FirstX+CloudVolumeDomain.Mod(x-d.SlotX,d.Size)+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorX),
        (float)(d.Bottom+(y-.5)*d.VerticalCell),
        (float)((d.FirstZ+CloudVolumeDomain.Mod(z-d.SlotZ,d.Size)+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorZ));
    /// <summary>Reads produced arguments only after an explicit test wait, outside measured intervals.</summary>
    private static uint[] Arguments(CloudVolumePublication owner)
    {
        Complete();using var read=owner.Arguments.MapScope<uint>(0,4,BufferMapAccess.Read);
        return read.Span.ToArray();
    }
    /// <summary>Samples the same hardware-filtered fields used by the actual cloud view.</summary>
    private static Vector4[] Sample(CloudVolumeProbeProgram probe,GpuShaderStorageBuffer result,CloudVolumeViewFields fields,Vector3 point)
    {
        probe.Capture(fields,point);probe.Density=fields.Density;probe.Direct=fields.Direct;probe.Diffuse=fields.Diffuse;probe.Result=result;
        probe.Dispatch(1);RHICommandContext.Current.Barrier(new(ResourceAccess.ShaderStorage|ResourceAccess.BufferUpdate));
        using var read=result.MapScope<Vector4>(0,2,BufferMapAccess.Read);return read.Span.ToArray();
    }
    #endregion
}
