using System;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Retains MemoryObject consumption authority without lifecycle methods.</summary>
public readonly struct GpuMemoryObjectRef
{
    private readonly GpuMemoryObject? resource;
    #region Public API
    /// <summary>Reports absence independently of retirement.</summary>
    public bool IsEmpty => resource is null;
    /// <summary>Reports managed usability without native discovery.</summary>
    public bool IsValid => RHIResourceManager.IsAuxiliaryReferenceUsable(resource);
    #endregion
    #region Internal API
    /// <summary>Retains the supplied owner without registering it.</summary>
    internal GpuMemoryObjectRef(GpuMemoryObject? resource) => this.resource = resource;
    /// <summary>Resolves the existing owner only at manager/backend boundaries.</summary>
    internal GpuMemoryObject RequireResource() => resource ?? throw new InvalidOperationException("A MemoryObject resource is required.");
    #endregion
}
