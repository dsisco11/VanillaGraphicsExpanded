namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Exposes explicit optional residency independently of backend tokens.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Commits or decommits sparse pages, rejecting unsupported requests before dispatch.</summary>
    public void CommitBufferPages(in RHIBufferRange range, bool commit) => backend.CommitBufferPages(range, commit);
    /// <summary>Commits a region of existing sparse image storage under the optional native residency contract.</summary>
    public void CommitTexturePages(GpuTextureRef texture, in RHITextureRegion region, bool commit) => backend.CommitTexturePages(texture, region, commit);
    #endregion
}
