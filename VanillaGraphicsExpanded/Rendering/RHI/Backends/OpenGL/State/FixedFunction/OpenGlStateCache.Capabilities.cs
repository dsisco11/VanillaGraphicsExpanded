using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Establishes supported managed capability fields through independent cached knowledge.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    #region State changes
    /// <summary>Establishes a represented managed capability; raw engine enum behavior belongs to exact adapters.</summary>
    internal void SetCapability(EnableCap capability, bool enabled)
    {
        int clipIndex = (int)capability - (int)EnableCap.ClipDistance0;
        if (clipIndex >= 0 && clipIndex < 32 && clipIndex < GpuSupport.Graphics.MaxClipDistances)
        {
            SetClipDistance(clipIndex, enabled);
            return;
        }
        switch (capability)
        {
            case EnableCap.StencilTest:
            case EnableCap.DepthClamp:
            case EnableCap.RasterizerDiscard:
            case EnableCap.PolygonOffsetFill:
            case EnableCap.PolygonOffsetLine:
            case EnableCap.PolygonOffsetPoint:
            case EnableCap.ProgramPointSize:
            case EnableCap.Multisample:
            case EnableCap.SampleCoverage:
            case EnableCap.SampleMask:
            case EnableCap.SampleAlphaToCoverage:
            case EnableCap.SampleAlphaToOne:
            case EnableCap.SampleShading:
            case EnableCap.FramebufferSrgb:
            case EnableCap.Dither:
            case EnableCap.ColorLogicOp:
            case EnableCap.PrimitiveRestart:
            case EnableCap.PrimitiveRestartFixedIndex:
                SetGraphicsEnable(capability, enabled); break;
            case EnableCap.LineSmooth: SetLineSmooth(enabled); break;
            case EnableCap.PolygonSmooth: SetPolygonSmooth(enabled); break;
            case EnableCap.DepthTest:
                ValidateBoundaryMutation(depth: DepthStateKnowledge.TestEnabled);
                if (depthKnown.HasFlag(DepthStateKnowledge.TestEnabled) && depth.TestEnabled == enabled) return;
                // Withdraw only this field before native work; failures leave it unknown.
                depthKnown &= ~DepthStateKnowledge.TestEnabled;
                SetEnable(capability, enabled);
                depth.TestEnabled = enabled;
                depthKnown |= DepthStateKnowledge.TestEnabled;
                break;
            case EnableCap.CullFace:
                ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.CullEnabled);
                if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.CullEnabled) && rasterizer.CullEnabled == enabled) return;
                // Withdraw only this field before native work; failures leave it unknown.
                rasterizerKnown &= ~RasterizerStateKnowledge.CullEnabled;
                SetEnable(capability, enabled);
                rasterizer.CullEnabled = enabled;
                rasterizerKnown |= RasterizerStateKnowledge.CullEnabled;
                break;
            case EnableCap.ScissorTest:
                ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ScissorEnabled);
                if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.ScissorEnabled) && rasterizer.ScissorEnabled == enabled) return;
                // Withdraw only this field before native work; failures leave it unknown.
                rasterizerKnown &= ~RasterizerStateKnowledge.ScissorEnabled;
                SetEnable(capability, enabled);
                rasterizer.ScissorEnabled = enabled;
                rasterizerKnown |= RasterizerStateKnowledge.ScissorEnabled;
                break;
            case EnableCap.Blend: SetBlendEnabled(enabled); break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(capability), "Unsupported managed capability.");
        }
    }





    #endregion

    #endregion
    #region Private


    /// <summary>Issues a native capability transition and records its diagnostic count.</summary>
    private void SetEnable(EnableCap cap, bool enabled)
    {
        if (enabled) OpenGLStateEffects.Enable(cap); else OpenGLStateEffects.Disable(cap);
#if DEBUG
        FixedFunctionCalls++;
#endif
    }
    #endregion
}
