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
    public static void Validate(GraphicsPipelineDesc value, GraphicsCapabilities capabilities)
    {
        if (!capabilities.Graphics33) throw new NotSupportedException("Complete graphics descriptions require OpenGL 3.3.");
        if (capabilities.MaxDrawBuffers < 1 || capabilities.MaxVertexAttributes < 1 || capabilities.MaxSampleMaskWords < 1)
            throw new ArgumentException("Incomplete graphics capabilities.", nameof(capabilities));
        if ((value.Dynamics & ~DynamicPipelineState.All) != 0 || !value.Dynamics.HasFlag(DynamicPipelineState.Viewport))
            throw new ArgumentException("A complete pipeline must explicitly declare viewport dynamics.");
        if (value.Rasterizer.Scissor && !value.Dynamics.HasFlag(DynamicPipelineState.Scissor)
            || value.DepthStencil.StencilTest && !value.Dynamics.HasFlag(DynamicPipelineState.StencilReference))
            throw new ArgumentException("Enabled scissor/stencil requires its dynamic declaration.");
        if (value.Targets.Colors.Count > capabilities.MaxDrawBuffers || value.Targets.Samples > Math.Max(1, capabilities.MaxSamples))
            throw new NotSupportedException("Target signature exceeds device limits.");
        if (value.Blending.Count != value.Targets.Colors.Count)
            throw new ArgumentException("Blend state must cover every output slot, including sparse holes.");
        if ((value.DepthStencil.DepthTest || value.DepthStencil.DepthWrite) && !value.Targets.HasDepth
            || value.DepthStencil.StencilTest && !value.Targets.HasStencil)
            throw new ArgumentException("Depth/stencil state requires the corresponding target aspect.");

        PipelineFixedFunctionValidation.Validate(value, capabilities);
        PipelineBlendValidation.Validate(value, capabilities);
        ValidateGeometry(value, capabilities);
    }
    #endregion

    #region Private
    /// <summary>Rejects unsupported topology/stage and layout/capability combinations before native preparation.</summary>
    private static void ValidateGeometry(GraphicsPipelineDesc value, GraphicsCapabilities caps)
    {
        var assembly = value.Assembly;
        if (assembly.Topology is not (PrimitiveType.Points or PrimitiveType.Lines or PrimitiveType.LineStrip
            or PrimitiveType.LineLoop or PrimitiveType.Triangles or PrimitiveType.TriangleStrip or PrimitiveType.TriangleFan
            or PrimitiveType.LinesAdjacency or PrimitiveType.LineStripAdjacency or PrimitiveType.TrianglesAdjacency
            or PrimitiveType.TriangleStripAdjacency or PrimitiveType.Patches))
            throw new ArgumentException("Unsupported primitive topology.");
        bool control = value.Shader.Stages.Any(s => s.Stage.Kind == ShaderStageKind.TessellationControl);
        bool evaluation = value.Shader.Stages.Any(s => s.Stage.Kind == ShaderStageKind.TessellationEvaluation);
        bool patches = assembly.Topology == PrimitiveType.Patches;
        if (control != evaluation || patches != (control && evaluation) || assembly.PatchVertices < 1)
            throw new ArgumentException("Patch topology requires paired tessellation stages and a positive patch size.");
        if (patches && (!caps.Tessellation || assembly.PatchVertices > caps.MaxPatchVertices)
            || assembly.FixedIndexRestart && !caps.FixedIndexRestart)
            throw new NotSupportedException("Unsupported tessellation or restart configuration.");
        if (assembly.Restart && assembly.FixedIndexRestart)
            throw new ArgumentException("Choose explicit-index or fixed-index restart, not both.");
        if (!value.Rasterizer.Discard && !value.Shader.Stages.Any(s => s.Stage.Kind == ShaderStageKind.Fragment))
            throw new ArgumentException("Rasterizing graphics requires a fragment stage.");
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
