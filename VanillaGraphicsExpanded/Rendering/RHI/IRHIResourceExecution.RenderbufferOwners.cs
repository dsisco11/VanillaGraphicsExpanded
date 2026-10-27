namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines private storage allocation for existing renderbuffer owner policy.</summary>
internal partial interface IRHIResourceExecution
{
    #region Renderbuffer ownership
    /// <summary>Initializes an unpublished owner and publishes its complete storage facts once.</summary>
    void AllocateRenderbufferOwner(GpuRenderbuffer owner, in GpuRenderbufferInitialization request);
    #endregion
}
