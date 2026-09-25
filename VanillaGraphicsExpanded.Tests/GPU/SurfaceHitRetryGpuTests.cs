using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises retained geometric hits and cache-only completion through packaged production shaders.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceHitRetryGpuTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    private const int HeaderBytes=544,RecordBytes=4176,Capacity=16;

    #region Retained hit completion
    /// <summary>Unavailable lighting retains every geometric hit and later resolves without another geometry dispatch.</summary>
    [Fact]
    public void CapturedHitsCompleteAfterLightingPublicationWithoutRetrace()
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture();
        room.Seed();room.SetIndirect(7,4);room.WithholdPages();using var queue=Queue();
        var before=room.Read(room.Snapshot.IndirectIrradiance);
        Assert.False(room.BounceSample(rays:4,hitRetries:queue,captureHitRetries:true));
        var retained=Read(queue);Assert.Equal(4,retained.Queries.Length);
        Assert.Equal(4,retained.Texels[0].Request.Fraction.W);
        Assert.Equal(before,room.Read(room.Snapshot.IndirectIrradiance));
        using var assets=new BinaryShaderApiFixture();using var queries=new SurfaceLightingQueryBatch(assets.Api);
        queries.Submit(room.Geometry.Scene,room.Snapshot,retained.Queries.AsSpan());
        GpuTestFence.WaitForGpuOrSkip("Unavailable retained lighting query");
        Assert.True(queries.TryRead(out var missing));Assert.Empty(SurfaceFallbackEstimates.Resolve(retained,missing));
        ulong rays=Counter(room,SurfaceWorkCounter.Rays);
        room.RestorePageReadiness();queries.Submit(room.Geometry.Scene,room.Snapshot,retained.Queries.AsSpan());
        GpuTestFence.WaitForGpuOrSkip("Ready retained lighting query");Assert.True(queries.TryRead(out var ready));
        var commit=Assert.Single(SurfaceFallbackEstimates.Resolve(retained,ready));Assert.True(commit.Estimate.X>0);
        room.CommitFallback([commit]);Assert.Equal(rays,Counter(room,SurfaceWorkCounter.Rays));
        var result=room.Read(room.Snapshot.IndirectIrradiance);
        float expected=(7*4+commit.Estimate.X)/5;
        Assert.InRange(result[0],expected-.04f,expected+.04f);Assert.Equal(4,result[3]);
    }

    /// <summary>Retained texels suppress ordinary ray work while direct refresh still updates their displayed lighting.</summary>
    [Fact]
    public void PendingGeometrySkipsRayWorkWithoutSuppressingDirectRefresh()
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture();room.Seed();room.WithholdPages();
        using var queue=Queue();Assert.False(room.BounceSample(rays:4,hitRetries:queue,captureHitRetries:true));
        var request=Read(queue).Texels[0].Request;ulong rays=Counter(room,SurfaceWorkCounter.Rays);
        queue.UploadSubData<uint>([1,0,0,0,request.Page,request.Slot,request.Patch,request.Linear],16,32);
        var before=room.Read(room.Snapshot.IndirectIrradiance);
        room.BounceSample(rays:4,hitRetries:queue,captureHitRetries:false);
        Assert.Equal(rays,Counter(room,SurfaceWorkCounter.Rays));Assert.Equal(before,room.Read(room.Snapshot.IndirectIrradiance));
        var direct=room.Read(room.Snapshot.DirectIrradiance);
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();
        room.DispatchDiagnosticWork(4,pageCount:1,hitRetries:queue);
        GpuTestFence.WaitForGpuOrSkip("Direct refresh beside retained hits");
        Assert.True(room.Read(room.Snapshot.DirectIrradiance)[0]<direct[0]);
    }

    /// <summary>Sixteen retained batches bound GPU writes even when every selected texel hits unavailable lighting.</summary>
    [Fact]
    public void AdmissionCapacityPreservesCanaryAndHistory()
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture();room.Seed();room.WithholdPages();
        using var queue=Queue(pages:4);room.DispatchDiagnosticWork(1,rays:4,steps:256,hitRetries:queue,captureHitRetries:true);
        GpuTestFence.WaitForGpuOrSkip("Bounded retained hit capture");
        using(var header=queue.MapRange<uint>(0,4,MapBufferAccessMask.MapReadBit))
        { Assert.True(header.IsMapped);Assert.InRange(header.Span[0],1u,16u); }
        using var guard=queue.MapRange<uint>(HeaderBytes+Capacity*RecordBytes,4,MapBufferAccessMask.MapReadBit);
        Assert.True(guard.IsMapped);foreach(uint word in guard.Span)Assert.Equal(0xfeedabcdU,word);
        for(int page=0;page<4;page++)Assert.Equal(0,room.Read(room.Snapshot.IndirectIrradiance,page)[3]);
    }

    /// <summary>Unfinished traversal must not masquerade as a complete retained hit estimator.</summary>
    [Fact]
    public void BudgetExhaustionDoesNotProduceRetainedGeometry()
    {
        EnsureContextValid();using var room=new SurfaceLightingEnclosureFixture();room.Seed();room.WithholdPages();
        using var queue=Queue();Assert.False(room.BounceSample(rays:4,steps:0,hitRetries:queue,captureHitRetries:true));
        GpuTestFence.WaitForGpuOrSkip("Incomplete retained geometry");
        uint count;
        using(var header=queue.MapRange<uint>(0,4,MapBufferAccessMask.MapReadBit))
        { Assert.True(header.IsMapped);count=header.Span[0]; }
        if(count>0)
        {
            using var state=queue.MapRange<uint>(HeaderBytes+64,4,MapBufferAccessMask.MapReadBit);
            Assert.True(state.IsMapped);Assert.Equal(0u,state.Span[0]);
        }
    }
    #endregion

    #region Buffer and diagnostic fixtures
    /// <summary>Allocates the production bounded record layout with a guard beyond all writable records.</summary>
    private static GpuShaderStorageBuffer Queue(uint pages=1)
    {
        var buffer=GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw);
        var words=new uint[(HeaderBytes+Capacity*RecordBytes+16)>>2];words[1]=Capacity;words[2]=pages;words[3]=37;
        Array.Fill(words,0xfeedabcdU,words.Length-4,4);
        buffer.EnsureCapacity(words.Length<<2,growExponentially:false);buffer.UploadSubData<uint>(words,0,words.Length<<2);return buffer;
    }

    /// <summary>Reads exactly one completed geometric record, keeping integer hit descriptors intact.</summary>
    private static SurfaceFallbackResult Read(GpuShaderStorageBuffer queue)
    {
        GpuTestFence.WaitForGpuOrSkip("Retained geometric hit readback");
        using(var header=queue.MapRange<uint>(0,4,MapBufferAccessMask.MapReadBit))
        { Assert.True(header.IsMapped);Assert.Equal(1u,header.Span[0]); }
        SurfaceFallbackRequest request;
        using(var record=queue.MapRange<SurfaceFallbackRequest>(HeaderBytes,1,MapBufferAccessMask.MapReadBit))
        { Assert.True(record.IsMapped);request=record.Span[0]; }
        int count;
        using(var state=queue.MapRange<uint>(HeaderBytes+64,4,MapBufferAccessMask.MapReadBit))
        { Assert.True(state.IsMapped);Assert.Equal(1u,state.Span[0]);count=(int)state.Span[1]; }
        using var hits=queue.MapRange<SurfaceLightingQuery>(HeaderBytes+80,count,MapBufferAccessMask.MapReadBit);
        Assert.True(hits.IsMapped);return new([new(request,0,count,true)],hits.Span.ToArray().ToImmutableArray(),[]);
    }

    /// <summary>Waits in the test harness before reading asynchronous production ray counters.</summary>
    private static ulong Counter(SurfaceLightingEnclosureFixture room,SurfaceWorkCounter counter)
    {
        GpuTestFence.WaitForGpuOrSkip("Retained hit trace diagnostics");room.Diagnostics.Poll();
        return room.Diagnostics.Snapshot(SurfaceWorkStage.Indirect).Counters[(int)counter];
    }
    #endregion
}
