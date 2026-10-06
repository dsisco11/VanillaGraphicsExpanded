using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolves supplemental drawing values without canonicalizing inactive engine parameters.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Resolves unknown values directly into their category, publishing each completed query group.</summary>
    private void ResolveCompleteGraphics()
    {
        // Publish knowledge only after all native reads for the field succeed.
        var support = GpuSupport.Graphics;
        foreach (var cap in SupplementalCapabilities)
        {
            if (cap == EnableCap.SampleShading && !support.SampleShading
                || cap == EnableCap.PrimitiveRestartFixedIndex && !support.FixedIndexRestart) continue;
            ResolveCompleteEnable(cap);
        }
        if (!completeOutput.Known.HasFlag(CompleteOutputKnowledge.LogicOperation))
        {
            completeOutput.LogicOperation = QueryBoundary(() => (LogicOp)GL.GetInteger(GetPName.LogicOpMode));
            completeOutput.Known |= CompleteOutputKnowledge.LogicOperation;
        }
        if (!depth.SupplementalKnown.HasFlag(CompleteDepthKnowledge.DepthRange))
        {
            depth.DepthRange = QueryBoundary(() => { double[] value = new double[2]; GL.GetDouble(GetPName.DepthRange, value); return (value[0], value[1]); });
            depth.SupplementalKnown |= CompleteDepthKnowledge.DepthRange;
        }
        if (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.CullMode))
        {
            rasterizer.CullMode = QueryBoundary(() => (CullFaceMode)GL.GetInteger(GetPName.CullFaceMode));
            rasterizer.SupplementalKnown |= CompleteRasterKnowledge.CullMode;
        }
        if (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.FrontFace))
        {
            rasterizer.FrontFace = QueryBoundary(() => (FrontFaceDirection)GL.GetInteger(GetPName.FrontFace));
            rasterizer.SupplementalKnown |= CompleteRasterKnowledge.FrontFace;
        }
        if (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonModes))
        {
            rasterizer.PolygonModes = QueryBoundary(() =>
            {
                int[] value = new int[2]; GL.GetInteger(GetPName.PolygonMode, value);
                return ((PolygonMode)value[0], (PolygonMode)(support.CoreProfile ? value[0] : value[1]));
            });
            rasterizer.SupplementalKnown |= CompleteRasterKnowledge.PolygonModes;
        }
        if (!rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonOffset))
        {
            rasterizer.PolygonOffset = (QueryBoundary(() => GL.GetFloat(GetPName.PolygonOffsetFactor)), QueryBoundary(() => GL.GetFloat(GetPName.PolygonOffsetUnits)));
            rasterizer.SupplementalKnown |= CompleteRasterKnowledge.PolygonOffset;
        }
        if (!completeSampling.Known.HasFlag(CompleteSamplingKnowledge.SampleCoverage))
        {
            completeSampling.SampleCoverage = (QueryBoundary(() => GL.GetFloat(GetPName.SampleCoverageValue)), QueryBoundary(() => GL.GetBoolean(GetPName.SampleCoverageInvert)));
            completeSampling.Known |= CompleteSamplingKnowledge.SampleCoverage;
        }
        if (!assembly.SupplementalKnown.HasFlag(CompleteAssemblyKnowledge.RestartIndex))
        {
            assembly.RestartIndex = QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.PrimitiveRestartIndex)));
            assembly.SupplementalKnown |= CompleteAssemblyKnowledge.RestartIndex;
        }
        if (!dynamicState.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.Scissor))
        {
            dynamicState.Scissor = QueryBoundary(() => { int[] value = new int[4]; GL.GetInteger(GetPName.ScissorBox, value); return (value[0], value[1], value[2], value[3]); });
            dynamicState.SupplementalKnown |= CompleteDynamicKnowledge.Scissor;
        }
        if (!dynamicState.SupplementalKnown.HasFlag(CompleteDynamicKnowledge.BlendConstant))
        {
            dynamicState.BlendConstant = QueryBoundary(() => { float[] value = new float[4]; GL.GetFloat(GetPName.BlendColor, value); return (value[0], value[1], value[2], value[3]); });
            dynamicState.SupplementalKnown |= CompleteDynamicKnowledge.BlendConstant;
        }
        if (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilFunction))
        {
            completeStencil.FrontStencilFunction = (QueryBoundary(() => (StencilFunction)GL.GetInteger(GetPName.StencilFunc)), QueryBoundary(() => GL.GetInteger(GetPName.StencilRef)), QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilValueMask))));
            completeStencil.Known |= CompleteStencilKnowledge.FrontStencilFunction;
        }
        if (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilFunction))
        {
            completeStencil.BackStencilFunction = (QueryBoundary(() => (StencilFunction)GL.GetInteger(GetPName.StencilBackFunc)), QueryBoundary(() => GL.GetInteger(GetPName.StencilBackRef)), QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilBackValueMask))));
            completeStencil.Known |= CompleteStencilKnowledge.BackStencilFunction;
        }
        if (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilMask))
        {
            completeStencil.FrontStencilMask = QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilWritemask)));
            completeStencil.Known |= CompleteStencilKnowledge.FrontStencilMask;
        }
        if (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilMask))
        {
            completeStencil.BackStencilMask = QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilBackWritemask)));
            completeStencil.Known |= CompleteStencilKnowledge.BackStencilMask;
        }
        if (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilOperation))
        {
            completeStencil.FrontStencilOperation = (QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilPassDepthFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilPassDepthPass)));
            completeStencil.Known |= CompleteStencilKnowledge.FrontStencilOperation;
        }
        if (!completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilOperation))
        {
            completeStencil.BackStencilOperation = (QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilBackFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilBackPassDepthFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilBackPassDepthPass)));
            completeStencil.Known |= CompleteStencilKnowledge.BackStencilOperation;
        }
        if (support.SampleShading && !completeSampling.Known.HasFlag(CompleteSamplingKnowledge.MinimumSampleShading))
        {
            completeSampling.MinimumSampleShading = QueryBoundary(() => GL.GetFloat((GetPName)All.MinSampleShadingValue));
            completeSampling.Known |= CompleteSamplingKnowledge.MinimumSampleShading;
        }
        for (int index = 0; index < support.MaxSampleMaskWords; index++)
        {
            int word = index;
            if (!completeSampleMasks.ContainsKey(word))
                completeSampleMasks[word] = unchecked((uint)QueryBoundaryIndexed((GetPName)All.SampleMaskValue, word));
        }

    }

    /// <summary>Publishes an enable only after its native read succeeds.</summary>
    private void ResolveCompleteEnable(EnableCap capability)
    {
        ref var state = ref SupplementalEnableStorage(capability);
        var flag = SupplementalEnableFlag(capability);
        if (state.Known.HasFlag(flag)) return;
        bool enabled = QueryBoundary(() => GL.IsEnabled(capability));
        if (enabled) state.Values |= flag; else state.Values &= ~flag;
        state.Known |= flag;
    }
    #endregion
}
