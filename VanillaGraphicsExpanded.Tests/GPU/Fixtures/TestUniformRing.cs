using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the uniform ring used by serial headless render tests within the current fixture context.</summary>
internal static class TestUniformRing
{
    private static GpuUniformRingBuffer? ring;

    #region Context lifetime
    /// <summary>Installs the allocator for setup helpers without invalidating allocations for not-yet-submitted draws.</summary>
    internal static void EnsureFrame()
    {
        if (ring is null) BeginFrame();
        else GpuUniformRingSystem.SetCurrent(ring);
    }

    /// <summary>Starts a controlled test frame and installs its allocator through the production render-thread API.</summary>
    internal static void BeginFrame()
    {
        // Explicit frame boundaries close the previous interval; nested setup uses EnsureFrame.
        // Retire all commands submitted so far before resetting the single page's write offset.
        // Clearing the allocator first prevents reuse if completion fails or times out.
        GpuUniformRingSystem.ClearCurrent();
        if (ring is not null) RetireSubmittedWork();
        ring ??= new GpuUniformRingBuffer(
            pageSizeBytes: 1 << 21,
            pageCount: 1,
            preferPersistent: true,
            coherent: true,
            debugName: "Test.UboRing");
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
    }

    /// <summary>Releases mapped storage before the owning headless GL context is destroyed.</summary>
    internal static void Dispose()
    {
        GpuUniformRingSystem.ClearCurrent();
        if (ring is not null) RetireSubmittedWork();
        ring?.Dispose();
        ring = null;
    }

    /// <summary>Flushes and bounds completion of previous consumers before page reuse or mapped-storage disposal.</summary>
    private static void RetireSubmittedWork()
    {
        // Tests have irregular boundaries and helper re-entry, so insert the fence here, after
        // every possible previous consumer. The production ring's EndFrame polling policy is
        // deliberately not used: this fixture owns a single page and needs a bounded wait.
        using var completion = GpuFence.Insert();
        var status = completion.Wait(TimeSpan.FromSeconds(10));
        if (status == OpenTK.Graphics.OpenGL.WaitSyncStatus.TimeoutExpired)
            throw new TimeoutException("Test uniform-buffer consumers did not retire within ten seconds; the page was not reused.");
        if (status is not (OpenTK.Graphics.OpenGL.WaitSyncStatus.AlreadySignaled or OpenTK.Graphics.OpenGL.WaitSyncStatus.ConditionSatisfied))
            throw new InvalidOperationException($"Test uniform-buffer retirement failed ({status}); the page was not reused.");
    }
    #endregion
}
