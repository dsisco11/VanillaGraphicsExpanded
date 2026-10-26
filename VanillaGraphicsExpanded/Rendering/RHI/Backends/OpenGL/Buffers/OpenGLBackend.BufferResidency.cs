using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes optional named sparse residency with cached page alignment and current identity.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void CommitBufferPages(in RHIBufferRange range, bool commit)
    {
        RequireContext();
        if (!Capabilities.SparseBuffers) throw new NotSupportedException("Sparse buffers are unavailable.");
        var owner = RequireRange(range.Buffer, range.Offset, range.Length);
        var metadata = owner.PublishedMetadata;
        int pageSize = Capabilities.SparseBufferPageSize;
        if (!metadata.ImmutableStorage || (metadata.StorageFlags & (OpenTK.Graphics.OpenGL.BufferStorageFlags)ArbSparseBuffer.SparseStorageBitArb) == 0
            || pageSize <= 0 || range.Offset % pageSize != 0 || range.Length <= 0
            || range.Length % pageSize != 0 && range.Offset + range.Length != metadata.Capacity)
            throw new ArgumentException("The commitment must select aligned pages of sparse immutable storage.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, owner.ResourceId);
        GL.Arb.NamedBufferPageCommitment((int)owner.ResourceId, range.Offset, range.Length, commit);
        RequireContext();
    }
    #endregion
}
