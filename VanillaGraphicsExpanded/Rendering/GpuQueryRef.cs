using System;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains a query for execution and results without disposal authority.</summary>
public readonly struct GpuQueryRef
{
    private readonly GpuQuery? resource;
    #region Public API
    /// <summary>Reports absence independently of retirement.</summary>
    public bool IsEmpty => resource is null;
    /// <summary>Reports current managed usability without native queries.</summary>
    public bool IsValid => GpuResourceManager.IsAuxiliaryReferenceUsable(resource);
    #endregion
    #region Internal API
    /// <summary>Retains an existing owner without registering it.</summary>
    internal GpuQueryRef(GpuQuery? resource) => this.resource = resource;
    /// <summary>Resolves the retained owner only inside the backend.</summary>
    internal GpuQuery RequireResource() => resource ?? throw new InvalidOperationException("A query is required.");
    #endregion
}
