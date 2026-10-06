using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Adapts individual engine state operations to the cache without changing unrelated state.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region State changes
    /// <summary>Tracks supported capabilities and preserves native behavior for all other capabilities.</summary>
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
                SetCompleteEnable(capability, enabled); break;
            case EnableCap.AlphaTest: SetAlphaTest(enabled); break;
            case EnableCap.PointSmooth: SetPointSmooth(enabled); break;
            case EnableCap.LineSmooth: SetLineSmooth(enabled); break;
            case EnableCap.PolygonSmooth: SetPolygonSmooth(enabled); break;
            case EnableCap.LineStipple: SetLineStipple(enabled); break;
            case EnableCap.PolygonStipple: SetPolygonStipple(enabled); break;
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
                RejectUnsupportedBoundaryMutation();
                if (enabled) GL.Enable(capability); else GL.Disable(capability);
                break;
        }
    }

    /// <summary>Tracks indexed blending while leaving unsupported indexed capabilities to OpenGL.</summary>
    internal void SetIndexedCapability(IndexedEnableCap capability, int index, bool enabled)
    {
        if (capability == IndexedEnableCap.Blend) SetBlendEnabledIndexed(index, enabled);
        else
        {
            RejectUnsupportedBoundaryMutation();
            if (enabled) GL.Enable(capability, index); else GL.Disable(capability, index);
        }
    }

    /// <summary>Forwards one integer pixel-store change after withdrawing the affected layout knowledge.</summary>
    internal void SetPixelStore(PixelStoreParameter parameter, int value)
    {
        InvalidatePixelTransferLayout(parameter);
        GL.PixelStore(parameter, value);
    }

    /// <summary>Preserves native float-to-integer conversion without retaining stale layout knowledge.</summary>
    internal void SetPixelStore(PixelStoreParameter parameter, float value)
    {
        InvalidatePixelTransferLayout(parameter);
        GL.PixelStore(parameter, value);
    }

    #endregion

    #endregion
    #region Private
    /// <summary>Forgets only layout fields that the specified native pixel-store command can change.</summary>
    private void InvalidatePixelTransferLayout(PixelStoreParameter parameter)
    {
        // Pack owns the readback layout; unpack additionally owns image strides for layered uploads.
        if (parameter is PixelStoreParameter.PackAlignment or PixelStoreParameter.PackRowLength
            or PixelStoreParameter.PackSkipRows or PixelStoreParameter.PackSkipPixels
            or PixelStoreParameter.PackSwapBytes or PixelStoreParameter.PackLsbFirst) DirtyPixelPackState();
        else if (parameter is PixelStoreParameter.UnpackAlignment or PixelStoreParameter.UnpackRowLength
            or PixelStoreParameter.UnpackSkipRows or PixelStoreParameter.UnpackSkipPixels
            or PixelStoreParameter.UnpackSwapBytes or PixelStoreParameter.UnpackLsbFirst
            or PixelStoreParameter.UnpackImageHeight or PixelStoreParameter.UnpackSkipImages) DirtyPixelUnpackState();
    }

    /// <summary>Issues a native capability transition and records its diagnostic count.</summary>
    private void SetEnable(EnableCap cap, bool enabled)
    {
        if (enabled) GL.Enable(cap); else GL.Disable(cap);
        FixedFunctionCalls++;
    }
    #endregion
}
