using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates views of explicitly supplied external native names without inferring ownership transfer.</summary>
internal static class OpenGLTextureIntegration
{
    #region Internal API
    /// <summary>Explicitly acquires external backing facts without taking native buffer ownership.</summary>
    internal static RHICreationResult<GpuBufferTexture> CreateBufferView(int buffer, SizedInternalFormat format, bool whole, nint offset, nint length, string? label)
    {
        _ = RHICommandContext.Current;
        var request = new GpuBufferTextureInitialization { Buffer = buffer, Format = format, Mode = whole ? GpuBufferTextureMode.Whole : GpuBufferTextureMode.Range };
        if (!whole) { request.Offset = offset; request.Size = length; }
        var owner = new GpuBufferTexture();
        try
        {
            if (!GpuResourceManager.CreateBufferTexture(request, owner, out _).HasValue) return new(null, "External buffer view initialization did not complete.");
            owner.SetDebugName(label);
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }

    /// <summary>
    /// Creates a new texture view of an existing texture's immutable storage.
    /// </summary>
    /// <remarks>
    /// The source texture must have immutable storage (e.g. created with <c>glTexStorage*</c> or equivalent).
    /// </remarks>
    /// <param name="sourceTextureId">Source texture id that owns the storage.</param>
    /// <param name="viewTarget">Target for binding the view (e.g. <see cref="TextureTarget.Texture2D"/>).</param>
    /// <param name="viewInternalFormat">Internal format for the view (must be compatible with the source storage).</param>
    /// <param name="baseLevel">First mip level exposed by the view.</param>
    /// <param name="levelCount">Number of mip levels exposed by the view.</param>
    /// <param name="baseLayer">First layer exposed by the view.</param>
    /// <param name="layerCount">Number of layers exposed by the view.</param>
    /// <param name="debugName">Optional debug label (debug builds only).</param>
    internal static GpuTextureView CreateView(
        int sourceTextureId,
        TextureTarget viewTarget,
        PixelInternalFormat viewInternalFormat,
        int baseLevel,
        int levelCount,
        int baseLayer,
        int layerCount,
        string? debugName = null)
    {
        if (sourceTextureId == 0)
        {
            throw new ArgumentException("Source texture id must be non-zero.", nameof(sourceTextureId));
        }

        if (baseLevel < 0) throw new ArgumentOutOfRangeException(nameof(baseLevel));
        if (baseLayer < 0) throw new ArgumentOutOfRangeException(nameof(baseLayer));

        if (levelCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(levelCount), "LevelCount must be > 0.");
        }

        if (layerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layerCount), "LayerCount must be > 0.");
        }

        return InitializeView(sourceTextureId, viewTarget, viewInternalFormat, baseLevel, levelCount, baseLayer, layerCount, debugName);
    }
    /// <summary>
    /// Attempts to create a new texture view. Returns <c>false</c> on failure and provides an error string.
    /// </summary>
    internal static bool TryCreateView(
        int sourceTextureId,
        TextureTarget viewTarget,
        PixelInternalFormat viewInternalFormat,
        int baseLevel,
        int levelCount,
        int baseLayer,
        int layerCount,
        out GpuTextureView? view,
        out string error,
        string? debugName = null)
    {
        view = null;
        error = string.Empty;

        try
        {
            view = CreateView(
                sourceTextureId,
                viewTarget,
                viewInternalFormat,
                baseLevel,
                levelCount,
                baseLayer,
                layerCount,
                debugName);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            view = null;
            return false;
        }
    }
    #endregion
    #region Private
    /// <summary>Supplies explicit source selection to the manager without authoring a canonical snapshot.</summary>
    private static GpuTextureView InitializeView(int sourceId, TextureTarget target, PixelInternalFormat format,
        int baseLevel, int levelCount, int baseLayer, int layerCount, string? debugName, GpuTexture? source = null)
    {
        var request = new GpuTextureViewInitialization
        {
            Source = sourceId, Target = target, Format = format, BaseLevel = baseLevel,
            LevelCount = levelCount, BaseLayer = baseLayer, LayerCount = layerCount
        };
        var view = new GpuTextureView(sourceId);
        try
        {
            if (!GpuResourceManager.CreateTextureView(request, view, out _, source).HasValue)
                throw new InvalidOperationException("Texture view creation failed.");
            view.SetDebugName(debugName);
            return view;
        }
        catch { try { view.Dispose(); } catch { } throw; }
    }
    #endregion
}
