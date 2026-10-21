using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies stored-density transport arithmetic and retained source/publication behavior on the actual GPU path.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeDirectTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Finite-domain transmission is reusable across intensity changes and failed work cannot certify partial output.</summary>
    [Fact]
    public void DirectTransportUsesStoredDensityAndPublishesOnlySuccessfulBatches()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        var d=new CloudVolumeDomain(5,default,-100,100);
        storage.Request(d,new(new object(),1,1),new(Vector3.One),Vector3.Zero,()=>true);
        while(!storage.InitializeNext(()=>true)){}
        Assert.True(storage.PublishPending(()=>true,()=>true));var fields=storage.Published;
        const float sigma=.0078125f;
        var density=Enumerable.Repeat(sigma,d.Voxels).ToArray();
        fields.Density.UploadDataImmediate(density,0,0,0,d.Size,66,d.Size);
        fields.Occupancy.UploadData(Enumerable.Repeat(1u,d.Blocks).ToArray());
        using var work=new CloudVolumeWork((op,large)=>CloudVolumeWorkProgram.Create(Programs.Api,op,large));
        using var solver=new CloudVolumeDirect(op=>CloudVolumeDirectProgram.Create(Programs.Api,op));
        var lighting=new AtmosphereLighting(Vector3.UnitY,Vector3.One,Vector3.One,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
            {Moon=-Vector3.UnitX,Lunar=Vector3.One};
        var planes=Enumerable.Repeat(new Vector4(0,0,0,1),6).ToArray();
        using var probe=CloudVolumeProbeProgram.Create(Programs.Api);
        using var output=GpuShaderStorageBuffer.Create();output.UploadData(new Vector4[2]);
        fields.Direct.UploadDataImmediate(Enumerable.Repeat(.25f,d.Voxels*2).ToArray(),0,0,0,d.Size,66,d.Size);
        for(uint frame=1;frame<=5;frame++)
        {
            Assert.True(work.Select(storage,lighting,frame,planes,()=>true),work.Failure);
            Assert.True(solver.Update(storage,work,()=>true),solver.Failure);
            Assert.True(solver.Update(storage,work,()=>true));
            if(frame==1)
            {
                // The final physical block is outside the first bounded list; activation still resets its stale channel.
                Assert.Equal(0u,Word(fields.Regions,(d.Blocks-1)*16+6));
                int logical=(9-d.SlotX+d.Size)%d.Size;
                var pendingPoint=new Vector3((float)((d.FirstX+logical+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorX),
                    (float)(d.Bottom+63.5*d.VerticalCell),(float)((d.FirstZ+logical+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorZ));
                var fallback=Query(probe,fields,output,pendingPoint);Assert.Equal(1,fallback.Y);Assert.Equal(1,fallback.Z);
            }
        }
        // A true texel centre avoids conflating transport arithmetic with spatial reconstruction.
        int lx=8,lz=8,y=32;
        var point=new Vector3((float)((d.FirstX+lx+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorX),
            (float)(d.Bottom+(y-.5)*d.VerticalCell),(float)((d.FirstZ+lz+.5)*CloudVolumeDomain.HorizontalCell-d.AnchorZ));
        var value=Query(probe,fields,output,point);
        float solar=MathF.Exp(-sigma*((float)(d.Bottom+d.Band)-point.Y));
        float lunar=MathF.Exp(-sigma*point.X);
        // Two half representational steps allow float32 accumulation/exp narrowing; this is not an approximation gate.
        Assert.InRange(Math.Abs((int)BitConverter.HalfToUInt16Bits((Half)solar)-(int)BitConverter.HalfToUInt16Bits((Half)value.Y)),0,2);
        Assert.InRange(Math.Abs((int)BitConverter.HalfToUInt16Bits((Half)lunar)-(int)BitConverter.HalfToUInt16Bits((Half)value.Z)),0,2);
        lighting=lighting with{Solar=new Vector3(10),Environment=new Vector3(2)};
        Assert.True(work.Select(storage,lighting,6,planes,()=>true));
        Assert.Equal(0u,Word(fields.Scratch,16));
        Assert.True(solver.Update(storage,work,()=>true));Assert.Equal(value,Query(probe,fields,output,point));
        lighting=lighting with{Sun=Vector3.UnitX};
        Assert.True(work.Select(storage,lighting,7,planes,()=>true));
        Assert.Equal(value,Query(probe,fields,output,point)); // Compatible stale transmission remains readable.
        Assert.True(solver.Update(storage,work,()=>true));
        Assert.Equal(value.Z,Query(probe,fields,output,point).Z); // Ordered solar writes preserve lunar transmission.
        lighting=lighting with{Lunar=Vector3.Zero};
        Assert.True(work.Select(storage,lighting,8,planes,()=>true,directEnabled:false));
        using(var disabled=new CloudVolumeDirect(_=>throw new InvalidOperationException("Disabled transport compiled a program.")))
            Assert.True(disabled.Update(storage,work,()=>true));
        lighting=lighting with{Lunar=Vector3.One};
        Assert.True(work.Select(storage,lighting,9,planes,()=>true));
        uint stamp=Word(fields.Regions,10);
        Array.Fill(density,float.NaN);fields.Density.UploadDataImmediate(density,0,0,0,d.Size,66,d.Size);
        Assert.False(solver.Update(storage,work,()=>true));Assert.False(storage.Ready);
        Assert.Equal(stamp,Word(fields.Regions,10));
    }
    #endregion
    #region Private
    /// <summary>Samples retained fields through the existing hardware-filtered diagnostic program.</summary>
    private static Vector4 Query(CloudVolumeProbeProgram p,CloudVolumeFields fields,GpuShaderStorageBuffer result,Vector3 point)
    {
        p.Capture(fields,point);p.Density=fields.Density;p.Direct=fields.Direct;p.Diffuse=fields.Diffuse;p.Result=result;p.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var read=result.MapScope<Vector4>(0,1,MapBufferAccessMask.MapReadBit);return read.Span[0];
    }
    /// <summary>Reads one exact metadata word without transferring the job list.</summary>
    private static uint Word(GpuShaderStorageBuffer buffer,int word)
    {using var read=buffer.MapScope<uint>(word*4,1,MapBufferAccessMask.MapReadBit);return read.Span[0];}
    #endregion
}
