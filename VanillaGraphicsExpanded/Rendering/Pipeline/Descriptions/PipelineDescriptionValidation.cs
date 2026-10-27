using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Coordinates release-build description validation without reflection, native state queries or resource binding.</summary>
internal static class PipelineDescriptionValidation
{
    #region Public API
    /// <summary>Checks cross-category invariants and delegates focused state validation before publication.</summary>
    public static void Validate(RHIGraphicsPipelineDescription value, GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ValidateShape(value);
        if (capabilities.MaxDrawBuffers < 1 || capabilities.MaxVertexAttributes < 1 || capabilities.MaxSampleMaskWords < 1)
            throw new ArgumentException("Incomplete graphics capabilities.", nameof(capabilities));
        if (value.Targets.Colors.Count > capabilities.MaxDrawBuffers || value.Targets.Samples > Math.Max(1, capabilities.MaxSamples))
            throw new NotSupportedException("Target signature exceeds device limits.");
        PipelineFixedFunctionValidation.Validate(value, capabilities);
        ValidateGeometry(value, capabilities);
    }
    /// <summary>Rejects invalid supplied behavior using only immutable values, independently of context and device limits.</summary>
    internal static void ValidateShape(RHIGraphicsPipelineDescription value)
    {
        var shader = value.Shader.Describe();
        if (shader.Kind != ShaderKind.Graphics) throw new ArgumentException("A graphics executable is required.");
        if ((value.Dynamics & ~DynamicPipelineState.All) != 0 || !value.Dynamics.HasFlag(DynamicPipelineState.Viewport))
            throw new ArgumentException("A complete pipeline must explicitly declare viewport dynamics.");
        if (value.Rasterizer.Scissor && !value.Dynamics.HasFlag(DynamicPipelineState.Scissor)
            || value.DepthStencil.StencilTest && !value.Dynamics.HasFlag(DynamicPipelineState.StencilReference))
            throw new ArgumentException("Enabled scissor/stencil requires its dynamic declaration.");
        if (value.Blending.Count != value.Targets.Colors.Count)
            throw new ArgumentException("Blend state must cover every output slot, including sparse holes.");
        if ((value.DepthStencil.DepthTest || value.DepthStencil.DepthWrite) && !value.Targets.HasDepth
            || value.DepthStencil.StencilTest && !value.Targets.HasStencil)
            throw new ArgumentException("Depth/stencil state requires the corresponding target aspect.");
        var assembly = value.Assembly;
        if (!Enum.IsDefined(assembly.Topology) || assembly.PatchVertices < 1)
            throw new ArgumentException("Invalid primitive assembly inputs.");
        bool tessellation = shader.Stages.Any(stage => stage.Stage == ShaderStage.TessellationEvaluation);
        if ((assembly.Topology == PrimitiveTopology.Patches) != tessellation)
            throw new ArgumentException("Patch topology requires the executable's paired tessellation stages.");
        if (assembly.Restart && assembly.FixedIndexRestart)
            throw new ArgumentException("Choose explicit-index or fixed-index restart, not both.");
        if (!value.Rasterizer.Discard && !shader.Stages.Any(stage => stage.Stage == ShaderStage.Fragment))
            throw new ArgumentException("Rasterizing graphics requires a fragment stage.");
        PipelineFixedFunctionValidation.ValidateShape(value);
        PipelineBlendValidation.Validate(value);
    }
    #endregion

    #region Private
    /// <summary>Rejects unsupported topology/stage and layout/capability combinations before native preparation.</summary>
    private static void ValidateGeometry(RHIGraphicsPipelineDescription value, GraphicsCapabilities caps)
    {
        var assembly = value.Assembly;
        if (assembly.Topology is not (PrimitiveTopology.Points or PrimitiveTopology.Lines or PrimitiveTopology.LineStrip
            or PrimitiveTopology.LineLoop or PrimitiveTopology.Triangles or PrimitiveTopology.TriangleStrip or PrimitiveTopology.TriangleFan
            or PrimitiveTopology.LinesAdjacency or PrimitiveTopology.LineStripAdjacency or PrimitiveTopology.TrianglesAdjacency
            or PrimitiveTopology.TriangleStripAdjacency or PrimitiveTopology.Patches))
            throw new ArgumentException("Unsupported primitive topology.");
        if (assembly.PatchVertices > caps.MaxPatchVertices)
            throw new NotSupportedException("Patch size exceeds the device limit.");
        foreach (var a in value.VertexLayout.Attributes)
        {
            if (a.Location >= caps.MaxVertexAttributes || a.Binding >= caps.MaxVertexBindings
                || a.Stride > caps.MaxVertexStride || a.Offset > caps.MaxVertexRelativeOffset
                || a.Interpretation == VertexInterpretation.Double && !caps.DoubleAttributes)
                throw new NotSupportedException("Vertex layout exceeds device capabilities.");
        }
    }
    #endregion
}
