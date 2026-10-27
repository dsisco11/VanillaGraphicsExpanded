namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Admits renderbuffer allocation without exposing private initialization records to consumers.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Routes owner-selected storage through the final private execution contract.</summary>
    internal void AllocateRenderbufferOwner(GpuRenderbuffer owner, in GpuRenderbufferInitialization request) => backend.AllocateRenderbufferOwner(owner, request);
    #endregion
}
