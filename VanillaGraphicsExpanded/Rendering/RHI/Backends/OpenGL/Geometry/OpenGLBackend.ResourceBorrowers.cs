using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains the existing geometry and transfer borrower protocol independently of prepared shader resources.</summary>
internal sealed partial class OpenGLBackend
{
    // TODO: Replace temporary geometry/transfer protection with their owning operation's prepared relationships.
    private readonly List<GpuResource> executionResources = new();
    private readonly HashSet<GpuResource> executionResourceSet = new(ReferenceEqualityComparer.Instance);

    #region Private
    /// <summary>Releases temporary geometry/transfer borrowers after native cleanup finishes.</summary>
    private void ReleaseExecutionResources()
    {
        for (int index = executionResources.Count - 1; index >= 0; index--) executionResources[index].ReleasePassReference();
        executionResources.Clear(); executionResourceSet.Clear();
        ClearResourceBindings();
    }

    /// <summary>Preserves the geometry and transfer guard for their operation-specific use intervals.</summary>
    private void RetainExecutionResource(GpuResource? resource)
    {
        if (resource is null || executionResourceSet.Contains(resource)) return;
        resource.RetainPassReference(); executionResources.Add(resource); executionResourceSet.Add(resource);
        if (resource is GpuTextureView { Source: { } source }) RetainExecutionResource(source);
    }
    #endregion
}
