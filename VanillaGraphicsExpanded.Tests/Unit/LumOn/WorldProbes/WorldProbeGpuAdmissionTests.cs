using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks compact per-probe input layout and bounded direction admission before GPU dispatch.</summary>
public sealed class WorldProbeGpuAdmissionTests
{
    /// <summary>Integer anchors retain fractional precision and GPU traversal cannot exceed its fixed cell limit.</summary>
    [Fact]
    public void ProbeLayoutPreservesSignedFractionsAndClampsSteps()
    {
        var probe=new WorldProbeTraceProbeGpu(new(-16777216.25,35.5,16777216.75),64,256,8,0,64,maxSteps:int.MaxValue);
        Assert.Equal(64,Marshal.SizeOf<WorldProbeTraceProbeGpu>());
        Assert.Equal(80,Marshal.SizeOf<WorldProbeTraceAnswerGpu>());
        Assert.Equal(16,Marshal.OffsetOf<WorldProbeTraceAnswerGpu>(nameof(WorldProbeTraceAnswerGpu.Hit)).ToInt32());
        Assert.Equal(-16777217,probe.OriginX); Assert.Equal(16777216,probe.OriginZ);
        Assert.Equal(new Vector4(.75f,.5f,.75f,64),probe.FractionDistance);
        Assert.Equal(512u,probe.MaxSteps);
        Assert.Equal(0u,new WorldProbeTraceProbeGpu(new(0,1,0),1,256,8,0,1,maxSteps:-1).MaxSteps);
        Assert.Equal(320,Marshal.SizeOf<WorldProbeTraceProbeGpu>()+(64<<2));
    }

    /// <summary>Largest configured atlas updates admit every selected direction plus the shared nearby query.</summary>
    [Fact]
    public void MaximumAtlasSelectionFitsOneBoundedBatch()
    {
        var item=new LumOnWorldProbeTraceWorkItem(0,new(0,new(),new(),0),new(1,2,3),64,64,4096,false,.25f,-1,1e-6f,2,true);
        var directions=WorldProbeGpuIntegration.CreateDirections(item);
        Assert.Equal(4097,directions.Length);
        Assert.Equal(0x80000000u,directions[0]);
        Assert.Equal(4096,directions.Skip(1).Distinct().Count());
        Assert.All(directions.Skip(1),direction=>Assert.InRange(direction,0u,4095u));
    }

    /// <summary>Nonfinite origins, unsupported atlas sizes and invalid ranges never reach the GPU.</summary>
    [Fact]
    public void InvalidRaysAreRejectedBeforeSubmission()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new WorldProbeTraceProbeGpu(new(double.NaN,0,0),1,256,8,0,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new WorldProbeTraceProbeGpu(new(0,1,0),1,256,65,0,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new WorldProbeTraceProbeGpu(new(0,1,0),double.PositiveInfinity,256,8,0,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new WorldProbeTraceProbeGpu(new(0,1,0),1,256,8,0,0));
    }
}
