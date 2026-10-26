namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines explicit optional buffer page commitment.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Commits or decommits the explicitly selected sparse-buffer pages.</summary>
    void CommitBufferPages(in RHIBufferRange range, bool commit);
    /// <summary>Commits a selected region of externally configured sparse image storage.</summary>
    void CommitTexturePages(GpuTextureRef texture, in RHITextureRegion region, bool commit);
}
