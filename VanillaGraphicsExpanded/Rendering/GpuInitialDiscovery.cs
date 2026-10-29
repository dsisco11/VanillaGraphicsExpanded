namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Records why an original registration may perform its first explicit metadata discovery.</summary>
internal enum GpuInitialDiscovery
{
    /// <summary>No initial discovery is authorized, or its single attempt has been consumed.</summary>
    None,
    /// <summary>An integration owner proved that the object existed before observation began.</summary>
    Preexisting,
    /// <summary>An EXT realization boundary encountered an object with an unknown prior history.</summary>
    ImplicitCreation
}
