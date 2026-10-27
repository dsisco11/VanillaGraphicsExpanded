using System;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns direct graphics dispatch and retains original resources through all native callbacks.</summary>
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
        ValidateGraphicsPacket(pipeline, geometry, dynamics);
        ValidateDirectDraw(geometry, draw);
        pipeline.BeginSubmission();
        try
        {
            BeginBindingExecution(shader, resources);
            try
            {
                PrepareGraphicsPacket(pipeline, shader, geometry, dynamics, resources);
                MarkBindingExecution(shader, resources);
                GL.DrawArraysInstancedBaseInstance(OpenGLPrimitiveValues.ToCore(pipeline.Description.Assembly.Topology),
                    draw.FirstVertex, draw.VertexCount, draw.InstanceCount, draw.BaseInstance);
                CompleteGraphicsPacket(geometry);
            }
            finally { ReleaseExecutionResources(); }
        }
        finally { pipeline.EndSubmission(); }
    }
    /// <inheritdoc />
    public void DrawIndexed(RHIGraphicsPipelineRef reference, RHIGeometryInputs geometry, in RHIDrawIndexed draw,
        RHIDynamicState dynamics, RHIResourceBindings? resources)
    {
        RequireContext();
        var pipeline = reference.RequirePipeline();
        var shader = pipeline.RequireShader();
        ValidateGraphicsPacket(pipeline, geometry, dynamics);
        ValidateIndexedDraw(geometry, draw);
        var indices = geometry.Indices!.Value;
        nint offset = checked(indices.Range.Offset + checked((nint)draw.FirstIndex * RHIGeometryInputs.IndexWidth(indices.Format)));
        pipeline.BeginSubmission();
        try
        {
            BeginBindingExecution(shader, resources);
            try
            {
                PrepareGraphicsPacket(pipeline, shader, geometry, dynamics, resources);
                MarkBindingExecution(shader, resources);
                GL.DrawElementsInstancedBaseVertexBaseInstance(OpenGLPrimitiveValues.ToCore(pipeline.Description.Assembly.Topology),
                    draw.IndexCount, IndexType(indices.Format), offset, draw.InstanceCount, draw.BaseVertex, draw.BaseInstance);
                CompleteGraphicsPacket(geometry);
            }
            finally { ReleaseExecutionResources(); }
        }
        finally { pipeline.EndSubmission(); }
    }
    #endregion

    #region Private
    /// <summary>Rejects the complete supplied draw shape before any VAO configuration, program selection or resource upload.</summary>
    private void ValidateGraphicsPacket(RHIGraphicsPipeline pipeline, RHIGeometryInputs geometry, RHIDynamicState dynamics)
    {
        ArgumentNullException.ThrowIfNull(dynamics);
        var pass = activeRenderPass ?? throw new InvalidOperationException("Graphics drawing requires an active render pass.");
        pass.ValidatePipeline(pipeline);
        ValidateGeometryInputs(pipeline, geometry);
        dynamics.ValidateFor(pipeline.Description);
    }
    /// <summary>Realizes geometry and applies already validated state while all original buffer and executable guards are held.</summary>
    private void PrepareGraphicsPacket(RHIGraphicsPipeline pipeline, RHIShader shader, RHIGeometryInputs geometry,
        RHIDynamicState dynamics, RHIResourceBindings? resources)
    {
        RetainGeometryInputs(geometry);
        var vertexArray = ResolveGeometryVertexArray(geometry);
        RetainExecutionResource(vertexArray);
        int vertexArrayName = checked((int)vertexArray.ResourceId);
        activeRenderPass!.ValidatePipeline(pipeline);
        geometry.RequireLive();
        cache.ApplyGraphicsState(pipeline, dynamics);
        if (resources is null) ApplyExecutable(shader);
        else PublishResourceBindings(shader, resources, reuseCommittedUniforms: true);
        OpenGLVertexSelection.Bind(cache,vertexArrayName);
        shader.RequireUsable();
        geometry.RequireLive();
        geometry.ImportedArray?.RequireCurrent(NativeOrigin, cache.VertexConfigurationEpoch);
    }
    /// <summary>Checks originating admission after native callbacks and records only actually issued draw commands.</summary>
    private void CompleteGraphicsPacket(RHIGeometryInputs geometry)
    {
        RequireContext();
        geometry.RequireLive();
        geometry.ImportedArray?.RequireCurrent(NativeOrigin, cache.VertexConfigurationEpoch);
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
