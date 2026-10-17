using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Immutable field coverage derived from pipeline intents, independent of their values.</summary>
internal sealed class PipelineStateCoverage
{
    private readonly Dictionary<int, BlendStateKnowledge> outputs;
    internal DepthStateKnowledge Depth { get; }
    internal RasterizerStateKnowledge Rasterizer { get; }
    internal PrimitiveAssemblyStateKnowledge Assembly { get; }
    internal DynamicDrawStateKnowledge Dynamic { get; }
    internal StencilStateKnowledge Stencil { get; }
    internal SamplingStateKnowledge Sampling { get; }
    internal OutputStateKnowledge Output { get; }
    internal bool SampleMaskWords { get; }
    internal BlendStateKnowledge GlobalBlend { get; }
    internal bool ClearColor { get; }
    /// <summary>Admits complete graphics operations without implicitly granting field mutation authority.</summary>
    internal bool CompleteGraphics { get; }
    internal static PipelineStateCoverage Empty { get; } = new();
    /// <summary>Retains conservative authority for application paths that still establish every parameter.</summary>
    internal static PipelineStateCoverage FullGraphics { get; } = new(DepthStateKnowledge.All,
        RasterizerStateKnowledge.All, PrimitiveAssemblyStateKnowledge.All, DynamicDrawStateKnowledge.All,
        BlendStateKnowledge.All, completeGraphics: true, stencil: StencilStateKnowledge.All,
        sampling: SamplingStateKnowledge.All, output: OutputStateKnowledge.All, sampleMaskWords: true);

    #region Public API
    #region Declaration composition
    /// <summary>Declares individual helper effects; global blend fields cover every native output.</summary>
    internal PipelineStateCoverage(DepthStateKnowledge depth = default,
        RasterizerStateKnowledge rasterizer = default, PrimitiveAssemblyStateKnowledge assembly = default,
        DynamicDrawStateKnowledge dynamic = default, BlendStateKnowledge globalBlend = default,
        bool clearColor = false, IReadOnlyDictionary<int, BlendStateKnowledge>? indexedBlend = null,
        bool completeGraphics = false, StencilStateKnowledge stencil = default,
        SamplingStateKnowledge sampling = default, OutputStateKnowledge output = default,
        bool sampleMaskWords = false)
    {
        if (!DepthStateKnowledge.All.HasFlag(depth) || !RasterizerStateKnowledge.All.HasFlag(rasterizer)
            || !PrimitiveAssemblyStateKnowledge.All.HasFlag(assembly) || !DynamicDrawStateKnowledge.All.HasFlag(dynamic)
            || !BlendStateKnowledge.All.HasFlag(globalBlend) || !StencilStateKnowledge.All.HasFlag(stencil)
            || !SamplingStateKnowledge.All.HasFlag(sampling) || !OutputStateKnowledge.All.HasFlag(output))
            throw new ArgumentOutOfRangeException(nameof(depth));
        // Admission is independent of write groups. Partial helpers retain their established field set.
        const DepthStateKnowledge fullDepth = DepthStateKnowledge.DepthRange;
        const RasterizerStateKnowledge fullRaster = RasterizerStateKnowledge.CullMode | RasterizerStateKnowledge.FrontFace
            | RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.PolygonOffset | RasterizerStateKnowledge.DepthClamp
            | RasterizerStateKnowledge.RasterizerDiscard | RasterizerStateKnowledge.PolygonOffsetFill | RasterizerStateKnowledge.PolygonOffsetLine
            | RasterizerStateKnowledge.PolygonOffsetPoint | RasterizerStateKnowledge.ProgramPointSize;
        const PrimitiveAssemblyStateKnowledge fullAssembly = PrimitiveAssemblyStateKnowledge.RestartIndex
            | PrimitiveAssemblyStateKnowledge.PrimitiveRestart | PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
        const DynamicDrawStateKnowledge fullDynamic = DynamicDrawStateKnowledge.Scissor | DynamicDrawStateKnowledge.BlendConstant;
        if (!completeGraphics && ((depth & fullDepth) != 0 || (rasterizer & fullRaster) != 0
            || (assembly & fullAssembly) != 0 || (dynamic & fullDynamic) != 0
            || stencil != default || sampling != default || output != default || sampleMaskWords))
            throw new ArgumentException("These fields require complete graphics boundary coverage.");
        Depth = depth; Rasterizer = rasterizer; Assembly = assembly; Dynamic = dynamic;
        GlobalBlend = globalBlend; ClearColor = clearColor; CompleteGraphics = completeGraphics;
        Stencil = stencil; Sampling = sampling; Output = output; SampleMaskWords = sampleMaskWords;
        outputs = new();
        if (indexedBlend is null) return;
        foreach (var entry in indexedBlend)
        {
            if (entry.Key < 0 || !BlendStateKnowledge.All.HasFlag(entry.Value))
                throw new ArgumentOutOfRangeException(nameof(indexedBlend));
            outputs.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>Copies descriptor intent, including indexed payload indices but never pipeline values.</summary>
    internal static PipelineStateCoverage From(in GlPipelineDesc descriptor)
    {
        GlPipelineStateValidation.ValidateDesc(descriptor);
        var desc = descriptor;
        DepthStateKnowledge depth = default;
        RasterizerStateKnowledge rasterizer = default;
        BlendStateKnowledge global = default;
        var indexed = new Dictionary<int, BlendStateKnowledge>();
        if (HasIntent(desc, GlPipelineStateId.DepthTestEnable)) depth |= DepthStateKnowledge.TestEnabled;
        if (HasIntent(desc, GlPipelineStateId.DepthFunc)) depth |= DepthStateKnowledge.Comparison;
        if (HasIntent(desc, GlPipelineStateId.DepthWriteMask)) depth |= DepthStateKnowledge.WriteEnabled;
        if (HasIntent(desc, GlPipelineStateId.CullFaceEnable)) rasterizer |= RasterizerStateKnowledge.CullEnabled;
        if (HasIntent(desc, GlPipelineStateId.ScissorTestEnable)) rasterizer |= RasterizerStateKnowledge.ScissorEnabled;
        if (HasIntent(desc, GlPipelineStateId.LineWidth)) rasterizer |= RasterizerStateKnowledge.LineWidth;
        if (HasIntent(desc, GlPipelineStateId.PointSize)) rasterizer |= RasterizerStateKnowledge.PointSize;
        if (HasIntent(desc, GlPipelineStateId.BlendEnable)) global |= BlendStateKnowledge.Enabled;
        if (HasIntent(desc, GlPipelineStateId.BlendFunc)) global |= BlendStateKnowledge.Factors;
        if (HasIntent(desc, GlPipelineStateId.ColorMask)) global |= BlendStateKnowledge.WriteMask;
        if (HasIntent(desc, GlPipelineStateId.BlendEnableIndexed))
            foreach (int index in desc.BlendEnableIndexedAttachments ?? throw new ArgumentException("Missing indexed enable payload."))
                indexed[index] = BlendStateKnowledge.Enabled;
        if (HasIntent(desc, GlPipelineStateId.BlendFuncIndexed))
            foreach (var value in desc.BlendFuncIndexed ?? throw new ArgumentException("Missing indexed factor payload."))
            {
                indexed.TryGetValue(value.AttachmentIndex, out var previous);
                indexed[value.AttachmentIndex] = previous | BlendStateKnowledge.Factors;
            }
        return new(depth, rasterizer, globalBlend: global, indexedBlend: indexed);
    }

    /// <summary>Preserves all drawing fields affected by a complete graphics application.</summary>
    internal static PipelineStateCoverage From(GraphicsPipelineDesc descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return FullGraphics;
    }

    /// <summary>Copies prepared potential write groups, preserving indexed slots and global native aliases.</summary>
    internal static PipelineStateCoverage From(GraphicsStateRelevance relevance)
    {
        ArgumentNullException.ThrowIfNull(relevance);
        var indexed = new Dictionary<int, BlendStateKnowledge>();
        for (int slot = 0; slot < relevance.Blending.Count; slot++)
            if (relevance.Blending[slot] != BlendStateKnowledge.None) indexed.Add(slot, relevance.Blending[slot]);
        return new(relevance.Depth, relevance.Rasterizer, relevance.Assembly, relevance.Dynamic,
            relevance.GlobalBlend, indexedBlend: indexed, completeGraphics: true,
            stencil: relevance.Stencil, sampling: relevance.Sampling, output: relevance.Output,
            sampleMaskWords: relevance.SampleMaskWords);
    }

    /// <summary>Combines independent descriptor, dynamic and helper effects without sharing mutable payloads.</summary>
    internal PipelineStateCoverage Union(PipelineStateCoverage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var combined = new Dictionary<int, BlendStateKnowledge>(outputs);
        foreach (var entry in other.outputs)
        {
            combined.TryGetValue(entry.Key, out var previous);
            combined[entry.Key] = previous | entry.Value;
        }
        return new(Depth | other.Depth, Rasterizer | other.Rasterizer, Assembly | other.Assembly,
            Dynamic | other.Dynamic, GlobalBlend | other.GlobalBlend, ClearColor || other.ClearColor, combined,
            CompleteGraphics || other.CompleteGraphics, Stencil | other.Stencil, Sampling | other.Sampling,
            Output | other.Output, SampleMaskWords || other.SampleMaskWords);
    }

    #endregion
    #region Coverage validation
    /// <summary>Returns effective coverage for a draw-output slot, including global aliases.</summary>
    internal BlendStateKnowledge BlendAt(int index) => GlobalBlend | outputs.GetValueOrDefault(index);

    /// <summary>Rejects indexed declarations unsupported by the current context.</summary>
    internal void ValidateOutputCount(int count)
    {
        foreach (int index in outputs.Keys)
            if (index >= count) throw new ArgumentOutOfRangeException(nameof(count), "Unsupported draw-output index.");
    }

    /// <summary>Checks complete operation coverage before any part of a managed operation executes.</summary>
    internal bool Contains(PipelineStateCoverage operation, int outputCount)
    {
        if (!Depth.HasFlag(operation.Depth) || !Rasterizer.HasFlag(operation.Rasterizer)
            || !Assembly.HasFlag(operation.Assembly) || !Dynamic.HasFlag(operation.Dynamic)
            || !Stencil.HasFlag(operation.Stencil) || !Sampling.HasFlag(operation.Sampling)
            || !Output.HasFlag(operation.Output) || (operation.SampleMaskWords && !SampleMaskWords)
            || (operation.ClearColor && !ClearColor) || (operation.CompleteGraphics && !CompleteGraphics)) return false;
        operation.ValidateOutputCount(outputCount);
        for (int i = 0; i < outputCount; i++)
            if (!BlendAt(i).HasFlag(operation.BlendAt(i))) return false;
        return true;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Treats both default and explicit values as native mutation intent.</summary>
    private static bool HasIntent(in GlPipelineDesc desc, GlPipelineStateId id) =>
        desc.DefaultMask.Contains(id) || desc.NonDefaultMask.Contains(id);
    #endregion
}
