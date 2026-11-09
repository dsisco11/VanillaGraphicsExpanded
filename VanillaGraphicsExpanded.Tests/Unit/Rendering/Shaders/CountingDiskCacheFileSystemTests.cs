using VanillaGraphicsExpanded.Tests.Support.Cache;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks concurrent lease admission and release in the in-memory cache filesystem.</summary>
public sealed class CountingDiskCacheFileSystemTests
{
    #region Public API
    /// <summary>Concurrent requests yield one held lease, then permit a new owner after cleanup.</summary>
    [Fact]
    public void ConcurrentLeaseRequestsHaveOneOwner()
    {
        // Arrange one shared lease path and collect successful owners.
        string path = Path.Combine(directory, "index.lock");
        var acquired = new System.Collections.Concurrent.ConcurrentBag<Stream>();

        // Act with competing requests while each successful lease remains held.
        try
        {
            // Keep a winning lease alive across the round so later calls contend with an owner.
            Parallel.For(0, 16, _ =>
            {
                try { acquired.Add(storage.AcquireExclusive(path)); }
                catch (IOException) { }
            });

            // Assert exclusion admits exactly one concurrent owner.
            Assert.Single(acquired);
        }
        finally
        {
            // Release every acquired lease even if the exclusivity assertion fails.
            foreach (var lease in acquired) lease.Dispose();
        }

        // Act and assert the path can be acquired again after all leases have been released.
        using var next = storage.AcquireExclusive(path);
    }
    #endregion

    #region Private
    private readonly CountingDiskCacheFileSystem storage = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "VGE.ProgramBinaryTests", Guid.NewGuid().ToString("N"));
    #endregion
}
