using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks isolation and exact dirty-block publication without image-quality or timing thresholds.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumePublicationTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
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
        using var publication=new CloudVolumePublication(()=>CloudVolumePublishProgram.Create(Programs.Api));
        Complete();publication.Publish(fields,0,storage.StorageBytes);
        var old=publication.Published!;Assert.NotSame(fields.Density,old.Density);Assert.NotSame(fields.Regions,old.Regions);
        using var probe=CloudVolumeProbeProgram.Create(Programs.Api);
        using var result=GpuShaderStorageBuffer.Create();result.UploadData(new Vector4[2]);
        var before=Sample(probe,result,old,Point(d,4));Assert.Equal(1,before[0].X);Assert.Equal(1,before[0].Y);
        fields.Density.UploadDataImmediate(Enumerable.Repeat(2f,d.Voxels).ToArray(),0,0,0,d.Size,66,d.Size);
        fields.Direct.UploadDataImmediate(Enumerable.Repeat(.5f,d.Voxels*2).ToArray(),0,0,0,d.Size,66,d.Size);
        var records=new uint[d.Blocks*16];records[0]=4096;fields.Regions.UploadData(records);
        Complete();Assert.Equal(before,Sample(probe,result,old,Point(d,4)));
        publication.Publish(fields,0,storage.StorageBytes);
        var after=Sample(probe,result,publication.Published!,Point(d,4));
        Assert.Equal(2,after[0].X);Assert.Equal(before[0].Y,after[0].Y);Assert.Equal(before[1],after[1]);
        Assert.Equal(1,Sample(probe,result,publication.Published!,Point(d,12))[0].X);
        using(var flags=fields.Regions.MapScope<uint>(0,1,MapBufferAccessMask.MapReadBit))Assert.Equal(0u,flags.Span[0]);
        Assert.Equal(old.Revision+1,publication.Published!.Revision);
        var maximum=new CloudVolumeDomain(200,default,-100,100);
        Assert.True(maximum.Bytes*2+CloudVolumePublication.Payload(maximum)+16L*200*200<CloudVolumeDomain.PeakLimit);
        var retired=publication.Published.Density;publication.Dispose();Assert.Null(publication.Published);Assert.False(retired.IsValid);
    }
    #endregion
    #region Private
    /// <summary>Waits explicitly in this small GPU fixture; live publication uses zero-timeout polling.</summary>
    private static void Complete()
    {
        using var fence=GpuFence.Insert();
        Assert.Contains(fence.Wait(TimeSpan.FromSeconds(1)),new[]{WaitSyncStatus.AlreadySignaled,WaitSyncStatus.ConditionSatisfied});
    }
    /// <summary>Places a lookup exactly at the center of a selected physical texel.</summary>
    private static Vector3 Point(CloudVolumeDomain d,int x)=>new(
        (float)((d.FirstX+CloudVolumeDomain.Mod(x-d.SlotX,d.Size)+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorX),
        (float)(d.Bottom+1.5*d.VerticalCell),
        (float)((d.FirstZ+CloudVolumeDomain.Mod(4-d.SlotZ,d.Size)+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorZ));
    /// <summary>Samples the same hardware-filtered fields used by the actual cloud view.</summary>
    private static Vector4[] Sample(CloudVolumeProbeProgram probe,GpuShaderStorageBuffer result,CloudVolumeViewFields fields,Vector3 point)
    {
        probe.Capture(fields,point);probe.Density=fields.Density;probe.Direct=fields.Direct;probe.Diffuse=fields.Diffuse;probe.Result=result;
        probe.Dispatch(1);GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var read=result.MapScope<Vector4>(0,2,MapBufferAccessMask.MapReadBit);return read.Span.ToArray();
    }
    #endregion
}
