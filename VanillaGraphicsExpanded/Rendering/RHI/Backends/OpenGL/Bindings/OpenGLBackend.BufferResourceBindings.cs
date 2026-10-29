using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Uniforms;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes captured GPU-buffer and current-epoch logical uniform publication.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Publishes or clears a previously validated buffer selection without implicit extent conventions.</summary>
    private void PublishBufferInput(ShaderBindingKind kind, int slot, RHIBufferBinding input)
    {
        var target = kind switch
        {
            ShaderBindingKind.UniformBlock => Legacy.BufferRangeTarget.UniformBuffer,
            ShaderBindingKind.StorageBlock => Legacy.BufferRangeTarget.ShaderStorageBuffer,
            ShaderBindingKind.AtomicCounter => Legacy.BufferRangeTarget.AtomicCounterBuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        int name = input.Buffer.IsEmpty ? 0 : checked((int)input.Buffer.RequireResource().ResourceId);
        if (input.Buffer.IsEmpty || input.Whole) OpenGLBufferBindings.BindBufferBase(cache, target, slot, name);
        else OpenGLBufferBindings.BindBufferRange(cache, target, slot, name, input.Offset, input.Length);
    }
    /// <summary>Selects current-epoch physical storage and commits only the captured version after native binding succeeds.</summary>
    private void PublishUniformSnapshot(int slot, RHIUniformSnapshot? snapshot, bool reuseCommittedUniforms)
    {
        if (snapshot is null)
        {
            OpenGLBufferBindings.BindBufferBase(cache, Legacy.BufferRangeTarget.UniformBuffer, slot, 0);
            return;
        }
        snapshot.RequireUsable();
        if (!RHIUniformRingSystem.TryGetCurrent(out var ring)) throw new InvalidOperationException("No open uniform allocation epoch.");
        var owner = snapshot.Publication;
        UniformPublication.Candidate candidate;
        if (!reuseCommittedUniforms || !owner.TryGetCommitted(ring, snapshot.ContentRevision, out candidate))
            candidate = owner.Prepare(ring, snapshot.Bytes, snapshot.ContentRevision);
        try
        {
            RetainExecutionResource(candidate.Buffer);
            OpenGLBufferBindings.BindBufferRange(cache, Legacy.BufferRangeTarget.UniformBuffer, slot,
                candidate.Buffer.BufferId, candidate.Offset, candidate.Size);
            owner.Commit(ring, candidate, snapshot.ContentRevision);
            snapshot.CommitSource();
        }
        catch { owner.Abort(candidate); throw; }
    }
    #endregion
}
