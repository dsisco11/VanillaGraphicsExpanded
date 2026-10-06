using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>A complete immutable graphics description, distinct from legacy partial state overrides.</summary>
/// <remarks>No executable revision, native name, target dimensions or resource instance belongs to this reusable identity.</remarks>
internal sealed class GraphicsPipelineDesc : IEquatable<GraphicsPipelineDesc>
{
    public ShaderPipelineIdentity Shader { get; }
    public VertexLayoutDesc VertexLayout { get; }
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
    /// <summary>Resolves omissions, rejects invalid/capability-incompatible intent in every build, then publishes canonical values.</summary>
    public GraphicsPipelineDesc(ShaderPipelineIdentity shader, VertexLayoutDesc vertexLayout,
        RenderTargetSignature targets, DynamicPipelineState dynamics, GraphicsCapabilities? capabilities = null,
        DepthStencilDesc? depthStencil = null, RasterizerDesc? rasterizer = null,
        IEnumerable<ColorBlendDesc>? blending = null, SamplingDesc? sampling = null,
        PrimitiveAssemblyDesc? assembly = null, OutputDesc? output = null, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(vertexLayout);
        ArgumentNullException.ThrowIfNull(targets);
        capabilities ??= GpuSupport.Graphics;
        Shader = shader; VertexLayout = vertexLayout; Targets = targets; Dynamics = dynamics; Label = label;
        DepthStencil = depthStencil ?? new(); Rasterizer = rasterizer ?? new(); Sampling = sampling ?? new();
        Assembly = assembly ?? new(); Output = output ?? new();
        Blending = new(blending ?? Enumerable.Repeat(new ColorBlendDesc(), targets.Colors.Count));
        // Validate authored values before canonicalization so invalid inactive enums never silently pass.
        PipelineDescriptionValidation.Validate(this, capabilities);
        DepthStencil = PipelineCanonicalization.DepthStencil(DepthStencil);
        Rasterizer = PipelineCanonicalization.Rasterizer(Rasterizer);
        Blending = new(Blending.Select(PipelineCanonicalization.Blend));
        Sampling = PipelineCanonicalization.Sampling(Sampling);
        Assembly = PipelineCanonicalization.Assembly(Assembly);
    }
    #endregion

    #region Identity
    /// <summary>Compares every behavior-bearing field while excluding diagnostic labels.</summary>
    public bool Equals(GraphicsPipelineDesc? other) => other is not null && Shader == other.Shader
        && VertexLayout == other.VertexLayout && Targets == other.Targets && Dynamics == other.Dynamics
        && DepthStencil == other.DepthStencil && Rasterizer == other.Rasterizer && Blending.Equals(other.Blending)
        && Sampling == other.Sampling && Assembly == other.Assembly && Output == other.Output;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphicsPipelineDesc other && Equals(other);
    /// <summary>Uses structural identity for complete descriptions, including null operands.</summary>
    public static bool operator ==(GraphicsPipelineDesc? left, GraphicsPipelineDesc? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);
    /// <summary>Reports structural inequality rather than managed-object identity.</summary>
    public static bool operator !=(GraphicsPipelineDesc? left, GraphicsPipelineDesc? right) => !(left == right);
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
