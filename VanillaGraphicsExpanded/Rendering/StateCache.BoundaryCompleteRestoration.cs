using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Restores independent supplemental fields through their existing transition owner.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Skips unchanged native groups and continues restoring independent groups after failure.</summary>
    private void RestoreCompleteGraphics(PipelineStateSnapshot saved, List<Exception> failures)
    {
        if (saved.Output.Known.HasFlag(CompleteOutputKnowledge.LogicOperation)
            && (!completeOutput.Known.HasFlag(CompleteOutputKnowledge.LogicOperation) || completeOutput.LogicOperation != saved.Output.LogicOperation))
        {
            var logicOperation = saved.Output.LogicOperation;
            RestoreBoundaryField(() => SetLogicOperation(logicOperation),
                () => completeOutput.Known &= ~CompleteOutputKnowledge.LogicOperation, failures);
        }
        if (saved.Depth.SupplementalKnown.HasFlag(CompleteDepthKnowledge.DepthRange)
            && (!depth.SupplementalKnown.HasFlag(CompleteDepthKnowledge.DepthRange) || depth.DepthRange != saved.Depth.DepthRange))
        {
            var depthRange = saved.Depth.DepthRange;
            RestoreBoundaryField(() => SetDepthRange(depthRange.Near, depthRange.Far),
                () => depth.SupplementalKnown &= ~CompleteDepthKnowledge.DepthRange, failures);
        }
        if (saved.Rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.CullMode)
            && (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.CullMode) || rasterizer.CullMode != saved.Rasterizer.CullMode))
        {
            var cullMode = saved.Rasterizer.CullMode;
            RestoreBoundaryField(() => SetCullMode(cullMode),
                () => rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.CullMode, failures);
        }
        if (saved.Rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.FrontFace)
            && (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.FrontFace) || rasterizer.FrontFace != saved.Rasterizer.FrontFace))
        {
            var frontFace = saved.Rasterizer.FrontFace;
            RestoreBoundaryField(() => SetFrontFace(frontFace),
                () => rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.FrontFace, failures);
        }
        if (saved.Rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonModes)
            && (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonModes) || rasterizer.PolygonModes != saved.Rasterizer.PolygonModes))
        {
            var polygonModes = saved.Rasterizer.PolygonModes;
            RestoreBoundaryField(() => SetPolygonModes(polygonModes.Front, polygonModes.Back),
                () => rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.PolygonModes, failures);
        }
        if (saved.Rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonOffset)
            && (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonOffset) || rasterizer.PolygonOffset != saved.Rasterizer.PolygonOffset))
        {
            var polygonOffset = saved.Rasterizer.PolygonOffset;
            RestoreBoundaryField(() => SetPolygonOffset(polygonOffset.Factor, polygonOffset.Units),
                () => rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.PolygonOffset, failures);
        }
        if (saved.Sampling.Known.HasFlag(CompleteSamplingKnowledge.SampleCoverage)
            && (!completeSampling.Known.HasFlag(CompleteSamplingKnowledge.SampleCoverage) || completeSampling.SampleCoverage != saved.Sampling.SampleCoverage))
        {
            var sampleCoverage = saved.Sampling.SampleCoverage;
            RestoreBoundaryField(() => SetSampleCoverage(sampleCoverage.Value, sampleCoverage.Invert),
                () => completeSampling.Known &= ~CompleteSamplingKnowledge.SampleCoverage, failures);
        }
        if (saved.Assembly.SupplementalKnown.HasFlag(CompleteAssemblyKnowledge.RestartIndex)
            && (!assembly.SupplementalKnown.HasFlag(CompleteAssemblyKnowledge.RestartIndex) || assembly.RestartIndex != saved.Assembly.RestartIndex))
        {
            var restartIndex = saved.Assembly.RestartIndex;
            RestoreBoundaryField(() => SetRestartIndex(restartIndex),
                () => assembly.SupplementalKnown &= ~CompleteAssemblyKnowledge.RestartIndex, failures);
        }
        if (saved.Dynamic.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.Scissor)
            && (!dynamicState.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.Scissor) || dynamicState.Scissor != saved.Dynamic.Scissor))
        {
            var scissor = saved.Dynamic.Scissor;
            RestoreBoundaryField(() => SetScissor(scissor.X, scissor.Y, scissor.Width, scissor.Height),
                () => dynamicState.SupplementalKnown &= ~CompleteDynamicKnowledge.Scissor, failures);
        }
        if (saved.Dynamic.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.BlendConstant)
            && (!dynamicState.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.BlendConstant) || dynamicState.BlendConstant != saved.Dynamic.BlendConstant))
        {
            var blendConstant = saved.Dynamic.BlendConstant;
            RestoreBoundaryField(() => SetBlendConstant(blendConstant.R, blendConstant.G, blendConstant.B, blendConstant.A),
                () => dynamicState.SupplementalKnown &= ~CompleteDynamicKnowledge.BlendConstant, failures);
        }
        if (saved.Stencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilFunction)
            && (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilFunction) || completeStencil.FrontStencilFunction != saved.Stencil.FrontStencilFunction))
        {
            var frontStencilFunction = saved.Stencil.FrontStencilFunction;
            RestoreBoundaryField(() => SetStencilFunction(StencilFace.Front, frontStencilFunction.Function, frontStencilFunction.Reference, frontStencilFunction.Mask),
                () => completeStencil.Known &= ~CompleteStencilKnowledge.FrontStencilFunction, failures);
        }
        if (saved.Stencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilFunction)
            && (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilFunction) || completeStencil.BackStencilFunction != saved.Stencil.BackStencilFunction))
        {
            var backStencilFunction = saved.Stencil.BackStencilFunction;
            RestoreBoundaryField(() => SetStencilFunction(StencilFace.Back, backStencilFunction.Function, backStencilFunction.Reference, backStencilFunction.Mask),
                () => completeStencil.Known &= ~CompleteStencilKnowledge.BackStencilFunction, failures);
        }
        if (saved.Stencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilMask)
            && (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilMask) || completeStencil.FrontStencilMask != saved.Stencil.FrontStencilMask))
        {
            var frontStencilMask = saved.Stencil.FrontStencilMask;
            RestoreBoundaryField(() => SetStencilWriteMask(StencilFace.Front, frontStencilMask),
                () => completeStencil.Known &= ~CompleteStencilKnowledge.FrontStencilMask, failures);
        }
        if (saved.Stencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilMask)
            && (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilMask) || completeStencil.BackStencilMask != saved.Stencil.BackStencilMask))
        {
            var backStencilMask = saved.Stencil.BackStencilMask;
            RestoreBoundaryField(() => SetStencilWriteMask(StencilFace.Back, backStencilMask),
                () => completeStencil.Known &= ~CompleteStencilKnowledge.BackStencilMask, failures);
        }
        if (saved.Stencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilOperation)
            && (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilOperation) || completeStencil.FrontStencilOperation != saved.Stencil.FrontStencilOperation))
        {
            var frontStencilOperation = saved.Stencil.FrontStencilOperation;
            RestoreBoundaryField(() => SetStencilOperation(StencilFace.Front, frontStencilOperation.Fail, frontStencilOperation.DepthFail, frontStencilOperation.Pass),
                () => completeStencil.Known &= ~CompleteStencilKnowledge.FrontStencilOperation, failures);
        }
        if (saved.Stencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilOperation)
            && (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilOperation) || completeStencil.BackStencilOperation != saved.Stencil.BackStencilOperation))
        {
            var backStencilOperation = saved.Stencil.BackStencilOperation;
            RestoreBoundaryField(() => SetStencilOperation(StencilFace.Back, backStencilOperation.Fail, backStencilOperation.DepthFail, backStencilOperation.Pass),
                () => completeStencil.Known &= ~CompleteStencilKnowledge.BackStencilOperation, failures);
        }
        if (saved.Sampling.Known.HasFlag(CompleteSamplingKnowledge.MinimumSampleShading)
            && (!completeSampling.Known.HasFlag(CompleteSamplingKnowledge.MinimumSampleShading) || completeSampling.MinimumSampleShading != saved.Sampling.MinimumSampleShading))
        {
            var minimumSampleShading = saved.Sampling.MinimumSampleShading;
            RestoreBoundaryField(() => SetMinimumSampleShading(minimumSampleShading),
                () => completeSampling.Known &= ~CompleteSamplingKnowledge.MinimumSampleShading, failures);
        }
        RestoreCompleteEnables(saved.Rasterizer.SupplementalEnables, failures);
        RestoreCompleteEnables(saved.Assembly.SupplementalEnables, failures);
        RestoreCompleteEnables(saved.Stencil.Enables, failures);
        RestoreCompleteEnables(saved.Sampling.Enables, failures);
        RestoreCompleteEnables(saved.Output.Enables, failures);
        foreach (var entry in saved.SampleMasks)
            if (!completeSampleMasks.TryGetValue(entry.Key, out var currentSampleMasks) || currentSampleMasks != entry.Value)
                RestoreBoundaryField(() => SetSampleMask(entry.Key, entry.Value),
                    () => completeSampleMasks.Remove(entry.Key), failures);

    }
    /// <summary>Restores only known enables retained by the copied category.</summary>
    private void RestoreCompleteEnables(CompleteEnableState saved, List<Exception> failures)
    {
        foreach (var capability in SupplementalCapabilities)
        {
            var flag = SupplementalEnableFlag(capability);
            if (!saved.Known.HasFlag(flag)) continue;
            ref var current = ref SupplementalEnableStorage(capability);
            bool enabled = saved.Values.HasFlag(flag);
            if (current.Known.HasFlag(flag) && current.Values.HasFlag(flag) == enabled) continue;
            RestoreBoundaryField(() => SetCompleteEnable(capability, enabled),
                () => { ref var state = ref SupplementalEnableStorage(capability); state.Known &= ~flag; }, failures);
        }
    }
    #endregion
}
