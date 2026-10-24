using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Creates texture associations and publishes command-proven view metadata through the manager.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Attempts a named whole-buffer association; publication errors do not trigger fallback.</summary>
    internal bool TryTextureBuffer(GpuBufferView view, int buffer, SizedInternalFormat format)
    {
        try { GL.TextureBuffer(view.TextureId, format, buffer); }
        catch { return false; }
        ObserveBufferView(view, buffer, format, 0, 0);
        return true;
    }

    /// <summary>Associates the bound texture with a whole buffer and publishes the accepted facts.</summary>
    internal void TexBuffer(GpuBufferView view, int buffer, SizedInternalFormat format)
    {
        GL.TexBuffer(TextureBufferTarget.TextureBuffer, format, buffer);
        ObserveBufferView(view, buffer, format, 0, 0);
    }

    /// <summary>Attempts a named range association; publication errors do not trigger fallback.</summary>
    internal bool TryTextureBufferRange(GpuBufferView view, int buffer, SizedInternalFormat format, nint offset, nint size)
    {
        try { GL.TextureBufferRange(view.TextureId, format, buffer, offset, size); }
        catch { return false; }
        ObserveBufferView(view, buffer, format, offset, size);
        return true;
    }

    /// <summary>Associates a range through the bound entry point, preserving its native integer size contract.</summary>
    internal void TexBufferRange(GpuBufferView view, int buffer, SizedInternalFormat format, nint offset, int size)
    {
        GL.TexBufferRange(TextureBufferTarget.TextureBuffer, format, buffer, offset, size);
        ObserveBufferView(view, buffer, format, offset, size);
    }

    /// <summary>Creates an immutable view and publishes the actual validated level/layer selection.</summary>
    internal void ConfigureTextureView(GpuTextureView view, int source, TextureTarget target, PixelInternalFormat format,
        int baseLevel, int levelCount, int baseLayer, int layerCount)
    {
        GL.TextureView(view.TextureId, target, source, format, baseLevel, levelCount, baseLayer, layerCount);
        if (!new GpuTextureMetadataReader(target).TryRead(view.TextureId, out var data))
            throw new InvalidOperationException("Texture view metadata is unavailable.");
        if (data.View != new GpuTextureViewMetadata(baseLevel, levelCount, baseLayer, layerCount))
            throw new InvalidOperationException("The native texture view range differs from the requested range.");
        GpuResourceManager.PublishObservedMetadata(view, data, GpuMetadataChange.Storage);
    }
    #endregion

    #region Private
    /// <summary>Publishes the accepted association and native-width range after validating a newly realized view.</summary>
    private void ObserveBufferView(GpuBufferView view, int buffer, SizedInternalFormat format, nint offset, nint size)
    {
        // A newly created texture starts with no buffer association. A rejected command
        // therefore cannot pass this check or certify requested range values as native facts.
        using var binding = !GpuSupport.Graphics.SupportsArbDirectStateAccess
            ? BindTextureScope(TextureTarget.TextureBuffer, 0, view.TextureId) : (TextureScope?)null;
        int actualBuffer;
        int actualFormat;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)0x8C2D, out actualBuffer);
            GL.GetTextureLevelParameter(view.TextureId, 0, GetTextureParameter.TextureInternalFormat, out actualFormat);
        }
        else
        {
            GL.GetTexLevelParameter(TextureTarget.TextureBuffer, 0, (GetTextureParameter)0x8C2D, out actualBuffer);
            GL.GetTexLevelParameter(TextureTarget.TextureBuffer, 0, GetTextureParameter.TextureInternalFormat, out actualFormat);
        }
        if (actualBuffer != buffer || actualFormat != (int)format)
            throw new InvalidOperationException("The native buffer texture association was rejected.");
        var data = new GpuTextureMetadata(TextureTarget.TextureBuffer, ImmutableArray<GpuTextureLevelMetadata>.Empty,
            false, 0, 0, 0, false, new(actualBuffer, (SizedInternalFormat)actualFormat, offset, size, size == 0));
        GpuResourceManager.PublishObservedMetadata(view, data, GpuMetadataChange.Storage);
    }

    #endregion
}
