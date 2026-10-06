using System.Collections.Generic;
using OpenTK.Mathematics;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolved incoming values for explicit coverage, independent of live cache knowledge.</summary>
internal sealed class PipelineStateSnapshot
{
    private readonly BlendState[] blend;
    private readonly BlendStateKnowledge[] blendKnown;
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
    internal SamplingState Sampling { get; }
    internal StencilState Stencil { get; }
    internal OutputState Output { get; }
    internal PipelineStateCoverage Coverage { get; }
    internal DepthState Depth { get; }
    internal RasterizerState Rasterizer { get; }
    internal PrimitiveAssemblyState Assembly { get; }
    internal DynamicDrawState Dynamic { get; }
    internal Vector4 ClearColor { get; }
    internal int OutputCount => blend.Length;

    /// <summary>Detached validity of the captured depth values.</summary>
    internal DepthStateKnowledge DepthKnown { get; }
    /// <summary>Detached validity of the captured rasterizer values.</summary>
    internal RasterizerStateKnowledge RasterizerKnown { get; }
    /// <summary>Detached validity of the captured assembly values.</summary>
    internal PrimitiveAssemblyStateKnowledge AssemblyKnown { get; }
    /// <summary>Detached validity of the captured dynamic values.</summary>
    internal DynamicDrawStateKnowledge DynamicKnown { get; }
    /// <summary>Detached validity of the captured sampling values.</summary>
    internal SamplingStateKnowledge SamplingKnown { get; }
    /// <summary>Detached validity of the captured stencil values.</summary>
    internal StencilStateKnowledge StencilKnown { get; }
    /// <summary>Detached validity of the captured output values.</summary>
    internal OutputStateKnowledge OutputKnown { get; }

    #region Public API
    /// <summary>Copies resolved category values; coverage alone determines which fields are meaningful.</summary>
    internal PipelineStateSnapshot(PipelineStateCoverage coverage,
        DepthState depth, RasterizerState rasterizer, PrimitiveAssemblyState assembly,
        DynamicDrawState dynamic, Vector4 clearColor, BlendState[] blend, SamplingState sampling,
        StencilState stencil, OutputState output,
        IReadOnlyDictionary<int, uint>? sampleMasks,
        DepthStateKnowledge depthKnown,
        RasterizerStateKnowledge rasterizerKnown,
        PrimitiveAssemblyStateKnowledge assemblyKnown,
        DynamicDrawStateKnowledge dynamicKnown,
        SamplingStateKnowledge samplingKnown,
        StencilStateKnowledge stencilKnown,
        OutputStateKnowledge outputKnown, BlendStateKnowledge[] blendKnown)
    {
        DepthKnown = depthKnown & coverage.Depth;
        RasterizerKnown = rasterizerKnown & coverage.Rasterizer;
        AssemblyKnown = assemblyKnown & coverage.Assembly;
        DynamicKnown = dynamicKnown & coverage.Dynamic;
        SamplingKnown = coverage.CompleteGraphics ? samplingKnown : default;
        StencilKnown = coverage.CompleteGraphics ? stencilKnown : default;
        OutputKnown = coverage.CompleteGraphics ? outputKnown : default;
        Coverage = coverage; Depth = depth; Rasterizer = rasterizer;
        Sampling = sampling; Stencil = stencil; Output = output;
        this.sampleMasks = sampleMasks is null ? null : new(sampleMasks);
        Assembly = assembly; Dynamic = dynamic; ClearColor = clearColor;
        this.blend = (BlendState[])blend.Clone();
        this.blendKnown = (BlendStateKnowledge[])blendKnown.Clone();
        for (int i = 0; i < this.blendKnown.Length; i++) this.blendKnown[i] &= coverage.BlendAt(i);
    }

    /// <summary>Returns an output value by copy, never exposing the retained array.</summary>
    internal BlendState BlendAt(int index) => blend[index];
    /// <summary>Returns detached validity for an output slot.</summary>
    internal BlendStateKnowledge BlendKnowledgeAt(int index) => blendKnown[index];
    #endregion
}
