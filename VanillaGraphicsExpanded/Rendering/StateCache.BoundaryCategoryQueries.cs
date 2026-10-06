using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolves category drawing values without canonicalizing inactive engine parameters.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Resolves unknown values directly into their category, publishing each completed query group.</summary>
    private void ResolveGraphicsCategories(PipelineStateCoverage coverage)
    {
        // Publish knowledge only after all native reads for the field succeed.
        var support = GpuSupport.Graphics;
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.DepthClamp) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.DepthClamp))
        {
            rasterizer.DepthClamp = QueryBoundary(() => GL.IsEnabled(EnableCap.DepthClamp));
            rasterizerKnown |= RasterizerStateKnowledge.DepthClamp;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.RasterizerDiscard) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.RasterizerDiscard))
        {
            rasterizer.RasterizerDiscard = QueryBoundary(() => GL.IsEnabled(EnableCap.RasterizerDiscard));
            rasterizerKnown |= RasterizerStateKnowledge.RasterizerDiscard;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill))
        {
            rasterizer.PolygonOffsetFill = QueryBoundary(() => GL.IsEnabled(EnableCap.PolygonOffsetFill));
            rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetFill;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine))
        {
            rasterizer.PolygonOffsetLine = QueryBoundary(() => GL.IsEnabled(EnableCap.PolygonOffsetLine));
            rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetLine;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint))
        {
            rasterizer.PolygonOffsetPoint = QueryBoundary(() => GL.IsEnabled(EnableCap.PolygonOffsetPoint));
            rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetPoint;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.ProgramPointSize) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProgramPointSize))
        {
            rasterizer.ProgramPointSize = QueryBoundary(() => GL.IsEnabled(EnableCap.ProgramPointSize));
            rasterizerKnown |= RasterizerStateKnowledge.ProgramPointSize;
        }
        if (coverage.Assembly.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestart) && !assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestart))
        {
            assembly.PrimitiveRestart = QueryBoundary(() => GL.IsEnabled(EnableCap.PrimitiveRestart));
            assemblyKnown |= PrimitiveAssemblyStateKnowledge.PrimitiveRestart;
        }
        if (coverage.Assembly.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex) && support.FixedIndexRestart && !assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex))
        {
            assembly.PrimitiveRestartFixedIndex = QueryBoundary(() => GL.IsEnabled(EnableCap.PrimitiveRestartFixedIndex));
            assemblyKnown |= PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
        }
        if (coverage.CompleteGraphics && !samplingKnown.HasFlag(SamplingStateKnowledge.Multisample))
        {
            sampling.Multisample = QueryBoundary(() => GL.IsEnabled(EnableCap.Multisample));
            samplingKnown |= SamplingStateKnowledge.Multisample;
        }
        if (coverage.CompleteGraphics && !samplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverageEnabled))
        {
            sampling.SampleCoverageEnabled = QueryBoundary(() => GL.IsEnabled(EnableCap.SampleCoverage));
            samplingKnown |= SamplingStateKnowledge.SampleCoverageEnabled;
        }
        if (coverage.CompleteGraphics && !samplingKnown.HasFlag(SamplingStateKnowledge.SampleMask))
        {
            sampling.SampleMask = QueryBoundary(() => GL.IsEnabled(EnableCap.SampleMask));
            samplingKnown |= SamplingStateKnowledge.SampleMask;
        }
        if (coverage.CompleteGraphics && !samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToCoverage))
        {
            sampling.SampleAlphaToCoverage = QueryBoundary(() => GL.IsEnabled(EnableCap.SampleAlphaToCoverage));
            samplingKnown |= SamplingStateKnowledge.SampleAlphaToCoverage;
        }
        if (coverage.CompleteGraphics && !samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToOne))
        {
            sampling.SampleAlphaToOne = QueryBoundary(() => GL.IsEnabled(EnableCap.SampleAlphaToOne));
            samplingKnown |= SamplingStateKnowledge.SampleAlphaToOne;
        }
        if (coverage.CompleteGraphics && support.SampleShading && !samplingKnown.HasFlag(SamplingStateKnowledge.SampleShading))
        {
            sampling.SampleShading = QueryBoundary(() => GL.IsEnabled(EnableCap.SampleShading));
            samplingKnown |= SamplingStateKnowledge.SampleShading;
        }
        if (coverage.CompleteGraphics && !stencilKnown.HasFlag(StencilStateKnowledge.TestEnabled))
        {
            stencil.TestEnabled = QueryBoundary(() => GL.IsEnabled(EnableCap.StencilTest));
            stencilKnown |= StencilStateKnowledge.TestEnabled;
        }
        if (coverage.CompleteGraphics && !outputKnown.HasFlag(OutputStateKnowledge.FramebufferSrgb))
        {
            output.FramebufferSrgb = QueryBoundary(() => GL.IsEnabled(EnableCap.FramebufferSrgb));
            outputKnown |= OutputStateKnowledge.FramebufferSrgb;
        }
        if (coverage.CompleteGraphics && !outputKnown.HasFlag(OutputStateKnowledge.Dither))
        {
            output.Dither = QueryBoundary(() => GL.IsEnabled(EnableCap.Dither));
            outputKnown |= OutputStateKnowledge.Dither;
        }
        if (coverage.CompleteGraphics && !outputKnown.HasFlag(OutputStateKnowledge.ColorLogicOp))
        {
            output.ColorLogicOp = QueryBoundary(() => GL.IsEnabled(EnableCap.ColorLogicOp));
            outputKnown |= OutputStateKnowledge.ColorLogicOp;
        }
        if (coverage.CompleteGraphics && !outputKnown.HasFlag(OutputStateKnowledge.LogicOperation))
        {
            output.LogicOperation = QueryBoundary(() => (LogicOp)GL.GetInteger(GetPName.LogicOpMode));
            outputKnown |= OutputStateKnowledge.LogicOperation;
        }
        if (coverage.Depth.HasFlag(DepthStateKnowledge.DepthRange) && !depthKnown.HasFlag(DepthStateKnowledge.DepthRange))
        {
            depth.DepthRange = QueryBoundary(() => { double[] value = new double[2]; GL.GetDouble(GetPName.DepthRange, value); return (value[0], value[1]); });
            depthKnown |= DepthStateKnowledge.DepthRange;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.CullMode) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.CullMode))
        {
            rasterizer.CullMode = QueryBoundary(() => (CullFaceMode)GL.GetInteger(GetPName.CullFaceMode));
            rasterizerKnown |= RasterizerStateKnowledge.CullMode;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.FrontFace) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.FrontFace))
        {
            rasterizer.FrontFace = QueryBoundary(() => (FrontFaceDirection)GL.GetInteger(GetPName.FrontFace));
            rasterizerKnown |= RasterizerStateKnowledge.FrontFace;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonModes) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonModes))
        {
            rasterizer.PolygonModes = QueryBoundary(() =>
            {
                int[] value = new int[2]; GL.GetInteger(GetPName.PolygonMode, value);
                return ((PolygonMode)value[0], (PolygonMode)(support.CoreProfile ? value[0] : value[1]));
            });
            rasterizerKnown |= RasterizerStateKnowledge.PolygonModes;
        }
        if (coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffset) && !rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffset))
        {
            rasterizer.PolygonOffset = (QueryBoundary(() => GL.GetFloat(GetPName.PolygonOffsetFactor)), QueryBoundary(() => GL.GetFloat(GetPName.PolygonOffsetUnits)));
            rasterizerKnown |= RasterizerStateKnowledge.PolygonOffset;
        }
        if (coverage.CompleteGraphics && !samplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverage))
        {
            sampling.SampleCoverage = (QueryBoundary(() => GL.GetFloat(GetPName.SampleCoverageValue)), QueryBoundary(() => GL.GetBoolean(GetPName.SampleCoverageInvert)));
            samplingKnown |= SamplingStateKnowledge.SampleCoverage;
        }
        if (coverage.Assembly.HasFlag(PrimitiveAssemblyStateKnowledge.RestartIndex) && !assemblyKnown.HasFlag(PrimitiveAssemblyStateKnowledge.RestartIndex))
        {
            assembly.RestartIndex = QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.PrimitiveRestartIndex)));
            assemblyKnown |= PrimitiveAssemblyStateKnowledge.RestartIndex;
        }
        if (coverage.Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor) && !dynamicKnown.HasFlag(DynamicDrawStateKnowledge.Scissor))
        {
            dynamicState.Scissor = QueryBoundary(() => { int[] value = new int[4]; GL.GetInteger(GetPName.ScissorBox, value); return (value[0], value[1], value[2], value[3]); });
            dynamicKnown |= DynamicDrawStateKnowledge.Scissor;
        }
        if (coverage.Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant) && !dynamicKnown.HasFlag(DynamicDrawStateKnowledge.BlendConstant))
        {
            dynamicState.BlendConstant = QueryBoundary(() => { float[] value = new float[4]; GL.GetFloat(GetPName.BlendColor, value); return (value[0], value[1], value[2], value[3]); });
            dynamicKnown |= DynamicDrawStateKnowledge.BlendConstant;
        }
        if (coverage.CompleteGraphics && !stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilFunction))
        {
            stencil.FrontStencilFunction = (QueryBoundary(() => (StencilFunction)GL.GetInteger(GetPName.StencilFunc)), QueryBoundary(() => GL.GetInteger(GetPName.StencilRef)), QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilValueMask))));
            stencilKnown |= StencilStateKnowledge.FrontStencilFunction;
        }
        if (coverage.CompleteGraphics && !stencilKnown.HasFlag(StencilStateKnowledge.BackStencilFunction))
        {
            stencil.BackStencilFunction = (QueryBoundary(() => (StencilFunction)GL.GetInteger(GetPName.StencilBackFunc)), QueryBoundary(() => GL.GetInteger(GetPName.StencilBackRef)), QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilBackValueMask))));
            stencilKnown |= StencilStateKnowledge.BackStencilFunction;
        }
        if (coverage.CompleteGraphics && !stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilMask))
        {
            stencil.FrontStencilMask = QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilWritemask)));
            stencilKnown |= StencilStateKnowledge.FrontStencilMask;
        }
        if (coverage.CompleteGraphics && !stencilKnown.HasFlag(StencilStateKnowledge.BackStencilMask))
        {
            stencil.BackStencilMask = QueryBoundary(() => unchecked((uint)GL.GetInteger(GetPName.StencilBackWritemask)));
            stencilKnown |= StencilStateKnowledge.BackStencilMask;
        }
        if (coverage.CompleteGraphics && !stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilOperation))
        {
            stencil.FrontStencilOperation = (QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilPassDepthFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilPassDepthPass)));
            stencilKnown |= StencilStateKnowledge.FrontStencilOperation;
        }
        if (coverage.CompleteGraphics && !stencilKnown.HasFlag(StencilStateKnowledge.BackStencilOperation))
        {
            stencil.BackStencilOperation = (QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilBackFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilBackPassDepthFail)), QueryBoundary(() => (StencilOp)GL.GetInteger(GetPName.StencilBackPassDepthPass)));
            stencilKnown |= StencilStateKnowledge.BackStencilOperation;
        }
        if (coverage.CompleteGraphics && support.SampleShading && !samplingKnown.HasFlag(SamplingStateKnowledge.MinimumSampleShading))
        {
            sampling.MinimumSampleShading = QueryBoundary(() => GL.GetFloat((GetPName)All.MinSampleShadingValue));
            samplingKnown |= SamplingStateKnowledge.MinimumSampleShading;
        }
        for (int index = 0; coverage.CompleteGraphics && index < support.MaxSampleMaskWords; index++)
        {
            int word = index;
            if (!sampleMasks.ContainsKey(word))
                sampleMasks[word] = unchecked((uint)QueryBoundaryIndexed((GetPName)All.SampleMaskValue, word));
        }

    }

    #endregion
}
