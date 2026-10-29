using System;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Thread-local access to the currently-active UBO ring allocator.
///
/// Set and cleared by render-thread frame controllers.
/// </summary>
internal static class RHIUniformRingSystem
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
    #endregion
}
