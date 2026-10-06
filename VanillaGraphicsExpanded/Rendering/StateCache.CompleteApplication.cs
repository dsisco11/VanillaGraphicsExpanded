using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Applies complete graphics intent through existing category owners and explicit dynamic values.</summary>
internal sealed partial class StateCache
{
    private static readonly ColorBlendDesc NeutralBlend = new();
    #region Public API
    /// <summary>Validates the entire request before changing state, then establishes every supported drawing parameter.</summary>
    internal void ApplyGraphicsState(GraphicsPipelineDesc pipeline, GraphicsDynamicState dynamics)
    {
        ArgumentNullException.ThrowIfNull(pipeline); ArgumentNullException.ThrowIfNull(dynamics);
        var caps = GpuSupport.Graphics;
        PipelineDescriptionValidation.Validate(pipeline, caps);
        ValidateGraphicsDynamics(pipeline, dynamics);
        EnsureViewportLimits();
        if (activeBoundary is not null || resolvingBoundary)
            ValidateBoundaryMutation(Pipeline.PipelineStateCoverage.From(pipeline));
        // Existing owners remain authoritative for previously tracked parameters.
        var depthDesc = pipeline.DepthStencil;
        SetCapability(EnableCap.DepthTest, depthDesc.DepthTest);
        SetDepthFunc(depthDesc.DepthComparison); SetDepthWriteMask(depthDesc.DepthWrite);
        SetDepthRange(depthDesc.DepthRangeNear, depthDesc.DepthRangeFar);
        SetGraphicsEnable(EnableCap.StencilTest, depthDesc.StencilTest);
        var refs = pipeline.Dynamics.HasFlag(DynamicPipelineState.StencilReference) ? dynamics.StencilReference!.Value : (Front: 0, Back: 0);
        ApplyStencilFace(StencilFace.Front, depthDesc.Front, refs.Front);
        ApplyStencilFace(StencilFace.Back, depthDesc.Back, refs.Back);
        var raster = pipeline.Rasterizer;
        SetCapability(EnableCap.CullFace, raster.Cull); SetCullMode(raster.CullMode); SetFrontFace(raster.FrontFace);
        SetPolygonModes(raster.PolygonMode, raster.PolygonMode); SetPolygonOffset(raster.DepthBiasFactor, raster.DepthBiasUnits);
        SetGraphicsEnable(EnableCap.PolygonOffsetFill, raster.OffsetFill);
        SetGraphicsEnable(EnableCap.PolygonOffsetLine, raster.OffsetLine);
        SetGraphicsEnable(EnableCap.PolygonOffsetPoint, raster.OffsetPoint);
        SetGraphicsEnable(EnableCap.DepthClamp, raster.DepthClamp);
        SetGraphicsEnable(EnableCap.RasterizerDiscard, raster.Discard);
        SetGraphicsEnable(EnableCap.ProgramPointSize, raster.ProgramPointSize);
        SetCapability(EnableCap.ScissorTest, raster.Scissor);
        SetLineWidth(raster.LineWidth); SetPointSize(raster.PointSize); SetProvokingVertex(raster.ProvokingVertex);
        ApplyConfigurableRaster(pipeline);
        ApplyCompleteBlending(pipeline);
        var sampling = pipeline.Sampling;
        SetGraphicsEnable(EnableCap.Multisample, sampling.Multisample);
        SetGraphicsEnable(EnableCap.SampleCoverage, sampling.CoverageEnabled);
        SetSampleCoverage(sampling.Coverage, sampling.CoverageInvert);
        SetGraphicsEnable(EnableCap.SampleMask, sampling.MaskEnabled);
        for (int i = 0; i < caps.MaxSampleMaskWords; i++) SetSampleMask(i, sampling.GetMaskWord(i));
        SetGraphicsEnable(EnableCap.SampleAlphaToCoverage, sampling.AlphaToCoverage);
        SetGraphicsEnable(EnableCap.SampleAlphaToOne, sampling.AlphaToOne);
        SetGraphicsEnable(EnableCap.SampleShading, sampling.SampleShading);
        if (caps.SampleShading) SetMinimumSampleShading(sampling.MinimumSampleShading);
        SetGraphicsEnable(EnableCap.PrimitiveRestart, pipeline.Assembly.Restart);
        SetGraphicsEnable(EnableCap.PrimitiveRestartFixedIndex, pipeline.Assembly.FixedIndexRestart);
        SetRestartIndex(pipeline.Assembly.RestartIndex);
        if (caps.Tessellation) SetPatchVertices(pipeline.Assembly.PatchVertices);
        SetGraphicsEnable(EnableCap.FramebufferSrgb, pipeline.Output.FramebufferSrgb);
        SetGraphicsEnable(EnableCap.Dither, pipeline.Output.Dither);
        SetGraphicsEnable(EnableCap.ColorLogicOp, false); SetLogicOperation(LogicOp.Copy);
        ApplyDynamic(dynamics.Viewport!.Value);
        var scissor = pipeline.Dynamics.HasFlag(DynamicPipelineState.Scissor) ? dynamics.Scissor!.Value : (X: 0, Y: 0, Width: 0, Height: 0);
        SetScissor(scissor.X, scissor.Y, scissor.Width, scissor.Height);
        var constant = pipeline.Dynamics.HasFlag(DynamicPipelineState.BlendConstant) ? dynamics.BlendConstant!.Value : (R: 0f, G: 0f, B: 0f, A: 0f);
        SetBlendConstant(constant.R, constant.G, constant.B, constant.A);
    }
    #endregion
    #region Private
    /// <summary>Rejects missing or invalid dynamic values before any pipeline mutation.</summary>
    private static void ValidateGraphicsDynamics(GraphicsPipelineDesc pipeline, GraphicsDynamicState value)
    {
        if (value.Viewport is not { } viewport || viewport.Width < 0 || viewport.Height < 0)
            throw new ArgumentException("A valid viewport is required.", nameof(value));
        if (pipeline.Dynamics.HasFlag(DynamicPipelineState.Scissor)
            && (value.Scissor is not { } scissor || scissor.Width < 0 || scissor.Height < 0))
            throw new ArgumentException("A valid scissor rectangle is required.", nameof(value));
        if (pipeline.Dynamics.HasFlag(DynamicPipelineState.StencilReference)
            && (value.StencilReference is not { } references || references.Front < 0 || references.Front > 255 || references.Back < 0 || references.Back > 255))
            throw new ArgumentException("Stencil references are required.", nameof(value));
        if (pipeline.Dynamics.HasFlag(DynamicPipelineState.BlendConstant)
            && (value.BlendConstant is not { } blend || !float.IsFinite(blend.R) || !float.IsFinite(blend.G) || !float.IsFinite(blend.B) || !float.IsFinite(blend.A)))
            throw new ArgumentException("A finite blend constant is required.", nameof(value));
    }
    /// <summary>Applies all face parameters including disabled stencil values.</summary>
    private void ApplyStencilFace(StencilFace face, StencilFaceDesc value, int reference)
    {
        SetStencilFunction(face, value.Comparison, reference, value.ReadMask);
        SetStencilWriteMask(face, value.WriteMask);
        SetStencilOperation(face, value.Fail, value.DepthFail, value.Pass);
    }
    /// <summary>Establishes declared outputs and neutral state in every unused native output slot.</summary>
    private void ApplyCompleteBlending(GraphicsPipelineDesc pipeline)
    {
        int count = MaxDrawBuffers;
        if (!GpuSupport.Graphics.IndependentBlend)
        {
            var common = pipeline.Blending.Count == 0 ? NeutralBlend : pipeline.Blending[0];
            SetBlendFunc(new(common.SourceRgb, common.DestinationRgb, common.SourceAlpha, common.DestinationAlpha));
            SetBlendEquation(common.RgbEquation, common.AlphaEquation);
        }
        for (int i = 0; i < count; i++)
        {
            var value = i < pipeline.Blending.Count ? pipeline.Blending[i] : NeutralBlend;
            SetBlendEnabledIndexed(i, value.Enabled);
            SetColorMaskIndexed(i, GlColorMask.FromRgba(value.WriteRed, value.WriteGreen, value.WriteBlue, value.WriteAlpha));
            if (GpuSupport.Graphics.IndependentBlend)
            {
                SetBlendFuncIndexed(i, new(value.SourceRgb, value.DestinationRgb, value.SourceAlpha, value.DestinationAlpha));
                SetBlendEquationIndexed(i, value.RgbEquation, value.AlphaEquation);
            }
        }
    }
    #endregion
}
