using System;
using System.Collections.Immutable;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Creates immutable views from available source descriptors and explicit selections.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Validates source-derived geometry before dispatch, then returns a complete view-relative snapshot.</summary>
    internal GpuTextureMetadata? CreateTextureView(in GpuTextureViewInitialization request, GpuTextureMetadata source, out nint id)
    {
        id = 0;
        var candidate = TextureViewDescriptor.Create(request, source);
        id = GenTexture();
        if (id == 0) return null;
        GL.TextureView((int)id, request.Target, request.Source, request.Format,
            request.BaseLevel, request.LevelCount, request.BaseLayer, request.LayerCount);
        return candidate;
    }
    #endregion
}
