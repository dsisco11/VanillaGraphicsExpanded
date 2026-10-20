using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks declared voxel overlap, owned history, bounded updates and safe occupancy through real compute programs.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeDensityTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Thin intersections contribute cell-average extinction, changes coalesce and filtered support stays occupied.</summary>
    [Fact]
    public void StoredDensityUsesCoverageAndRetainsUnchangedHistory()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        using var density=new CloudVolumeDensity(op=>CloudVolumeDensityProgram.Create(Programs.Api,op,false));
        using var map=DynamicTexture2D.Create(20,20,PixelInternalFormat.Rgba32f);
        var data=new float[20*20*4];int column=(10*20+10)*4;data[column]=.5f;data[column+3]=1f/50;
        map.UploadDataImmediate(data);
        var input=Input(map);var encoding=new CloudVolumeEncoding(new Vector3(100));
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        var fields=storage.Published;Assert.True(fields.DensityState.InitialComplete);
        using var probe=CloudVolumeProbeProgram.Create(Programs.Api);
        using var output=GpuShaderStorageBuffer.Create();output.UploadData(new Vector4[2]);
        Vector3 point=new((float)(.5*CloudVolumeDomain.HorizontalCell-fields.Domain.AnchorX),6.25f,
            (float)(.5*CloudVolumeDomain.HorizontalCell-fields.Domain.AnchorZ));
        AssertStoredRepresentation(.01f/12.5f,Sample(probe,fields,output,point));
        using(var words=fields.Occupancy.MapScope<uint>(0,fields.Domain.Blocks,MapBufferAccessMask.MapReadBit))
        {
            int last=fields.Domain.BlocksX-1,y=33/4;
            Assert.Equal(1u,words.Span[y*fields.Domain.BlocksX]);
            Assert.Equal(1u,words.Span[(last*fields.Domain.BlocksY+y)*fields.Domain.BlocksX+last]);
        }
        uint actual=fields.DensityState.CompletedFrame;
        input=input with{Frame=2,Generation=2,Upload=2};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Equal(0,fields.DensityState.Selected);Assert.Equal(actual,fields.DensityState.CompletedFrame);
        // A wind-only transform does not observe or regenerate stationary material content.
        uint revision=fields.DensityState.Revision;
        input=input with{Frame=3,Placement=input.Placement with{Offset=new Vector3(7,0,4),WindX=.7}};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Equal(revision,fields.DensityState.Revision);Assert.Equal(0,fields.DensityState.Selected);
        data[column]=1;map.UploadDataImmediate(data);input=input with{Frame=4,Generation=3,Upload=3};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.InRange(fields.DensityState.Selected,1,4096);
        AssertStoredRepresentation(.02f/12.5f,Sample(probe,fields,output,point));
        var retained=fields.Density;Assert.True(retained.IsValid);
        data[column]=-1;map.UploadDataImmediate(data);input=input with{Frame=5,Generation=4,Upload=4};
        Assert.False(density.Update(storage,input,map,encoding,Vector3.One,()=>true));
        Assert.False(storage.Ready);Assert.NotNull(density.Failure);Assert.True(map.IsValid);
    }

    /// <summary>A bounded incoming domain keeps its admitted snapshot even when live heights change mid-fill.</summary>
    [Fact]
    public void FrozenInitializationPublishesOnlyCompletedDensityAndDetailIsBounded()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        using var density=new CloudVolumeDensity(op=>CloudVolumeDensityProgram.Create(Programs.Api,op,true));
        using var map=DynamicTexture2D.Create(50,50,PixelInternalFormat.Rgba32f);
        var data=new float[50*50*4];int column=(25*50+25)*4;
        data[column]=.5f;data[column+2]=-2;data[column+3]=2;map.UploadDataImmediate(data);
        var input=Input(map);var encoding=new CloudVolumeEncoding(new Vector3(100));
        Assert.False(density.Update(storage,input,map,encoding,Vector3.One,()=>true));
        Assert.Null(density.Failure);Assert.NotNull(storage.Pending);Assert.Equal(4096,storage.Pending!.InitializedBlocks);
        uint frozen=storage.Pending.DensityState.SnapshotFrame;
        double bottom=storage.Pending.Domain.Bottom;
        data[column+2]=200;data[column+3]=202;map.UploadDataImmediate(data);
        input=input with{Frame=2,Generation=2,Upload=2};
        Assert.False(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Equal(frozen,storage.Pending!.DensityState.SnapshotFrame);Assert.Equal(bottom,storage.Pending.Domain.Bottom);
        Assert.InRange(storage.Pending.DensityState.Selected,1,4096);
        input=input with{Frame=3};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        var initial=storage.Published;Assert.Equal(frozen,initial.DensityState.SnapshotFrame);
        using var probe=CloudVolumeProbeProgram.Create(Programs.Api);
        using var output=GpuShaderStorageBuffer.Create();output.UploadData(new Vector4[2]);
        var point=new Vector3((float)(.5*CloudVolumeDomain.HorizontalCell-initial.Domain.AnchorX),6.25f,
            (float)(.5*CloudVolumeDomain.HorizontalCell-initial.Domain.AnchorZ));
        // The shaping contract is bounded0..1.15; this checks declared math, not visual approximation quality.
        Assert.InRange(Sample(probe,initial,output,point),0,HalfInterval(.0115f).Maximum);
        input=input with{Frame=4};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Same(initial,storage.Published);Assert.NotNull(storage.Pending);
        Assert.True(storage.Pending!.Domain.Bottom>initial.Domain.Bottom);
        // Missing/changed publication cannot commit any additional work or steal native ownership.
        Assert.False(density.Update(storage,input with{Frame=5},map,encoding,Vector3.One,()=>false));
        Assert.True(map.IsValid);Assert.Same(initial,storage.Published);
    }
    #endregion
    #region Private
    /// <summary>Creates a certified fixture identity without ticking or borrowing native weather ownership.</summary>
    private static CloudMapInput Input(GpuTexture map)=>new(new object(),1,1,1,1,1,1,
        new CloudMapStorage(map.TextureId,2,3,4,map.Width,50,5),default);
    /// <summary>Allows only the adjacent representable half values enclosing the declared arithmetic result.</summary>
    private static void AssertStoredRepresentation(float physical,float actual)
    {
        // Image storage can narrow differently from the CPU Half cast; this is representation
        // tolerance for a known analytic voxel average, not an approximation-quality threshold.
        var interval=HalfInterval(physical);Assert.InRange(actual,interval.Minimum,interval.Maximum);
    }
    /// <summary>Derives finite half endpoints directly rather than choosing an arbitrary numerical error limit.</summary>
    private static (float Minimum,float Maximum) HalfInterval(float physical)
    {
        Half value=(Half)physical;ushort bits=BitConverter.HalfToUInt16Bits(value);
        return ((float)value>physical?(float)BitConverter.UInt16BitsToHalf((ushort)(bits-1)):(float)value,
            (float)value<physical?(float)BitConverter.UInt16BitsToHalf((ushort)(bits+1)):(float)value);
    }
    /// <summary>Reads one actual hardware-filtered stored-density sample using the existing typed probe.</summary>
    private static float Sample(CloudVolumeProbeProgram probe,CloudVolumeFields fields,GpuShaderStorageBuffer output,Vector3 point)
    {
        probe.Capture(fields,point);probe.Density=fields.Density;probe.Direct=fields.Direct;probe.Diffuse=fields.Diffuse;probe.Result=output;
        probe.Dispatch(1);GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit);
        using var read=output.MapScope<Vector4>(0,2,MapBufferAccessMask.MapReadBit);return read.Span[0].X;
    }
    #endregion
}
