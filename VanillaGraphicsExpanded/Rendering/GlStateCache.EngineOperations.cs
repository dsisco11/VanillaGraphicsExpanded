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
            case EnableCap.DepthTest: SetEnable(capability, enabled, ref depthTestEnabled); break;
            case EnableCap.CullFace: SetEnable(capability, enabled, ref cullFaceEnabled); break;
            case EnableCap.ScissorTest: SetEnable(capability, enabled, ref scissorTestEnabled); break;
            case EnableCap.Blend:
                SetEnable(capability, enabled, ref blendEnabled);
                // Global enable affects every draw buffer, not just index zero.
                DirtyIndexedBlendEnable();
                break;
            default:
                if (enabled) GL.Enable(capability); else GL.Disable(capability);
                break;
        }
    }

    /// <summary>Tracks indexed blending while leaving unsupported indexed capabilities to OpenGL.</summary>
    internal void SetIndexedCapability(IndexedEnableCap capability, int index, bool enabled)
    {
        if (capability == IndexedEnableCap.Blend) SetBlendEnabledIndexed(index, enabled);
        else if (enabled) GL.Enable(capability, index);
        else GL.Disable(capability, index);
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

    /// <summary>Updates the provoking convention observed by tessellated draw adapters.</summary>
    internal void SetProvokingVertex(ProvokingVertexMode mode)
    {
        GL.ProvokingVertex(mode);
        provokingVertex = mode;
    }
    #endregion

    #endregion
}
