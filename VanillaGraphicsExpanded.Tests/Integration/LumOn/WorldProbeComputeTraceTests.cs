using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using System.Numerics;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Verifies native indirect-dispatch ownership and restoration for world-probe compute tracing.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class WorldProbeComputeTraceTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>GPU-generated tiles keep per-probe output ownership and restore the caller's indirect binding.</summary>
    [Fact]
    public void MultipleProbeTilesUseIndependentInputsAndRestoreIndirectBinding()
    {
        // Arrange
        EnsureContextValid();
        // Publish one deterministic geometry tile so the compute path exercises three independent probe ranges.
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,2,0),true,32,4),new(),(_,_,_)=>new(1,0,0));
        geometry.Publish();
        using var assets=new BinaryShaderApiFixture();using var batch=new WorldProbeTraceBatch(assets.Api);
        using var prior=RHICommandContext.Current.CreateIndirectBuffer(new RHIBufferDescription { Role = BufferRole.IndirectDraw, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();prior.UploadDispatchCommand(new(7,8,9));
        using var binding=OpenGlStateCache.Current.BindBufferScope(BufferTarget.DispatchIndirectBuffer,prior.BufferId);
        int original=GL.GetInteger(GetPName.DispatchIndirectBufferBinding);
        WorldProbeTraceProbeGpu[] probes=
        [
            new(new(.5,.5,.5),8,4,8,0,129,8),
            new(new(3.5,2.5,3.5),.25,4,8,129,3,.25),
            new(new(-3.5,1.5,-3.5),8,4,8,132,65,8,maxSteps:0)
        ];
        // Act
        batch.Submit(geometry.Scene,null,probes,Enumerable.Repeat(0x80000002u,197).ToArray());
        // Assert
        Assert.Equal(original,GL.GetInteger(GetPName.DispatchIndirectBufferBinding));
        GpuTestFence.WaitForGpuOrSkip("Multiple generated probe tiles");
        // Act
        Assert.True(batch.TryRead(out var answers));Assert.Equal(197,answers.Length);
        for(int index=0;index<answers.Length;index++)
        {
            var expected=index<129?WorldProbeTraceOutcome.Sky:index<132?WorldProbeTraceOutcome.DistanceLimit:WorldProbeTraceOutcome.BudgetExhausted;
            // Assert
            Assert.Equal(expected,answers[index].TraceOutcome);
        }
        Assert.Equal(original,GL.GetInteger(GetPName.DispatchIndirectBufferBinding));
        // Act
        batch.Submit(geometry.Scene,null,[new(new(.5,.5,.5),.25,4,8,0,1,.25)],[0x80000002u]);
        // Assert
        Assert.True(batch.Pending);
        GpuTestFence.WaitForGpuOrSkip("World probe smaller follow-up trace");
        // Act
        Assert.True(batch.TryRead(out var smaller));Assert.Single(smaller);
        // Assert
        Assert.False(batch.Pending);
        // Act
        Assert.False(batch.TryRead(out _));
        // Assert
        Assert.Equal(WorldProbeTraceOutcome.DistanceLimit,smaller[0].TraceOutcome);
        Assert.Equal(original,GL.GetInteger(GetPName.DispatchIndirectBufferBinding));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
