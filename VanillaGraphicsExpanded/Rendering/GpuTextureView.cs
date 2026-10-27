using VanillaGraphicsExpanded.Rendering.RHI;
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
    /// <summary>Supplies the manager-published view descriptor for selected-image projection.</summary>
    internal GpuTextureMetadata MetadataSnapshot => metadata;

    private int textureId;
    private readonly int sourceTextureId;
    private GpuResource<GpuTextureMetadata>? source;
    private ulong sourceGeneration;
    // This is a dependency certificate, not a second authoritative descriptor or a lifetime tracker.
    private object? sourceStorageIdentity;
    /// <summary>Supplies the original storage revision to manager-owned dependency validation.</summary>
    internal ulong SourceGeneration => sourceGeneration;
    /// <summary>Supplies the local source certificate to manager-owned dependency validation.</summary>
    internal object? SourceStorageIdentity => sourceStorageIdentity;
    /// <summary>Gets the native view target from the canonical texture snapshot.</summary>
    private TextureDimension target => metadata.Target;
    /// <summary>Gets the native view internalFormat from the canonical texture snapshot.</summary>
    private TextureFormat internalFormat => metadata.Levels.IsDefaultOrEmpty ? 0 : metadata.Levels[0].Format;
    /// <summary>Gets the native view baseLevel from the canonical texture snapshot.</summary>
    private int baseLevel => metadata.View?.BaseLevel ?? 0;
    /// <summary>Gets the native view levelCount from the canonical texture snapshot.</summary>
    private int levelCount => metadata.View?.LevelCount ?? 0;
    /// <summary>Gets the native view baseLayer from the canonical texture snapshot.</summary>
    private int baseLayer => metadata.View?.BaseLayer ?? 0;
    /// <summary>Gets the native view layerCount from the canonical texture snapshot.</summary>
    private int layerCount => metadata.View?.LayerCount ?? 0;

    #region Public API
    /// <summary>Exposes this view to consumers without transferring lifecycle authority.</summary>
    public static implicit operator GpuTextureRef(GpuTextureView? texture) => new(texture);
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
    public GpuResource<GpuTextureMetadata>? Source => source;

    /// <summary>
    /// Gets the view target used for binding.
    /// </summary>
    public TextureDimension Target => target;

    /// <summary>
    /// Gets the internal format used for interpreting the shared storage.
    /// </summary>
    public TextureFormat InternalFormat => internalFormat;

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
    public new bool IsValid => GpuResourceManager.TryGetTextureReferenceMetadata(this, out _);

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


    #endregion

    #region Protected API
    /// <summary>Retires only this separate texture object, preserving its shared storage owner.</summary>
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
    #endregion

    /// <summary>Retains original source provenance established by the resource manager.</summary>
    internal void SetSource(GpuResource<GpuTextureMetadata> source, ulong generation, object identity)
    {
        this.source = source;
        sourceGeneration = generation;
        sourceStorageIdentity = identity;
    }

    #region Private
    /// <summary>Retains source identity before manager-owned creation installs the separate native view.</summary>
    internal GpuTextureView(int sourceTextureId) => this.sourceTextureId = sourceTextureId;


    #endregion
}
