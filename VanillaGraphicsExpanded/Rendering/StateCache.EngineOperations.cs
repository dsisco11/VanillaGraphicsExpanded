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
        switch (capability)
        {
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

    /// <summary>Forwards a single pixel-store change and invalidates the aggregate pack snapshot.</summary>
    internal void SetPixelStore(PixelStoreParameter parameter, int value)
    {
        // Do not query or replay other pack fields: this call changes exactly one native field.
        GL.PixelStore(parameter, value);
        if (parameter is PixelStoreParameter.PackAlignment or PixelStoreParameter.PackRowLength
            or PixelStoreParameter.PackSkipRows or PixelStoreParameter.PackSkipPixels
            or PixelStoreParameter.PackSwapBytes) DirtyPixelPackState();
    }

    /// <summary>Forwards the floating-point overload without changing OpenGL's integer conversion rules.</summary>
    internal void SetPixelStore(PixelStoreParameter parameter, float value)
    {
        GL.PixelStore(parameter, value);
        DirtyPixelPackState();
    }

    #endregion

    #endregion
    #region Private
    /// <summary>Issues a native capability transition and records its diagnostic count.</summary>
    private void SetEnable(EnableCap cap, bool enabled)
    {
        if (enabled) GL.Enable(cap); else GL.Disable(cap);
        FixedFunctionCalls++;
    }
    #endregion
}
