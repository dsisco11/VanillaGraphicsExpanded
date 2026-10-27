using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains preparation selection and successful compatibility checks for one shader-owned input.</summary>
internal struct ShaderInputValidation
{
    private GpuPreparedBindings? table;
    private ulong identity;
    private GpuPreparedBindings.Entry selected;
    private GpuPreparedBindings.Entry validated;
    private InputKey key;
    private bool hasValidation;
#if DEBUG
    /// <summary>Counts successful compatibility checks, excluding lifetime checks and context cache comparisons.</summary>
    internal int CompatibilityChecks { get; private set; }
    /// <summary>Counts entry resolutions on installed executable changes rather than each use.</summary>
    internal int EntryResolutions { get; private set; }
#endif

    /// <summary>Captures the resource facts on which compatibility depends, independently of wrapper equality.</summary>
    internal readonly record struct InputKey(object Resource, nint Handle, int Target, int Format,
        int Width, int Height, int Depth, int Mips, RHIImageBinding View, nint Offset, nint Size, nint Capacity,
        (ulong Metadata, ulong Storage) Revisions);

    #region Public API
    /// <summary>Resolves the input once per prepared executable generation without global binding state.</summary>
    internal GpuPreparedBindings.Entry Resolve(IShaderSubmissionTarget owner, ulong inputIdentity)
    {
        var current = owner.ProgramLayout.BinaryInterface?.PreparedBindings ??
            throw new System.InvalidOperationException("Shader has no prepared executable interface.");
        if (!ReferenceEquals(table, current) || identity != inputIdentity)
        {
            table = current;
            identity = inputIdentity;
            selected = current.Resolve(inputIdentity);
            hasValidation = false;
#if DEBUG
            EntryResolutions++;
#endif
        }
        return selected;
    }

    /// <summary>Checks the last successful input facts without repeating format or view validation.</summary>
    internal readonly bool Matches(GpuPreparedBindings.Entry binding, InputKey input) =>
        hasValidation && validated == binding && key == input;

    /// <summary>Records compatibility only after validation succeeds, preserving failed-input retries.</summary>
    internal void Commit(GpuPreparedBindings.Entry binding, InputKey input)
    {
        validated = binding;
        key = input;
        hasValidation = true;
#if DEBUG
        CompatibilityChecks++;
#endif
    }

    /// <summary>Captures current published identity and image facts independently of reference equality.</summary>
    internal static InputKey TextureKey(GpuTextureRef texture, RHIImageBinding view = default)
    {
        var data = texture.RequireMetadata();
        var image = texture.RequireImage(view.Level);
        return new(texture.Resource!, texture.TextureId, (int)data.Target, (int)image.Format,
            image.Width, image.Height, image.Depth, data.Levels.Length, view,
            data.View?.BaseLevel ?? 0, data.View?.BaseLayer ?? 0, 0, RequireMetadata(texture.Resource!));
    }
    /// <summary>Snapshots the retained range and current owner-controlled capacity.</summary>
    internal static InputKey StorageKey(GpuStorageBufferBinding range) => new(range.Buffer!,
        range.Buffer!.BufferId, 0, 0, 0, 0, 0, 0, default, range.OffsetBytes, range.SizeBytes, range.Buffer.SizeBytes,
        RequireMetadata(range.Buffer));

    /// <summary>Rejects uncertified input facts and returns the current manager-owned compatibility revisions.</summary>
    internal static (ulong Metadata, ulong Storage) RequireMetadata(GpuResource resource)
    {
        // Context-local failures clear descriptors rather than retaining availability state.
        // A live native name alone cannot make those discarded snapshots usable again.
        if (resource is GpuTexture texture && texture.StorageImages.IsDefaultOrEmpty
            || resource is GpuBufferObject buffer && buffer.Usage == 0)
            throw new System.InvalidOperationException("Shader input resource metadata is unavailable.");
        var manager = resource.ResourceManager;
        if (manager?.GetResourceRegistration(resource) is not { } entry) return default;
        // Identity alone does not certify a snapshot. Failed refresh must reject reuse even
        // when the retained dimensions and range still match the previous successful check.
        if (!manager.IsResourceMetadataAvailable(resource))
            throw new System.InvalidOperationException("Shader input resource metadata is unavailable.");
        return (entry.MetadataRevision, entry.StorageRevision);
    }
    #endregion
}
