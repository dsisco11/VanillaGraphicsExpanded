using OpenTK.Graphics.OpenGL;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains buffer selector knowledge independently of backend execution.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Checks a generic selector without changing native state.</summary>
    internal bool NeedsBufferBinding(BufferTarget target, int buffer) => !TryGetCachedBoundBuffer(target, out int known) || known != buffer;

    /// <summary>Checks the complete indexed key, including its generic-selector side effect.</summary>
    internal bool NeedsIndexedBufferBinding(BufferRangeTarget target, int slot, int buffer, nint offset, nint size, bool range)
    {
#if DEBUG
        IndexedBufferCacheChecks++;
#endif
        return !indexedBufferBindings.TryGetValue((target, slot), out var known)
            || known != new IndexedBufferBinding(buffer, offset, size, range)
            || !bufferBindingByTarget.TryGetValue((BufferTarget)target, out int? generic) || generic != buffer;
    }

    /// <summary>Establishes only facts proved by a completed controlled generic bind.</summary>
    internal void EstablishBufferBinding(BufferTarget target, int buffer) => bufferBindingByTarget[target] = buffer;

    /// <summary>Commits an indexed key and its generic association after native execution.</summary>
    internal void EstablishIndexedBufferBinding(BufferRangeTarget target, int slot, int buffer, nint offset, nint size, bool range)
    {
        indexedBufferBindings[(target, slot)] = new(buffer, offset, size, range);
        bufferBindingByTarget[(BufferTarget)target] = buffer;
#if DEBUG
        ResourceSlotBindCount++;
#endif
    }

    /// <summary>Withdraws only uncertain generic/indexed selector knowledge after interruption.</summary>
    internal void ForgetBufferBinding(BufferTarget target, int? slot = null)
    {
        bufferBindingByTarget.Remove(target);
        if (slot.HasValue) indexedBufferBindings.Remove(((BufferRangeTarget)target, slot.Value));
    }

    /// <summary>Retires a proven original buffer name and repairs only its affected selectors.</summary>
    internal void ReconcileBufferDeletion(int id)
    {
        if (id == 0) return;
        CancelResourceOperation(GpuResourceKind.Buffer, id);
        GpuResourceManagerSystem.CurrentManager?.ObserveDeletion(GpuResourceKind.Buffer, id);
        ForgetDeletedBuffer(id);
    }
    #endregion
}
