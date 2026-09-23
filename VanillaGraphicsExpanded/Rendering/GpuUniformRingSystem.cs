using System;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Thread-local access to the currently-active UBO ring allocator.
///
/// Set and cleared by render-thread frame controllers.
/// </summary>
internal static class GpuUniformRingSystem
{
    [ThreadStatic]
    private static GpuUniformRingBuffer? current;

    #region Active allocator
    /// <summary>Returns the allocator installed for the current render thread.</summary>
    public static bool TryGetCurrent(out GpuUniformRingBuffer ring)
    {
        ring = current!;
        return ring is not null;
    }

    /// <summary>Installs the frame owner's allocator on the current render thread.</summary>
    public static void SetCurrent(GpuUniformRingBuffer ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        current = ring;
    }

    /// <summary>Detaches the current allocator without disposing its owner's resources.</summary>
    public static void ClearCurrent()
    {
        current = null;
    }

    #endregion

    #region Uniform binding
    /// <summary>Writes uniform bytes into the active ring and binds the resulting range to the program.</summary>
    public static bool TryBind(
        GpuProgram program,
        string blockName,
        ReadOnlySpan<byte> std140Bytes,
        string debugName,
        bool clearDirty)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugName);

        if (!TryGetCurrent(out var ring))
        {
            return false;
        }

        _ = clearDirty;

        var alloc = ring.AllocateAndWrite(std140Bytes);
        return program.ProgramLayout.TryBindUniformBlockRange(
            program.ProgramId,
            blockName,
            alloc.Buffer,
            alloc.OffsetBytes,
            alloc.SizeBytes,
            warn: null);
    }
    #endregion
}
