using System;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Retains Semaphore consumption authority without lifecycle methods.</summary>
public readonly struct GpuSemaphoreRef
{
    private readonly GpuSemaphore? resource;
    #region Public API
    /// <summary>Reports absence independently of retirement.</summary>
    public bool IsEmpty => resource is null;
    /// <summary>Reports managed usability without native discovery.</summary>
    public bool IsValid => GpuResourceManager.IsAuxiliaryReferenceUsable(resource);
    #endregion
    #region Internal API
    /// <summary>Retains the supplied owner without registering it.</summary>
    internal GpuSemaphoreRef(GpuSemaphore? resource) => this.resource = resource;
    /// <summary>Resolves the existing owner only at manager/backend boundaries.</summary>
    internal GpuSemaphore RequireResource() => resource ?? throw new InvalidOperationException("A Semaphore resource is required.");
    #endregion
}
