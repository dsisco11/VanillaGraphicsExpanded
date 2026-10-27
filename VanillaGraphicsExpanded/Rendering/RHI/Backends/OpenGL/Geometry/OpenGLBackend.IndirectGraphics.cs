using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes indirect graphics descriptors while leaving generated argument contents under producer authority.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void DrawIndirect(RHIGraphicsPipelineRef pipeline, RHIGeometryInputs geometry, in RHIIndirectDraw draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources) =>
        ExecuteIndirectGraphics(pipeline, geometry, draw, dynamics, resources, indexed: false);

    /// <inheritdoc />
    public void DrawIndexedIndirect(RHIGraphicsPipelineRef pipeline, RHIGeometryInputs geometry, in RHIIndirectDraw draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources) =>
        ExecuteIndirectGraphics(pipeline, geometry, draw, dynamics, resources, indexed: true);
    #endregion

    #region Private
    /// <summary>Validates the complete packet before effects and retains every argument source through native dispatch and restoration.</summary>
    private void ExecuteIndirectGraphics(RHIGraphicsPipelineRef reference, RHIGeometryInputs geometry, in RHIIndirectDraw draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources, bool indexed)
    {
        RequireContext();
        var pipeline = reference.RequirePipeline();
        var shader = pipeline.RequireShader();
        ValidateGraphicsPacket(pipeline, geometry, dynamics);
        ValidateIndirectDraw(geometry, draw, indexed);
        pipeline.BeginSubmission();
        try
        {
            BeginBindingExecution(shader, resources);
            try
            {
                RetainExecutionResource(draw.Arguments.Buffer.RequireResource());
                if (draw.CountBuffer is { } count) RetainExecutionResource(count.Buffer.RequireResource());
                PrepareGraphicsPacket(pipeline, shader, geometry, dynamics, resources);
                ExecuteIndirectSelectors(pipeline, geometry, draw, resources, indexed);
            }
            finally { ReleaseExecutionResources(); }
        }
        finally { pipeline.EndSubmission(); }
    }

    /// <summary>Scopes only the required bound-only selectors and preserves an original failure alongside independent restoration failures.</summary>
    private void ExecuteIndirectSelectors(RHIGraphicsPipeline pipeline, RHIGeometryInputs geometry,
        in RHIIndirectDraw draw, RHIResourceBindings? resources, bool indexed)
    {
        StateCache.BufferScope? arguments = null, count = null;
        Exception? operationFailure = null;
        try
        {
            arguments = cache.BindBufferScope(Legacy.BufferTarget.DrawIndirectBuffer,
                checked((int)draw.Arguments.Buffer.RequireResource().ResourceId));
            if (draw.CountBuffer is { } counter)
                count = cache.BindBufferScope(Legacy.BufferTarget.ParameterBuffer,
                    checked((int)counter.Buffer.RequireResource().ResourceId));
            RequireContext();
            pipeline.RequireShader().RequireUsable();
            geometry.RequireLive();
            MarkBindingExecution(pipeline.RequireShader(), resources);
            ExecuteNativeIndirect(pipeline.Description.Assembly.Topology, geometry.Indices?.Format, draw, indexed);
            CompleteGraphicsPacket(geometry);
        }
        catch (Exception failure) { operationFailure = failure; throw; }
        finally
        {
            List<Exception>? failures = null;
            RestoreIndirectSelector(count, ref failures);
            RestoreIndirectSelector(arguments, ref failures);
            if (failures is not null)
            {
                if (operationFailure is not null) failures.Insert(0, operationFailure);
                throw new AggregateException("Indirect draw selector restoration failed.", failures);
            }
        }
    }

    /// <summary>Selects only the represented native indirect family; optional counts are never emulated or retried.</summary>
    private void ExecuteNativeIndirect(PrimitiveTopology topology, IndexFormat? indexFormat, in RHIIndirectDraw draw, bool indexed)
    {
        var mode = OpenGLPrimitiveValues.ToCore(topology);
        if (draw.CountBuffer is not { } counter)
        {
            if (indexed) GL.MultiDrawElementsIndirect(mode, IndexType(indexFormat!.Value), draw.Arguments.Offset, draw.Count, draw.Stride);
            else GL.MultiDrawArraysIndirect(mode, draw.Arguments.Offset, draw.Count, draw.Stride);
            return;
        }
        bool core = NativeCapabilities.ApiVersion is { } version && version >= new Version(4, 6);
        if (indexed)
        {
            if (core) GL.MultiDrawElementsIndirectCount(mode, IndexScalar(indexFormat!.Value),
                draw.Arguments.Offset, counter.Offset, draw.Count, draw.Stride);
            else GL.Arb.MultiDrawElementsIndirectCount(mode, IndexType(indexFormat!.Value),
                draw.Arguments.Offset, counter.Offset, draw.Count, draw.Stride);
        }
        else if (core) GL.MultiDrawArraysIndirectCount(mode, draw.Arguments.Offset, counter.Offset, draw.Count, draw.Stride);
        else GL.Arb.MultiDrawArraysIndirectCount(mode, draw.Arguments.Offset, counter.Offset, draw.Count, draw.Stride);
    }

    /// <summary>Attempts each independent restoration without losing a prior operation or cleanup failure.</summary>
    private static void RestoreIndirectSelector(StateCache.BufferScope? scope, ref List<Exception>? failures)
    {
        try { scope?.Dispose(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
    }

    /// <summary>Maps the core count overload's explicit scalar enum members.</summary>
    private static All IndexScalar(IndexFormat format) => format switch
    {
        IndexFormat.UnsignedByte => All.UnsignedByte,
        IndexFormat.UnsignedShort => All.UnsignedShort,
        IndexFormat.UnsignedInt => All.UnsignedInt,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    #endregion
}
