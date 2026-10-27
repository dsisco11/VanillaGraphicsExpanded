using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes injected uniform blocks for engine executables through the same logical version executor as owned shaders.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Binds an integration-validated slot without synthesizing an owned shader or reserving storage during capture.</summary>
    internal bool BindEngineUniform(int slot, RHIUniformSnapshot snapshot)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)slot >= (uint)Capabilities.MaxUniformBufferBindings)
            throw new ArgumentOutOfRangeException(nameof(slot));
        snapshot.RequireUsable();
        if (snapshot.Bytes.Length > Capabilities.MaxUniformBlockSize)
            throw new ArgumentException("Uniform snapshot exceeds the cached block-size limit.", nameof(snapshot));
        if (executionResources.Count != 0) throw new InvalidOperationException("Resource execution cannot recurse.");
        if (!GpuUniformRingSystem.TryGetCurrent(out _)) return false;
        // Engine membership owns the numeric slot. Allocation, version commitment, abort
        // and captured CPU revision handling remain singular inside the backend publisher.
        try
        {
            PublishUniformSnapshot(slot, snapshot, reuseCommittedUniforms: false);
            snapshot.Publication.MarkExecution(snapshot.ContentRevision);
            return true;
        }
        finally { ReleaseExecutionResources(); }
    }
    #endregion
}
