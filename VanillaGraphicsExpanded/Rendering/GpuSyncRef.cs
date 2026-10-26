using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains a fence for waiting without granting lifecycle authority.</summary>
public readonly struct GpuSyncRef
{
    private readonly GpuFence? resource;
    #region Public API
    /// <summary>Reports absence without native discovery.</summary>
    public bool IsEmpty => resource is null;
    /// <summary>Reports already established managed usability.</summary>
    public bool IsValid => GpuResourceManager.IsAuxiliaryReferenceUsable(resource);
    #endregion
    #region Internal API
    /// <summary>Retains the existing owner without acquiring or querying it.</summary>
    internal GpuSyncRef(GpuFence? resource) => this.resource = resource;
    /// <summary>Resolves the retained wrapper only for manager/backend use.</summary>
    internal GpuFence RequireResource() => resource ?? throw new InvalidOperationException("A fence is required.");
    #endregion
}
