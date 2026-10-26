using System;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Retains TransformFeedback consumption authority without lifecycle methods.</summary>
public readonly struct GpuTransformFeedbackRef
{
    private readonly GpuTransformFeedback? resource;
    #region Public API
    /// <summary>Reports absence independently of retirement.</summary>
    public bool IsEmpty => resource is null;
    /// <summary>Reports managed usability without native discovery.</summary>
    public bool IsValid => GpuResourceManager.IsAuxiliaryReferenceUsable(resource);
    #endregion
    #region Internal API
    /// <summary>Retains the supplied owner without registering it.</summary>
    internal GpuTransformFeedbackRef(GpuTransformFeedback? resource) => this.resource = resource;
    /// <summary>Resolves the existing owner only at manager/backend boundaries.</summary>
    internal GpuTransformFeedback RequireResource() => resource ?? throw new InvalidOperationException("A TransformFeedback resource is required.");
    #endregion
}
