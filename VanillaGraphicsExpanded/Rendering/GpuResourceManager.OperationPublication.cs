namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Applies existing resource-specific delivery and dependency policies at explicit operation boundaries.</summary>
internal sealed partial class GpuResourceManager
{
    #region Private
    /// <summary>Delivers a local result and advances texture storage identity only for storage changes.</summary>
    private static void DeliverUntrackedOperationResult<T>(GpuResource<T> resource, T candidate, GpuMetadataChange effects) where T : struct
    {
        if ((effects & GpuMetadataChange.Storage) != 0 && resource is GpuTexture texture)
            texture.UntrackedStorageIdentity = new();
        resource.HandleMetadataChanged(candidate);
    }

    /// <summary>Reconciles retained framebuffer helpers after canonical snapshot publication.</summary>
    private static void NotifyOperationDependents<T>(GpuResourceMetadataEntry<T> entry, GpuResource<T> origin) where T : struct
        => entry.NotifyFramebufferDependents(origin);

    /// <summary>Withdraws dependent usability without replacing an operation's original exception.</summary>
    private static void WithdrawOperationDependents<T>(GpuResourceMetadataEntry<T> entry, GpuResource<T> origin,
        GpuMetadataChange effects) where T : struct
    {
        entry.NotifyImageDependents(suppressErrors: true);
        if (entry.Kind == GpuResourceKind.Texture) entry.Manager.SetTextureViewSourceAvailability(entry, false);
        try { entry.NotifyProgramDependents(effects, available: false); } catch { }
        try { entry.NotifyFramebufferDependents(origin); } catch { }
    }
    #endregion
}
