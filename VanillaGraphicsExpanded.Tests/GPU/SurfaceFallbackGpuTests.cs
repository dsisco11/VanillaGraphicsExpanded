using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises fallback admission and retained estimator commits through packaged production shaders.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceFallbackGpuTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Admission
    /// <summary>Only unsupported or outside geometry enqueues a CPU retry; established failures keep retained history.</summary>
    [Theory]
    [InlineData("coverage",true)] [InlineData("unsupported",true)] [InlineData("origin-outside",true)]
    [InlineData("unpublished",false)] [InlineData("budget",false)] [InlineData("missing-light",false)] [InlineData("sky",false)]
    public void QueueOnlyRetriesGeometryCoverageAndUnsupportedShapes(string scenario,bool expected)
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:35);
        floor.Seed();floor.SetIndirect(7,4);var before=floor.Read(floor.Snapshot.IndirectIrradiance);
        Configure(floor,scenario);
        using var queue=Queue(16,1,37);
        bool complete=floor.BounceSample(rays:1,steps:scenario=="budget"?0u:256u,fallbackRequests:queue);
        GpuTestFence.WaitForGpuOrSkip("Fallback admission");
        using(var header=queue.MapRange<uint>(0,4,MapBufferAccessMask.MapReadBit))
        { Assert.True(header.IsMapped);Assert.Equal(expected?1u:0u,header.Span[0]); }
        Assert.Equal(scenario=="sky",complete);
        if(scenario!="sky")Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
        if(expected)
        {
            using var request=queue.MapRange<SurfaceFallbackRequest>(16,1,MapBufferAccessMask.MapReadBit);
            Assert.True(request.IsMapped);Assert.Equal(1u,request.Span[0].Page);Assert.Equal(27u,request.Span[0].Linear);
            Assert.Equal(1,request.Span[0].Fraction.W);Assert.Equal(1,request.Span[0].Normal.Y);
        }
    }

    /// <summary>A saturated queue cannot overwrite its guard storage and leaves all unresolved history intact.</summary>
    [Fact]
    public void QueueExhaustionKeepsBoundedStorageAndExistingHistory()
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:35);
        floor.Seed();Configure(floor,"coverage");using var queue=Queue(16,4,0);
        floor.DispatchDiagnosticWork(1,steps:256,fallbackRequests:queue);
        GpuTestFence.WaitForGpuOrSkip("Bounded fallback queue");
        using var data=queue.MapRange<uint>(0,(16+16*64+16)/4,MapBufferAccessMask.MapReadBit);
        Assert.True(data.IsMapped);Assert.InRange(data.Span[0],1u,16u);
        foreach(uint guard in data.Span[^4..])Assert.Equal(0xfeedabcdU,guard);
        for(int page=0;page<4;page++)Assert.Equal(0,floor.Read(floor.Snapshot.IndirectIrradiance,page)[3]);
    }

    /// <summary>Rotating admission reaches every unresolved texel while keeping every dispatch within its sixteen-record bound.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectionRotatesAcrossAllPageTexels(bool cyclingBuckets)
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:35);
        floor.Seed();Configure(floor,"origin-outside");var visited=new HashSet<(uint Page,uint Linear)>();
        uint capacity=cyclingBuckets?4u:16u;
        for(uint serial=0;serial<1024 && visited.Count<256;serial++)
        {
            using var queue=Queue(capacity,4,serial);
            floor.DispatchDiagnosticWork(1,steps:256,texels:cyclingBuckets?4u:64u,fallbackRequests:queue,bucket:cyclingBuckets?serial%16:0);
            GpuTestFence.WaitForGpuOrSkip("Rotating fallback queue");
            using(var header=queue.MapRange<uint>(0,4,MapBufferAccessMask.MapReadBit))
            { Assert.True(header.IsMapped);Assert.Equal(capacity,header.Span[0]); }
            using var records=queue.MapRange<SurfaceFallbackRequest>(16,(int)capacity,MapBufferAccessMask.MapReadBit);
            Assert.True(records.IsMapped);
            foreach(var record in records.Span)visited.Add((record.Page,record.Linear));
        }
        Assert.Equal(256,visited.Count);
    }
    #endregion

    #region Commit history
    /// <summary>A validated completion uses reciprocal previous history while preserving direct light and other texels.</summary>
    [Theory]
    [InlineData(1)] [InlineData(4)] [InlineData(12)]
    public void CommitUsesTheNormalTemporalFormula(int cap)
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:35);
        floor.Seed();floor.MaxFramesAccumulated=cap;floor.SetIndirect(10,cap);
        using var queue=Queue(16,1,37);Configure(floor,"coverage");Assert.False(floor.BounceSample(rays:1,fallbackRequests:queue));
        var request=ReadRequest(queue);var direct=floor.Read(floor.Snapshot.DirectIrradiance);
        var other=floor.Read(floor.Snapshot.IndirectIrradiance,linear:28);
        floor.CommitFallback([new(){Page=request.Page,Slot=request.Slot,Patch=request.Patch,Linear=request.Linear,Estimate=new(0,0,0,1)}]);
        float expected=10f*cap/(cap+1);
        var actual=floor.Read(floor.Snapshot.IndirectIrradiance);
        Assert.InRange(actual[0],expected-.02f,expected+.02f);Assert.Equal(cap,actual[3]);
        Assert.Equal(direct,floor.Read(floor.Snapshot.DirectIrradiance));Assert.Equal(other,floor.Read(floor.Snapshot.IndirectIrradiance,linear:28));
    }

    /// <summary>Stale patch or slot identities and incomplete estimates cannot alter the selected texel.</summary>
    [Theory]
    [InlineData("patch")] [InlineData("slot")] [InlineData("page")] [InlineData("linear")] [InlineData("incomplete")]
    public void CommitRejectsNonmatchingIdentity(string mismatch)
    {
        EnsureContextValid();using var floor=new SurfaceLightingEnclosureFixture(skyFloor:true,worldHeight:35);
        floor.Seed();floor.SetIndirect(9,4);Configure(floor,"coverage");using var queue=Queue(16,1,37);
        Assert.False(floor.BounceSample(rays:1,fallbackRequests:queue));var request=ReadRequest(queue);
        var commit=new SurfaceFallbackCommit{Page=request.Page,Slot=request.Slot,Patch=request.Patch,Linear=request.Linear,Estimate=new(0,0,0,1)};
        switch(mismatch){case "patch":commit.Patch++;break;case "slot":commit.Slot++;break;case "page":commit.Page++;break;
            case "linear":commit.Linear++;break;case "incomplete":commit.Estimate.W=0;break;}
        var before=floor.Read(floor.Snapshot.IndirectIrradiance);floor.CommitFallback([commit]);
        Assert.Equal(before,floor.Read(floor.Snapshot.IndirectIrradiance));
    }
    #endregion

    #region Fixtures
    /// <summary>Authors one source failure while preserving the captured surface itself.</summary>
    private static void Configure(SurfaceLightingEnclosureFixture floor,string scenario)
    {
        if(scenario is "coverage" or "origin-outside")
        {
            var domain=floor.Geometry.Plan.Surface!.Value;
            floor.Geometry.Move(floor.Geometry.Plan with{Surface=new(domain.Min,new(domain.Max.X,scenario=="coverage"?34:33,domain.Max.Z))});
        }
        if(scenario=="unpublished")floor.Geometry.Dirty();
        if(scenario is "unsupported" or "missing-light")
        {
            var sample=floor.Geometry.Sample;
            floor.Geometry.Sample=(x,y,z)=>y>=34?new(scenario=="unsupported"?3u:sample(x,32,z).Geometry,0,0):sample(x,y,z);
            floor.Geometry.Dirty();floor.Geometry.Publish();
        }
    }

    /// <summary>Allocates bounded request storage followed by a canary outside the writable record capacity.</summary>
    private static GpuShaderStorageBuffer Queue(uint capacity,uint pages,uint serial)
    {
        var buffer=GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw);
        var words=new uint[(16+(int)capacity*Marshal.SizeOf<SurfaceFallbackRequest>()+16)/4];
        words[1]=capacity;words[2]=pages;words[3]=serial;Array.Fill(words,0xfeedabcdU,words.Length-4,4);
        buffer.EnsureCapacity(words.Length*4,growExponentially:false);buffer.UploadSubData<uint>(words,0,words.Length*4);return buffer;
    }

    /// <summary>Waits only in the fixture before reading one emitted GPU request.</summary>
    private static SurfaceFallbackRequest ReadRequest(GpuShaderStorageBuffer queue)
    {
        GpuTestFence.WaitForGpuOrSkip("Fallback request readback");
        using var request=queue.MapRange<SurfaceFallbackRequest>(16,1,MapBufferAccessMask.MapReadBit);
        Assert.True(request.IsMapped);return request.Span[0];
    }
    #endregion
}
