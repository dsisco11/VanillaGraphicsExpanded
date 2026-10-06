using System.Collections.Generic;
using OpenTK.Mathematics;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolved incoming values for explicit coverage, independent of live cache knowledge.</summary>
internal sealed class PipelineStateSnapshot
{
    private readonly BlendState[] blend;
    private readonly Dictionary<int, uint>? sampleMasks;
    /// <summary>Enumerates saved indexed values without exposing the mutable container.</summary>
    internal IEnumerable<KeyValuePair<int, uint>> SampleMasks
    {
        get
        {
            if (sampleMasks is null) yield break;
            foreach (var entry in sampleMasks) yield return entry;
        }
    }
    internal CompleteSamplingState Sampling { get; }
    internal CompleteStencilState Stencil { get; }
    internal CompleteOutputState Output { get; }
    internal PipelineStateCoverage Coverage { get; }
    internal DepthState Depth { get; }
    internal RasterizerState Rasterizer { get; }
    internal PrimitiveAssemblyState Assembly { get; }
    internal DynamicDrawState Dynamic { get; }
    internal Vector4 ClearColor { get; }
    internal int OutputCount => blend.Length;

    #region Public API
    /// <summary>Copies resolved category values; coverage alone determines which fields are meaningful.</summary>
    internal PipelineStateSnapshot(PipelineStateCoverage coverage,
        DepthState depth, RasterizerState rasterizer, PrimitiveAssemblyState assembly,
        DynamicDrawState dynamic, Vector4 clearColor, BlendState[] blend, CompleteSamplingState sampling = default,
        CompleteStencilState stencil = default, CompleteOutputState output = default,
        IReadOnlyDictionary<int, uint>? sampleMasks = null)
    {
        Coverage = coverage; Depth = depth; Rasterizer = rasterizer;
        Sampling = sampling; Stencil = stencil; Output = output;
        this.sampleMasks = sampleMasks is null ? null : new(sampleMasks);
        Assembly = assembly; Dynamic = dynamic; ClearColor = clearColor;
        this.blend = (BlendState[])blend.Clone();
    }

    /// <summary>Returns an output value by copy, never exposing the retained array.</summary>
    internal BlendState BlendAt(int index) => blend[index];
    #endregion
}
