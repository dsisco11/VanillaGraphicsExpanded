namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Apply stage (main-thread and/or render-thread depending on implementation).
/// Must gate side effects on SessionId.
/// </summary>
internal interface IArtifactApplier<TKey, TDiskPayload, TGpuPayload>
{
    void Apply(in ArtifactApplyContext<TKey, TDiskPayload, TGpuPayload> context);
}
