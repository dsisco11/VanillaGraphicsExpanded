using System;
using VanillaGraphicsExpanded.Rendering.Uniforms;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reuses executed binding publication while preserving uniform allocation and consumption lifetimes.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Checks every physical uniform version before extending its use without repeating slot publication.</summary>
    private bool ReuseUniformPublication(OpenGLBindingPlan plan, bool reuseCommittedUniforms)
    {
        // Logical snapshot equality cannot establish current ring storage or a single-draw reuse right.
        foreach (var input in plan.Uniforms)
        {
            if (input.Uniform is not { } snapshot) continue;
            if (!reuseCommittedUniforms && snapshot.Usage == UniformBufferUsage.SingleDraw) return false;
            if (!RHIUniformRingSystem.TryGetCurrent(out var ring)
                || !snapshot.Publication.TryGetCommitted(ring, snapshot.ContentRevision, out var candidate)
                || input.CommittedUniform is not { } committed || candidate != committed) return false;
            RetainExecutionResource(candidate.Buffer);
        }
        // Validation above is complete. Extend the allocator's last-use ownership and commit only captured CPU edits.
        foreach (var input in plan.Uniforms)
        {
            if (input.Uniform is not { } snapshot) continue;
            if (!RHIUniformRingSystem.TryGetCurrent(out var ring))
                throw new InvalidOperationException("No open uniform allocation epoch.");
            snapshot.Publication.Commit(ring, input.CommittedUniform!.Value, snapshot.ContentRevision);
            snapshot.CommitSource();
        }
        return true;
    }
    #endregion
}
