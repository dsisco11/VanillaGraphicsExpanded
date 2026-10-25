using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL texture view object created by <c>glTextureView</c>.
/// Texture views are separate texture objects that share immutable storage with a source texture.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
public sealed class GpuTextureView : GpuResource<GpuTextureMetadata>, IDisposable
{
    private int textureId;
    private readonly int sourceTextureId;
    private GpuTexture? source;
    private ulong sourceGeneration;
    // This is a dependency certificate, not a second authoritative descriptor or a lifetime tracker.
    private object? sourceStorageIdentity;
    /// <summary>Gets the native view target from the canonical texture snapshot.</summary>
    private TextureTarget target => metadata.Target;
    /// <summary>Gets the native view internalFormat from the canonical texture snapshot.</summary>
    private PixelInternalFormat internalFormat => metadata.Levels.IsDefaultOrEmpty ? 0 : metadata.Levels[0].Format;
    /// <summary>Gets the native view baseLevel from the canonical texture snapshot.</summary>
    private int baseLevel => metadata.View?.BaseLevel ?? 0;
    /// <summary>Gets the native view levelCount from the canonical texture snapshot.</summary>
    private int levelCount => metadata.View?.LevelCount ?? 0;
    /// <summary>Gets the native view baseLayer from the canonical texture snapshot.</summary>
    private int baseLayer => metadata.View?.BaseLayer ?? 0;
    /// <summary>Gets the native view layerCount from the canonical texture snapshot.</summary>
    private int layerCount => metadata.View?.LayerCount ?? 0;

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
    /// Returns <c>true</c> while the view and its original managed source allocation remain live with available metadata.
    /// </summary>
    public new bool IsValid => base.IsValid && !metadata.Levels.IsDefaultOrEmpty &&
        (ResourceManager?.GetResourceRegistration(this) is null || ResourceManager.IsResourceMetadataAvailable(this)) &&
        (source is null || source.IsValid && source.TextureId == sourceTextureId && source.StorageGeneration == sourceGeneration
            && !source.StorageImages.IsDefaultOrEmpty
            && (ResourceManager?.IsTextureViewSourceCurrent(this, source)
                ?? ReferenceEquals(source.UntrackedStorageIdentity, sourceStorageIdentity)));

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
        var result = CreateView(source.TextureId, viewTarget, viewInternalFormat, baseLevel, levelCount, baseLayer, layerCount, debugName, source);
        result.source = source;
        result.sourceGeneration = source.StorageGeneration;
        result.sourceStorageIdentity = source.UntrackedStorageIdentity;
        result.ResourceManager?.RecordTextureViewSource(result, source);
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

        return CreateView(sourceTextureId, viewTarget, viewInternalFormat, baseLevel, levelCount, baseLayer, layerCount, debugName);
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
    /// <summary>Retains source identity before manager-owned creation installs the separate native view.</summary>
    private GpuTextureView(int sourceTextureId) => this.sourceTextureId = sourceTextureId;

    /// <summary>Supplies explicit source selection to the manager without authoring a canonical snapshot.</summary>
    private static GpuTextureView CreateView(int sourceId, TextureTarget target, PixelInternalFormat format,
        int baseLevel, int levelCount, int baseLayer, int layerCount, string? debugName, GpuTexture? source = null)
    {
        var request = new GpuTextureViewInitialization
        {
            Source = sourceId, Target = target, Format = format, BaseLevel = baseLevel,
            LevelCount = levelCount, BaseLayer = baseLayer, LayerCount = layerCount
        };
        var view = new GpuTextureView(sourceId);
        if (!GpuResourceManager.CreateTextureView(request, view, out _, source).HasValue)
            throw new InvalidOperationException("Texture view creation failed.");
        view.SetDebugName(debugName);
        return view;
    }
    #endregion
}
