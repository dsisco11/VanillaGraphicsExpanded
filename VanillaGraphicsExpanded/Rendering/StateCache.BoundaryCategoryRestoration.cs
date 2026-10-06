using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Restores independent category fields through their existing transition owner.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Skips unchanged native groups and continues restoring independent groups after failure.</summary>
    private void RestoreGraphicsCategories(PipelineStateSnapshot saved, List<Exception> failures)
    {
        if (saved.OutputKnown.HasFlag(OutputStateKnowledge.LogicOperation)
            && (!outputKnown.HasFlag(OutputStateKnowledge.LogicOperation) || output.LogicOperation != saved.Output.LogicOperation))
        {
            var logicOperation = saved.Output.LogicOperation;
            RestoreBoundaryField(() => SetLogicOperation(logicOperation),
                () => outputKnown &= ~OutputStateKnowledge.LogicOperation, failures);
        }
        if (saved.DepthKnown.HasFlag(DepthStateKnowledge.DepthRange)
            && (!depthKnown.HasFlag(DepthStateKnowledge.DepthRange) || depth.DepthRange != saved.Depth.DepthRange))
        {
            var depthRange = saved.Depth.DepthRange;
            RestoreBoundaryField(() => SetDepthRange(depthRange.Near, depthRange.Far),
                () => depthKnown &= ~DepthStateKnowledge.DepthRange, failures);
        }
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.CullMode)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.CullMode) || rasterizer.CullMode != saved.Rasterizer.CullMode))
        {
            var cullMode = saved.Rasterizer.CullMode;
            RestoreBoundaryField(() => SetCullMode(cullMode),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.CullMode, failures);
        }
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.FrontFace)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.FrontFace) || rasterizer.FrontFace != saved.Rasterizer.FrontFace))
        {
            var frontFace = saved.Rasterizer.FrontFace;
            RestoreBoundaryField(() => SetFrontFace(frontFace),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.FrontFace, failures);
        }
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonModes)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonModes) || rasterizer.PolygonModes != saved.Rasterizer.PolygonModes))
        {
            var polygonModes = saved.Rasterizer.PolygonModes;
            RestoreBoundaryField(() => SetPolygonModes(polygonModes.Front, polygonModes.Back),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonModes, failures);
        }
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffset)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffset) || rasterizer.PolygonOffset != saved.Rasterizer.PolygonOffset))
        {
            var polygonOffset = saved.Rasterizer.PolygonOffset;
            RestoreBoundaryField(() => SetPolygonOffset(polygonOffset.Factor, polygonOffset.Units),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffset, failures);
        }
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverage)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverage) || sampling.SampleCoverage != saved.Sampling.SampleCoverage))
        {
            var sampleCoverage = saved.Sampling.SampleCoverage;
            RestoreBoundaryField(() => SetSampleCoverage(sampleCoverage.Value, sampleCoverage.Invert),
                () => samplingKnown &= ~SamplingStateKnowledge.SampleCoverage, failures);
        }
        if (saved.AssemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.RestartIndex)
            && (!assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.RestartIndex) || assembly.RestartIndex != saved.Assembly.RestartIndex))
        {
            var restartIndex = saved.Assembly.RestartIndex;
            RestoreBoundaryField(() => SetRestartIndex(restartIndex),
                () => assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.RestartIndex, failures);
        }
        if (saved.DynamicKnown.HasFlag(DynamicDrawStateKnowledge.Scissor)
            && (!dynamicKnown.HasFlag(DynamicDrawStateKnowledge.Scissor) || dynamicState.Scissor != saved.Dynamic.Scissor))
        {
            var scissor = saved.Dynamic.Scissor;
            RestoreBoundaryField(() => SetScissor(scissor.X, scissor.Y, scissor.Width, scissor.Height),
                () => dynamicKnown &= ~DynamicDrawStateKnowledge.Scissor, failures);
        }
        if (saved.DynamicKnown.HasFlag(DynamicDrawStateKnowledge.BlendConstant)
            && (!dynamicKnown.HasFlag(DynamicDrawStateKnowledge.BlendConstant) || dynamicState.BlendConstant != saved.Dynamic.BlendConstant))
        {
            var blendConstant = saved.Dynamic.BlendConstant;
            RestoreBoundaryField(() => SetBlendConstant(blendConstant.R, blendConstant.G, blendConstant.B, blendConstant.A),
                () => dynamicKnown &= ~DynamicDrawStateKnowledge.BlendConstant, failures);
        }
        if (saved.StencilKnown.HasFlag(StencilStateKnowledge.FrontStencilFunction)
            && (!stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilFunction) || stencil.FrontStencilFunction != saved.Stencil.FrontStencilFunction))
        {
            var frontStencilFunction = saved.Stencil.FrontStencilFunction;
            RestoreBoundaryField(() => SetStencilFunction(StencilFace.Front, frontStencilFunction.Function, frontStencilFunction.Reference, frontStencilFunction.Mask),
                () => stencilKnown &= ~StencilStateKnowledge.FrontStencilFunction, failures);
        }
        if (saved.StencilKnown.HasFlag(StencilStateKnowledge.BackStencilFunction)
            && (!stencilKnown.HasFlag(StencilStateKnowledge.BackStencilFunction) || stencil.BackStencilFunction != saved.Stencil.BackStencilFunction))
        {
            var backStencilFunction = saved.Stencil.BackStencilFunction;
            RestoreBoundaryField(() => SetStencilFunction(StencilFace.Back, backStencilFunction.Function, backStencilFunction.Reference, backStencilFunction.Mask),
                () => stencilKnown &= ~StencilStateKnowledge.BackStencilFunction, failures);
        }
        if (saved.StencilKnown.HasFlag(StencilStateKnowledge.FrontStencilMask)
            && (!stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilMask) || stencil.FrontStencilMask != saved.Stencil.FrontStencilMask))
        {
            var frontStencilMask = saved.Stencil.FrontStencilMask;
            RestoreBoundaryField(() => SetStencilWriteMask(StencilFace.Front, frontStencilMask),
                () => stencilKnown &= ~StencilStateKnowledge.FrontStencilMask, failures);
        }
        if (saved.StencilKnown.HasFlag(StencilStateKnowledge.BackStencilMask)
            && (!stencilKnown.HasFlag(StencilStateKnowledge.BackStencilMask) || stencil.BackStencilMask != saved.Stencil.BackStencilMask))
        {
            var backStencilMask = saved.Stencil.BackStencilMask;
            RestoreBoundaryField(() => SetStencilWriteMask(StencilFace.Back, backStencilMask),
                () => stencilKnown &= ~StencilStateKnowledge.BackStencilMask, failures);
        }
        if (saved.StencilKnown.HasFlag(StencilStateKnowledge.FrontStencilOperation)
            && (!stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilOperation) || stencil.FrontStencilOperation != saved.Stencil.FrontStencilOperation))
        {
            var frontStencilOperation = saved.Stencil.FrontStencilOperation;
            RestoreBoundaryField(() => SetStencilOperation(StencilFace.Front, frontStencilOperation.Fail, frontStencilOperation.DepthFail, frontStencilOperation.Pass),
                () => stencilKnown &= ~StencilStateKnowledge.FrontStencilOperation, failures);
        }
        if (saved.StencilKnown.HasFlag(StencilStateKnowledge.BackStencilOperation)
            && (!stencilKnown.HasFlag(StencilStateKnowledge.BackStencilOperation) || stencil.BackStencilOperation != saved.Stencil.BackStencilOperation))
        {
            var backStencilOperation = saved.Stencil.BackStencilOperation;
            RestoreBoundaryField(() => SetStencilOperation(StencilFace.Back, backStencilOperation.Fail, backStencilOperation.DepthFail, backStencilOperation.Pass),
                () => stencilKnown &= ~StencilStateKnowledge.BackStencilOperation, failures);
        }
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.MinimumSampleShading)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.MinimumSampleShading) || sampling.MinimumSampleShading != saved.Sampling.MinimumSampleShading))
        {
            var minimumSampleShading = saved.Sampling.MinimumSampleShading;
            RestoreBoundaryField(() => SetMinimumSampleShading(minimumSampleShading),
                () => samplingKnown &= ~SamplingStateKnowledge.MinimumSampleShading, failures);
        }
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.DepthClamp)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.DepthClamp) || rasterizer.DepthClamp != saved.Rasterizer.DepthClamp))
            RestoreBoundaryField(() => SetDepthClampEnabled(saved.Rasterizer.DepthClamp),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.DepthClamp, failures);
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.RasterizerDiscard)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.RasterizerDiscard) || rasterizer.RasterizerDiscard != saved.Rasterizer.RasterizerDiscard))
            RestoreBoundaryField(() => SetRasterizerDiscardEnabled(saved.Rasterizer.RasterizerDiscard),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.RasterizerDiscard, failures);
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill) || rasterizer.PolygonOffsetFill != saved.Rasterizer.PolygonOffsetFill))
            RestoreBoundaryField(() => SetPolygonOffsetFillEnabled(saved.Rasterizer.PolygonOffsetFill),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetFill, failures);
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine) || rasterizer.PolygonOffsetLine != saved.Rasterizer.PolygonOffsetLine))
            RestoreBoundaryField(() => SetPolygonOffsetLineEnabled(saved.Rasterizer.PolygonOffsetLine),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetLine, failures);
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint) || rasterizer.PolygonOffsetPoint != saved.Rasterizer.PolygonOffsetPoint))
            RestoreBoundaryField(() => SetPolygonOffsetPointEnabled(saved.Rasterizer.PolygonOffsetPoint),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetPoint, failures);
        if (saved.RasterizerKnown.HasFlag(RasterizerStateKnowledge.ProgramPointSize)
            && (!rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProgramPointSize) || rasterizer.ProgramPointSize != saved.Rasterizer.ProgramPointSize))
            RestoreBoundaryField(() => SetProgramPointSizeEnabled(saved.Rasterizer.ProgramPointSize),
                () => rasterizerKnown &= ~RasterizerStateKnowledge.ProgramPointSize, failures);
        if (saved.AssemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestart)
            && (!assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestart) || assembly.PrimitiveRestart != saved.Assembly.PrimitiveRestart))
            RestoreBoundaryField(() => SetPrimitiveRestartEnabled(saved.Assembly.PrimitiveRestart),
                () => assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PrimitiveRestart, failures);
        if (saved.AssemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex)
            && (!assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex) || assembly.PrimitiveRestartFixedIndex != saved.Assembly.PrimitiveRestartFixedIndex))
            RestoreBoundaryField(() => SetPrimitiveRestartFixedIndexEnabled(saved.Assembly.PrimitiveRestartFixedIndex),
                () => assemblyKnown &= ~PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex, failures);
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.Multisample)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.Multisample) || sampling.Multisample != saved.Sampling.Multisample))
            RestoreBoundaryField(() => SetMultisampleEnabled(saved.Sampling.Multisample),
                () => samplingKnown &= ~SamplingStateKnowledge.Multisample, failures);
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverageEnabled)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverageEnabled) || sampling.SampleCoverageEnabled != saved.Sampling.SampleCoverageEnabled))
            RestoreBoundaryField(() => SetSampleCoverageEnabled(saved.Sampling.SampleCoverageEnabled),
                () => samplingKnown &= ~SamplingStateKnowledge.SampleCoverageEnabled, failures);
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleMask)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.SampleMask) || sampling.SampleMask != saved.Sampling.SampleMask))
            RestoreBoundaryField(() => SetSampleMaskEnabled(saved.Sampling.SampleMask),
                () => samplingKnown &= ~SamplingStateKnowledge.SampleMask, failures);
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToCoverage)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToCoverage) || sampling.SampleAlphaToCoverage != saved.Sampling.SampleAlphaToCoverage))
            RestoreBoundaryField(() => SetSampleAlphaToCoverageEnabled(saved.Sampling.SampleAlphaToCoverage),
                () => samplingKnown &= ~SamplingStateKnowledge.SampleAlphaToCoverage, failures);
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToOne)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToOne) || sampling.SampleAlphaToOne != saved.Sampling.SampleAlphaToOne))
            RestoreBoundaryField(() => SetSampleAlphaToOneEnabled(saved.Sampling.SampleAlphaToOne),
                () => samplingKnown &= ~SamplingStateKnowledge.SampleAlphaToOne, failures);
        if (saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleShading)
            && (!samplingKnown.HasFlag(SamplingStateKnowledge.SampleShading) || sampling.SampleShading != saved.Sampling.SampleShading))
            RestoreBoundaryField(() => SetSampleShadingEnabled(saved.Sampling.SampleShading),
                () => samplingKnown &= ~SamplingStateKnowledge.SampleShading, failures);
        if (saved.StencilKnown.HasFlag(StencilStateKnowledge.TestEnabled)
            && (!stencilKnown.HasFlag(StencilStateKnowledge.TestEnabled) || stencil.TestEnabled != saved.Stencil.TestEnabled))
            RestoreBoundaryField(() => SetStencilTestEnabled(saved.Stencil.TestEnabled),
                () => stencilKnown &= ~StencilStateKnowledge.TestEnabled, failures);
        if (saved.OutputKnown.HasFlag(OutputStateKnowledge.FramebufferSrgb)
            && (!outputKnown.HasFlag(OutputStateKnowledge.FramebufferSrgb) || output.FramebufferSrgb != saved.Output.FramebufferSrgb))
            RestoreBoundaryField(() => SetFramebufferSrgbEnabled(saved.Output.FramebufferSrgb),
                () => outputKnown &= ~OutputStateKnowledge.FramebufferSrgb, failures);
        if (saved.OutputKnown.HasFlag(OutputStateKnowledge.Dither)
            && (!outputKnown.HasFlag(OutputStateKnowledge.Dither) || output.Dither != saved.Output.Dither))
            RestoreBoundaryField(() => SetDitherEnabled(saved.Output.Dither),
                () => outputKnown &= ~OutputStateKnowledge.Dither, failures);
        if (saved.OutputKnown.HasFlag(OutputStateKnowledge.ColorLogicOp)
            && (!outputKnown.HasFlag(OutputStateKnowledge.ColorLogicOp) || output.ColorLogicOp != saved.Output.ColorLogicOp))
            RestoreBoundaryField(() => SetColorLogicOpEnabled(saved.Output.ColorLogicOp),
                () => outputKnown &= ~OutputStateKnowledge.ColorLogicOp, failures);
        foreach (var entry in saved.SampleMasks)
            if (!sampleMasks.TryGetValue(entry.Key, out var currentSampleMasks) || currentSampleMasks != entry.Value)
                RestoreBoundaryField(() => SetSampleMask(entry.Key, entry.Value),
                    () => sampleMasks.Remove(entry.Key), failures);

    }
    #endregion
}
