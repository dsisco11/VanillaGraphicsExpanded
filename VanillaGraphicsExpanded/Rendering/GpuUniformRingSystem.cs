using System;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Thread-local access to the currently-active UBO ring allocator.
///
/// Production: set/cleared by render-thread frame controllers.
/// Tests: can be set manually via <see cref="BeginTestFrame"/>.
/// </summary>
internal static class GpuUniformRingSystem
{
    [ThreadStatic]
    private static GpuUniformRingBuffer? current;

    private static GpuUniformRingBuffer? testRing;
    private static int testFrameIndex;

    public static bool TryGetCurrent(out GpuUniformRingBuffer ring)
    {
        ring = current!;
        return ring is not null;
    }

    public static void SetCurrent(GpuUniformRingBuffer ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        current = ring;
    }

    public static void ClearCurrent()
    {
        current = null;
    }

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

    #region Test lifetime
    /// <summary>
    /// Ensures a ring is active on the current thread for GPU tests.
    /// </summary>
    internal static void BeginTestFrame()
    {
        // Single-page ring is sufficient for tests; no fences needed.
        testRing ??= new GpuUniformRingBuffer(
            pageSizeBytes: 2 * 1024 * 1024,
            pageCount: 1,
            preferPersistent: true,
            coherent: true,
            debugName: "Test.UboRing");

        testRing.BeginFrame(testFrameIndex++);
        SetCurrent(testRing);
    }

    internal static void EndTestFrame()
    {
        // Don't fence in tests by default; just clear the thread-local.
        ClearCurrent();
    }

    /// <summary>Releases test-owned mapped storage while its GL context is still current, before that context is destroyed.</summary>
    internal static void DisposeTestResources()
    {
        ClearCurrent();
        testRing?.Dispose();
        testRing = null;
        testFrameIndex = 0;
    }
    #endregion
}
