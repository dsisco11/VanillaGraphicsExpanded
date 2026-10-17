using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL texture view object created by <c>glTextureView</c>.
/// Texture views are separate texture objects that share immutable storage with a source texture.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
public sealed class GpuTextureView : GpuResource, IDisposable
{
    private int textureId;
    private readonly int sourceTextureId;
    private GpuTexture? source;
    private ulong sourceGeneration;
    private readonly TextureTarget target;
    private readonly PixelInternalFormat internalFormat;
    private readonly int baseLevel;
    private readonly int levelCount;
    private readonly int baseLayer;
    private readonly int layerCount;

    #region Public API
    /// <inheritdoc />
    public override nint ResourceId
    {
        get => textureId;
        protected set => textureId = (int)value;
    }



    /// <summary>
    /// Gets the underlying OpenGL texture id for this view.
    /// </summary>
    public int TextureId => textureId;

    /// <summary>Identifies the source immutable allocation shared by this view.</summary>
    public int SourceTextureId => sourceTextureId;

    /// <summary>Gets the managed source owner when the view was created from an owned texture.</summary>
    public GpuTexture? Source => source;

    /// <summary>
    /// Gets the view target used for binding.
    /// </summary>
    public TextureTarget Target => target;

    /// <summary>
    /// Gets the internal format used for interpreting the shared storage.
    /// </summary>
    public PixelInternalFormat InternalFormat => internalFormat;

    /// <summary>
    /// Gets the first mip level exposed by this view.
    /// </summary>
    public int BaseLevel => baseLevel;

    /// <summary>
    /// Gets the number of mip levels exposed by this view.
    /// </summary>
    public int LevelCount => levelCount;

    /// <summary>
    /// Gets the first array layer exposed by this view.
    /// </summary>
    public int BaseLayer => baseLayer;

    /// <summary>
    /// Gets the number of array layers exposed by this view.
    /// </summary>
    public int LayerCount => layerCount;

    /// <summary>
    /// Returns <c>true</c> while the view and its original managed source allocation remain live.
    /// </summary>
    public new bool IsValid => textureId != 0 && !IsDisposed &&
        (source is null || source.IsValid && source.TextureId == sourceTextureId && source.StorageGeneration == sourceGeneration);

    /// <summary>
    /// Sets the debug label for this texture view (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (textureId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
        }
#endif
    }

    /// <summary>Creates a view retaining the managed allocation identity for pass lifetime and alias validation.</summary>
    public static GpuTextureView CreateFromTexture(GpuTexture source, TextureTarget viewTarget,
        PixelInternalFormat viewInternalFormat, int baseLevel, int levelCount, int baseLayer, int layerCount,
        string? debugName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsValid) throw new ArgumentException("Source texture must be live.", nameof(source));
        if (baseLevel < 0 || levelCount <= 0 || baseLevel > source.StorageMipLevels - levelCount)
            throw new ArgumentOutOfRangeException(nameof(levelCount));
        var result = Create(source.TextureId, viewTarget, viewInternalFormat, baseLevel, levelCount, baseLayer, layerCount, debugName);
        result.source = source;
        result.sourceGeneration = source.StorageGeneration;
        return result;
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
    public static GpuTextureView Create(
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

        int id = GL.GenTexture();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenTextures failed.");
        }

        try
        {
            GL.TextureView(
                id,
                viewTarget,
                sourceTextureId,
                viewInternalFormat,
                baseLevel,
                levelCount,
                baseLayer,
                layerCount);

            // A failed texture-view operation leaves the newly generated name uninitialized.
            // Reject native validation failures before publishing a seemingly valid wrapper.
            if (!GL.IsTexture(id)) throw new InvalidOperationException("glTextureView failed to create a texture object.");
            // OpenGL can clamp requested ranges to available storage. Never retain a wider
            // authored range than the object actually exposes, which would weaken alias checks.
            GL.GetTextureParameter(id, GetTextureParameter.TextureViewMinLevel, out int actualBaseLevel);
            GL.GetTextureParameter(id, GetTextureParameter.TextureViewNumLevels, out int actualLevelCount);
            GL.GetTextureParameter(id, GetTextureParameter.TextureViewMinLayer, out int actualBaseLayer);
            GL.GetTextureParameter(id, GetTextureParameter.TextureViewNumLayers, out int actualLayerCount);
            if (actualBaseLevel != baseLevel || actualLevelCount != levelCount ||
                actualBaseLayer != baseLayer || actualLayerCount != layerCount)
                throw new InvalidOperationException("Texture view range differs from the requested immutable storage range.");
            var view = new GpuTextureView(id, sourceTextureId, viewTarget, viewInternalFormat, baseLevel, levelCount, baseLayer, layerCount);
            view.SetDebugName(debugName);
            return view;
        }
        catch
        {
            try { StateCache.Current.DeleteTexture(id); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Attempts to create a new texture view. Returns <c>false</c> on failure and provides an error string.
    /// </summary>
    public static bool TryCreate(
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
            view = Create(
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

    /// <summary>
    /// Binds this view to a texture unit.
    /// </summary>
    public void Bind(int unit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuTextureView] Attempted to bind disposed or invalid view");
            return;
        }

        StateCache.Current.BindTexture(target, unit, textureId);
    }
    #endregion

    #region Protected API
    /// <summary>Retires only this separate texture object, preserving its shared storage owner.</summary>
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
    #endregion

    #region Private
    /// <summary>Retains validated immutable view metadata for submission and attachment alias checks.</summary>
    private GpuTextureView(
        int textureId,
        int sourceTextureId,
        TextureTarget target,
        PixelInternalFormat internalFormat,
        int baseLevel,
        int levelCount,
        int baseLayer,
        int layerCount)
    {
        this.textureId = textureId;
        this.sourceTextureId = sourceTextureId;
        this.target = target;
        this.internalFormat = internalFormat;
        this.baseLevel = baseLevel;
        this.levelCount = levelCount;
        this.baseLayer = baseLayer;
        this.layerCount = layerCount;
    }

    #endregion
}
