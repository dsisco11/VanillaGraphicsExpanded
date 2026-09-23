using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the uniform ring used by serial headless render tests within the current fixture context.</summary>
internal static class TestUniformRing
{
    private static GpuUniformRingBuffer? ring;
    private static int frameIndex;

    #region Context lifetime
    /// <summary>Starts a controlled test frame and installs its allocator through the production render-thread API.</summary>
    internal static void BeginFrame()
    {
        // Readbacks synchronize test observations; a single page suffices for this fixture.
        ring ??= new GpuUniformRingBuffer(
            pageSizeBytes: 2 * 1024 * 1024,
            pageCount: 1,
            preferPersistent: true,
            coherent: true,
            debugName: "Test.UboRing");
        ring.BeginFrame(frameIndex++);
        GpuUniformRingSystem.SetCurrent(ring);
    }

    /// <summary>Releases mapped storage before the owning headless GL context is destroyed.</summary>
    internal static void Dispose()
    {
        GpuUniformRingSystem.ClearCurrent();
        ring?.Dispose();
        ring = null;
        frameIndex = 0;
    }
    #endregion
}
