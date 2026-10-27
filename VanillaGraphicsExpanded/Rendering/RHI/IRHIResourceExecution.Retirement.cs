namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines original-identity retirement execution after manager queue admission.</summary>
internal partial interface IRHIResourceExecution
{
    /// <summary>Deletes only the admitted original native object and reconciles its established outcome.</summary>
    void RetireResource(GpuResourceHandle resource);
}
