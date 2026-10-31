using System;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes prepared graphics under caller-owned resource lifetimes and context sequencing.</summary>
internal sealed partial class OpenGLBackend
{
    private RenderPass? activeRenderPass;

    #region Public API
    /// <inheritdoc />
    public void Draw(RHIGraphicsPipelineRef reference, RHIGeometryInputs geometry, in RHIDraw draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources)
    {
        RequireContext();
        var pipeline = reference.RequirePipeline();
        var shader = pipeline.RequireShader();
        ValidateGraphicsPacket(pipeline, geometry, dynamics, resources);
        ValidateDirectDraw(geometry, draw);

        try
        {
            SelectResourceBindings(shader, resources);

            PrepareGraphicsPacket(pipeline, shader, geometry, dynamics, resources);
            MarkBindingExecution();
            GL.DrawArraysInstancedBaseInstance(OpenGLPrimitiveValues.ToCore(pipeline.Description.Assembly.Topology),
                draw.FirstVertex, draw.VertexCount, draw.InstanceCount, draw.BaseInstance);
            CompleteGraphicsPacket(geometry);
        }
        finally { ClearResourceBindings(); }
    }
    /// <inheritdoc />
    public void DrawIndexed(RHIGraphicsPipelineRef reference, RHIGeometryInputs geometry, in RHIDrawIndexed draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources)
    {
        RequireContext();
        var pipeline = reference.RequirePipeline();
        var shader = pipeline.RequireShader();
        ValidateGraphicsPacket(pipeline, geometry, dynamics, resources);
        ValidateIndexedDraw(geometry, draw);
        var indices = geometry.Indices!.Value;
        nint offset = checked(indices.Range.Offset + checked((nint)draw.FirstIndex * RHIGeometryInputs.IndexWidth(indices.Format)));

        try
        {
            SelectResourceBindings(shader, resources);

            PrepareGraphicsPacket(pipeline, shader, geometry, dynamics, resources);
            MarkBindingExecution();
            GL.DrawElementsInstancedBaseVertexBaseInstance(OpenGLPrimitiveValues.ToCore(pipeline.Description.Assembly.Topology),
                draw.IndexCount, IndexType(indices.Format), offset, draw.InstanceCount, draw.BaseVertex, draw.BaseInstance);
            CompleteGraphicsPacket(geometry);
        }
        finally { ClearResourceBindings(); }
    }
    #endregion

    #region Private
    /// <summary>Rejects the complete supplied draw shape before any VAO configuration, program selection or resource upload.</summary>
    private void ValidateGraphicsPacket(RHIGraphicsPipeline pipeline, RHIGeometryInputs geometry, RHIDynamicState dynamics, RHIResourceBindings? resources)
    {
        ArgumentNullException.ThrowIfNull(dynamics);
        var pass = activeRenderPass ?? throw new InvalidOperationException("Graphics drawing requires an active render pass.");
        pass.ValidatePipeline(pipeline);
        ValidateGeometryInputs(pipeline, geometry);
        dynamics.ValidateFor(pipeline.Description);
        ValidateGraphicsFeedback(pipeline, resources);
    }
    /// <summary>Realizes prepared geometry and applies changed state without acquiring resource lifetime authority.</summary>
    private void PrepareGraphicsPacket(RHIGraphicsPipeline pipeline, RHIShader shader, RHIGeometryInputs geometry,
        RHIDynamicState dynamics, RHIResourceBindings? resources)
    {

        var vertexArray = ResolveGeometryVertexArray(geometry);

        int vertexArrayName = checked((int)vertexArray.ResourceId);

        geometry.RequireLive();
        var realized = (OpenGLGraphicsPipelineRealization)pipeline.Realization;
        cache.ApplyGraphicsState(pipeline.Description, dynamics, realized.Relevance, realized.Coverage);
        if (resources is null) ApplyExecutable(shader);
        else PublishResourceBindings(shader, reuseCommittedUniforms: true);
        OpenGLVertexSelection.Bind(cache,vertexArrayName);
        shader.RequirePrepared();
        geometry.RequireLive();
        if (geometry.ImportedArray is { } imported)
            imported.RequirePrepared(NativeOrigin);
    }
    /// <summary>Checks originating admission after native callbacks and records only actually issued draw commands.</summary>
    private void CompleteGraphicsPacket(RHIGeometryInputs geometry)
    {
        RequireContext();
        geometry.RequireLive();
        if (geometry.ImportedArray is { } imported)
            imported.RequirePrepared(NativeOrigin);
#if DEBUG
        cache.RecordDrawSubmission();
#endif
    }
    /// <summary>Maps index scalars by explicit core enum members.</summary>
    private static DrawElementsType IndexType(IndexFormat format) => format switch
    {
        IndexFormat.UnsignedByte => DrawElementsType.UnsignedByte,
        IndexFormat.UnsignedShort => DrawElementsType.UnsignedShort,
        IndexFormat.UnsignedInt => DrawElementsType.UnsignedInt,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
    #endregion
}
