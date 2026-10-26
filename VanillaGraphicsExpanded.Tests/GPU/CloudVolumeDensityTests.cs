using VanillaGraphicsExpanded.Rendering.RHI;
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
        var submission=new CloudVolumeSubmission();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api),submission);
        using var density=new CloudVolumeDensity((op,shared)=>CloudVolumeDensityProgram.Create(Programs.Api,op,false,shared),submission);
        using var map=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 20), Height = System.Math.Max(1, 20), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        var data=new float[20*20*4];int column=(10*20+10)*4;data[column]=.5f;data[column+3]=1f/50;
        map.UploadDataImmediate(data);
        var input=Input(map);var encoding=new CloudVolumeEncoding(new Vector3(100));
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        var fields=storage.Published;Assert.True(fields.DensityState.InitialComplete);
        using var probe=CloudVolumeProbeProgram.Create(Programs.Api);
        using var output=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();output.UploadData(new Vector4[2]);
        Vector3 point=new((float)(.5*CloudVolumeDomain.HorizontalCell-fields.Domain.AnchorX),6.25f,
            (float)(.5*CloudVolumeDomain.HorizontalCell-fields.Domain.AnchorZ));
        AssertStoredRepresentation(.01f/12.5f,Sample(probe,fields,output,point));
        using(var words=fields.Occupancy.MapScope<uint>(0,fields.Domain.Blocks,RHIBufferMapAccess.Read))
        {
            int last=fields.Domain.BlocksX-1,y=33/4;
            Assert.Equal(1u,words.Span[y*fields.Domain.BlocksX]&3u);
            Assert.Equal(1u,words.Span[(last*fields.Domain.BlocksY+y)*fields.Domain.BlocksX+last]&3u);
            // Origin counts describe stored positive cells, not neighboring traversal support.
            Assert.All(words.Span.ToArray(),value=>Assert.Equal(4u,value&4u));
            Assert.Equal(9u,words.Span.ToArray().Aggregate(0u,(sum,value)=>sum+((value>>3)&511u)));
            Assert.Contains(words.Span.ToArray(),value=>value==5u);
        }
        uint actual=fields.DensityState.CompletedFrame;
        // Consume the membership dirty flag as the scheduler does after building its retained list.
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,RHIBufferMapAccess.Write))membership.Span[0]=0;
        input=input with{Frame=2,Generation=2,Upload=2};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Equal(0,fields.DensityState.Selected);Assert.Equal(actual,fields.DensityState.CompletedFrame);
        Assert.Equal(0,fields.DensityState.Changed);Assert.False(fields.DensityState.MaintenanceRequired);
        // A wind-only transform does not observe or regenerate stationary material content.
        uint revision=fields.DensityState.Revision;int dispatches=submission.Dispatches;
        input=input with{Frame=3,Placement=input.Placement with{Offset=new Vector3(7,0,4),WindX=.7}};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Equal(revision,fields.DensityState.Revision);Assert.Equal(0,fields.DensityState.Selected);
        Assert.Equal(dispatches,submission.Dispatches);Assert.Equal(0,fields.DensityState.Changed);
        data[column]=1;map.UploadDataImmediate(data);input=input with{Frame=4,Generation=3,Upload=3};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.InRange(fields.DensityState.Selected,1,4096);
        // A one-metre layer needs only its intersecting vertical blocks plus the margin,
        // rather than every Y block for each connected horizontal neighbor.
        Assert.InRange(fields.DensityState.Selected,1,fields.Domain.BlocksX*fields.Domain.BlocksX);
        Assert.InRange(fields.DensityState.Changed,1,fields.DensityState.Selected);
        AssertStoredRepresentation(.02f/12.5f,Sample(probe,fields,output,point));
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,RHIBufferMapAccess.Read))Assert.Equal(0u,membership.Span[0]);
        // Move the layer to disjoint support, ensuring old cells are cleared as new ones fill.
        data[column+2]=2;data[column+3]=2+1f/50;map.UploadDataImmediate(data);
        input=input with{Frame=6,Generation=5,Upload=5};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Equal(0,Sample(probe,fields,output,point));
        AssertStoredRepresentation(.02f/12.5f,Sample(probe,fields,output,point+new Vector3(0,100,0)));
        Assert.InRange(fields.DensityState.Changed,1,fields.DensityState.Selected);
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,RHIBufferMapAccess.Read))Assert.Equal(1u,membership.Span[0]);
        // Recenter an already-empty field: changed transport coverage still dirties lighting,
        // and entering/guard occupancy must repair even when no generated texel changes.
        Array.Clear(data);
        // Both native values remain far below the smallest R16F extinction, even at full
        // coverage. Unlike a near-rounding-boundary perturbation, this guarantees equal storage.
        data[column]=1e-10f;data[column+3]=1f/50;
        map.UploadDataImmediate(data);input=input with{Frame=7,Generation=6,Upload=6};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        using(var dirty=fields.Scratch.MapScope<uint>(6*4,1,RHIBufferMapAccess.Write))dirty.Span[0]=0;
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,RHIBufferMapAccess.Write))membership.Span[0]=0;
        data[column]=2e-10f;map.UploadDataImmediate(data);input=input with{Frame=8,Generation=7,Upload=7};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.True(fields.DensityState.Selected>0);Assert.Equal(0,fields.DensityState.Changed);
        using(var dirty=fields.Scratch.MapScope<uint>(6*4,1,RHIBufferMapAccess.Read))Assert.Equal(0u,dirty.Span[0]);
        input=input with{Frame=9,Placement=input.Placement with{CenterX=1}};
        Assert.True(density.Update(storage,input,map,encoding,Vector3.One,()=>true),density.Failure);
        Assert.Equal(0,fields.DensityState.Changed);Assert.False(fields.DensityState.LayoutChanged);
        using(var dirty=fields.Scratch.MapScope<uint>(6*4,1,RHIBufferMapAccess.Read))Assert.Equal(1u,dirty.Span[0]);
        using(var occupied=fields.Occupancy.MapScope<uint>(0,fields.Domain.Blocks,RHIBufferMapAccess.Read))
            Assert.All(occupied.Span.ToArray(),value=>Assert.Equal(4u,value));
        using(var membership=fields.Scratch.MapScope<uint>(48*4,1,RHIBufferMapAccess.Read))Assert.Equal(0u,membership.Span[0]);
        var retained=fields.Density;Assert.True(retained.IsValid);
        data[column]=-1;map.UploadDataImmediate(data);input=input with{Frame=10,Generation=8,Upload=8};
        Assert.False(density.Update(storage,input,map,encoding,Vector3.One,()=>true));
        Assert.False(storage.Ready);Assert.NotNull(density.Failure);Assert.True(map.IsValid);
    }

    /// <summary>A bounded incoming domain keeps its admitted snapshot even when live heights change mid-fill.</summary>
    [Fact]
    public void FrozenInitializationPublishesOnlyCompletedDensityAndDetailIsBounded()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        using var density=new CloudVolumeDensity((op,shared)=>CloudVolumeDensityProgram.Create(Programs.Api,op,true,shared));
        using var map=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 50), Height = System.Math.Max(1, 50), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
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
        using var output=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();output.UploadData(new Vector4[2]);
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
        probe.Dispatch(1);RHICommandContext.Current.Barrier(new(RHIAccess.BufferUpdate|RHIAccess.ShaderStorage));
        using var read=output.MapScope<Vector4>(0,2,RHIBufferMapAccess.Read);return read.Span[0].X;
    }
    #endregion
}
