namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Provides owned readback requests without raw client pointers or exposed staging owners.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Copies source data into request-owned storage and inserts its explicit completion marker.</summary>
    public RHIReadbackTicket EnqueueReadback(in RHIReadbackDescription description) => backend.EnqueueReadback(description);
    #endregion
}
