using System;
using VanillaGraphicsExpanded.Rendering.Uniforms;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains immutable logical uniform bytes and their captured publication lifetime, never a GPU ring address.</summary>
public sealed class RHIUniformSnapshot
{
    private readonly byte[] bytes;
    private readonly CpuUniformBuffer source;

    #region Public API
    /// <summary>Gets the complete captured std140 payload without permitting edits.</summary>
    public ReadOnlySpan<byte> Bytes => bytes;
    /// <summary>Gets the content version captured with these bytes.</summary>
    public ulong ContentRevision { get; }
    /// <summary>Gets the logical storage policy independently of the execution epoch.</summary>
    public UniformBufferUsage Usage { get; }
    #endregion

    #region Internal API
    /// <summary>Retains the original logical publication; allocator selection occurs only at execution.</summary>
    internal UniformPublication Publication { get; }
    /// <summary>Copies a coherent CPU version without consulting native context, metadata or allocation state.</summary>
    internal RHIUniformSnapshot(CpuUniformBuffer source, UniformPublication publication, ReadOnlySpan<byte> bytes,
        ulong contentRevision, UniformBufferUsage usage)
    {
        this.source = source; Publication = publication; this.bytes = bytes.ToArray();
        ContentRevision = contentRevision; Usage = usage;
    }
    /// <summary>Rejects a retired captured publication even if the CPU owner has since begun another lifetime.</summary>
    internal void RequireUsable() => Publication.RequireUsable();
    /// <summary>Commits only this successful version, preserving any newer CPU edits and replacement publication.</summary>
    internal void CommitSource() => source.CommitCaptured(this);
    #endregion
}
