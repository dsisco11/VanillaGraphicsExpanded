using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Admits exact external capability fields without validating or rewriting native values.</summary>
internal static partial class EngineStateCalls
{
    #region Private
    /// <summary>Admits the represented indexed blend field or preserves unknown native capability behavior.</summary>
    private static void AdmitCapability(OpenGlStateCache cache, IndexedEnableCap capability, int index)
    {
        if (capability == IndexedEnableCap.Blend) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Enabled, index: index);
        else if ((int)capability == (int)EnableCap.ScissorTest && index == 0)
            cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ScissorEnabled);
        else cache.RejectUnsupportedBoundaryMutation();
    }

    /// <summary>Uses field-level restoration authority without suppressing the native operation.</summary>
    private static void AdmitCapability(OpenGlStateCache cache, EnableCap capability)
    {
        switch (capability)
        {
            case EnableCap.DepthTest: cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.TestEnabled); break;
            case EnableCap.Blend: cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Enabled); break;
            case EnableCap.CullFace: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullEnabled); break;
            case EnableCap.ScissorTest: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ScissorEnabled); break;
            case EnableCap.StencilTest: cache.ForgetExternalStateMutation(stencil: StencilStateKnowledge.TestEnabled); break;
            case EnableCap.Multisample: cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.Multisample); break;
            case EnableCap.SampleCoverage: cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleCoverageEnabled); break;
            case EnableCap.SampleMask: cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleMask); break;
            case EnableCap.SampleAlphaToCoverage: cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleAlphaToCoverage); break;
            case EnableCap.SampleAlphaToOne: cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleAlphaToOne); break;
            case EnableCap.SampleShading: cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleShading); break;
            case EnableCap.FramebufferSrgb: cache.ForgetExternalStateMutation(output: OutputStateKnowledge.FramebufferSrgb); break;
            case EnableCap.Dither: cache.ForgetExternalStateMutation(output: OutputStateKnowledge.Dither); break;
            case EnableCap.ColorLogicOp: cache.ForgetExternalStateMutation(output: OutputStateKnowledge.ColorLogicOp); break;
            case EnableCap.PrimitiveRestart: cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.PrimitiveRestart); break;
            case EnableCap.PrimitiveRestartFixedIndex: cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex); break;
            case EnableCap.DepthClamp: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.DepthClamp); break;
            case EnableCap.RasterizerDiscard: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.RasterizerDiscard); break;
            case EnableCap.PolygonOffsetFill: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonOffsetFill); break;
            case EnableCap.PolygonOffsetLine: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonOffsetLine); break;
            case EnableCap.PolygonOffsetPoint: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonOffsetPoint); break;
            case EnableCap.ProgramPointSize: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ProgramPointSize); break;
            case EnableCap.LineSmooth: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.LineSmooth); break;
            case EnableCap.PolygonSmooth: cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonSmooth); break;
            default:
                if ((int)capability >= (int)EnableCap.ClipDistance0 && (int)capability <= (int)EnableCap.ClipDistance7)
                    cache.WithdrawExternalClipDistance((int)capability - (int)EnableCap.ClipDistance0);
                else cache.RejectUnsupportedBoundaryMutation();
                break;
        }
    }
    #endregion
}
