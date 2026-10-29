using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the uniform ring used by serial headless render tests within the current fixture context.</summary>
internal static class TestUniformRing
{
    private static GpuUniformRingBuffer? ring;
    private static (nint Handle, long Generation) context;
    private static RHIResourceManager? manager;

    #region Context lifetime
    /// <summary>Installs the allocator for setup helpers without invalidating allocations for not-yet-submitted draws.</summary>
    internal static void EnsureFrame()
    {
        DiscardRetiredContext();
        if (ring is null) BeginFrame();
        else RHIUniformRingSystem.SetCurrent(ring);
    }

    /// <summary>Starts a controlled test frame and installs its allocator through the production render-thread API.</summary>
    internal static void BeginFrame()
    {
        DiscardRetiredContext();
        // Explicit frame boundaries close the previous interval; nested setup uses EnsureFrame.
        // Retire all commands submitted so far before resetting the single page's write offset.
        // Clearing the allocator first prevents reuse if completion fails or times out.
        RHIUniformRingSystem.ClearCurrent();
        if (ring is not null) RetireSubmittedWork();
        ring ??= new GpuUniformRingBuffer(
            pageSizeBytes: 1 << 21,
            pageCount: 1,
            preferPersistent: true,
            coherent: true,
            debugName: "Test.UboRing");
        context = RenderContextRegistry.Current();
        manager = RHIResourceManagerSystem.CurrentManager;
        ring.BeginFrame(0);
        RHIUniformRingSystem.SetCurrent(ring);
    }

    /// <summary>Releases mapped storage before the owning headless GL context is destroyed.</summary>
    internal static void Dispose()
    {
        DiscardRetiredContext();
        RHIUniformRingSystem.ClearCurrent();
        if (ring is not null) RetireSubmittedWork();
        ring?.Dispose();
        ring = null;
        context = default;
        manager = null;
    }

    /// <summary>Prevents retired context or service lifetimes from donating mapped pages to later tests.</summary>
    private static void DiscardRetiredContext()
    {
        if (ring is null) return;
        bool sameContext = context == RenderContextRegistry.Current();
        if (sameContext && ReferenceEquals(manager, RHIResourceManagerSystem.CurrentManager)
            && manager?.IsMetadataRegistryRetired != true) return;
        if (!sameContext && RenderContextRegistry.IsAlive(context))
            throw new InvalidOperationException("The test uniform allocator belongs to a different live context.");
        // An old service's registrations cannot certify this allocator's buffers even when
        // tests reuse the same physical context. Release through their original owners.
        RHIUniformRingSystem.ClearCurrent();
        ring.Dispose();
        ring = null;
        context = default;
        manager = null;
    }

    /// <summary>Flushes and bounds completion of previous consumers before page reuse or mapped-storage disposal.</summary>
    private static void RetireSubmittedWork()
    {
        // Tests have irregular boundaries and helper re-entry, so insert the fence here, after
        // every possible previous consumer. The production ring's EndFrame polling policy is
        // deliberately not used: this fixture owns a single page and needs a bounded wait.
        using var completion = RHICommandContext.Current.InsertFence();
        var status = RHICommandContext.Current.WaitCpu(completion, TimeSpan.FromSeconds(10));
        if (status == CompletionStatus.Pending)
            throw new TimeoutException("Test uniform-buffer consumers did not retire within ten seconds; the page was not reused.");
        if (status is not (CompletionStatus.Ready))
            throw new InvalidOperationException($"Test uniform-buffer retirement failed ({status}); the page was not reused.");
    }
    #endregion
}
