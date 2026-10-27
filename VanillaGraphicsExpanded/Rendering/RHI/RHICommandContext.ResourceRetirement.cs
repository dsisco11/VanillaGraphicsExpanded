namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Routes owner-authorized retirement through context execution after original queue admission.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Executes retirement without rediscovering an owner or invoking a public factory.</summary>
    internal void RetireResource(GpuResourceHandle resource) => backend.RetireResource(resource);
    #endregion
}
