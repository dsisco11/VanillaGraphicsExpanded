using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>A complete immutable graphics description, distinct from legacy partial state overrides.</summary>
/// <remarks>Identity includes the exact shader generation and behavior values, excluding native names and target dimensions.</remarks>
public sealed class RHIGraphicsPipelineDescription : IEquatable<RHIGraphicsPipelineDescription>
{
    public RHIShaderRef Shader { get; }
    public RHIVertexLayout VertexLayout { get; }
    public RenderTargetSignature Targets { get; }
    public DynamicPipelineState Dynamics { get; }
    public DepthStencilDesc DepthStencil { get; }
    public RasterizerDesc Rasterizer { get; }
    public PipelineValues<ColorBlendDesc> Blending { get; }
    public SamplingDesc Sampling { get; }
    public PrimitiveAssemblyDesc Assembly { get; }
    public OutputDesc Output { get; }
    public string? Label { get; }

    #region Public API
    #region Construction
    /// <summary>Freezes supplied shape and defaults without native context, resource inspection or device-limit discovery.</summary>
    public RHIGraphicsPipelineDescription(RHIShaderRef shader, RHIVertexLayout vertexLayout,
        RenderTargetSignature targets, DynamicPipelineState dynamics,
        DepthStencilDesc? depthStencil = null, RasterizerDesc? rasterizer = null,
        IEnumerable<ColorBlendDesc>? blending = null, SamplingDesc? sampling = null,
        PrimitiveAssemblyDesc? assembly = null, OutputDesc? output = null, string? label = null)
    {
        if (shader.IsEmpty) throw new ArgumentException("A fixed graphics shader reference is required.", nameof(shader));
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(targets);
        Shader = shader; VertexLayout = vertexLayout; Targets = targets; Dynamics = dynamics; Label = label;
        DepthStencil = depthStencil ?? new(); Rasterizer = rasterizer ?? new(); Sampling = sampling ?? new();
        Assembly = assembly ?? new(); Output = output ?? new();
        Blending = new(blending ?? Enumerable.Repeat(new ColorBlendDesc(), targets.Colors.Count));
        // Validate authored values before canonicalization so invalid inactive enums never silently pass.
        PipelineDescriptionValidation.ValidateShape(this);
        DepthStencil = PipelineCanonicalization.DepthStencil(DepthStencil);
        Sampling = PipelineCanonicalization.Sampling(Sampling);
    }
    #endregion

    #region Identity
    /// <summary>Compares every behavior-bearing field while excluding diagnostic labels.</summary>
    public bool Equals(RHIGraphicsPipelineDescription? other) => other is not null && Shader == other.Shader
        && VertexLayout == other.VertexLayout && Targets == other.Targets && Dynamics == other.Dynamics
        && DepthStencil == other.DepthStencil && Rasterizer == other.Rasterizer && Blending.Equals(other.Blending)
        && Sampling == other.Sampling && Assembly == other.Assembly && Output == other.Output;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RHIGraphicsPipelineDescription other && Equals(other);
    /// <summary>Uses structural identity for complete descriptions, including null operands.</summary>
    public static bool operator ==(RHIGraphicsPipelineDescription? left, RHIGraphicsPipelineDescription? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);
    /// <summary>Reports structural inequality rather than managed-object identity.</summary>
    public static bool operator !=(RHIGraphicsPipelineDescription? left, RHIGraphicsPipelineDescription? right) => !(left == right);
    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Shader); hash.Add(VertexLayout); hash.Add(Targets); hash.Add(Dynamics); hash.Add(DepthStencil);
        hash.Add(Rasterizer); hash.Add(Blending); hash.Add(Sampling); hash.Add(Assembly); hash.Add(Output);
        return hash.ToHashCode();
    }
    #endregion
    #endregion
}
