using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies the fixed managed record layout shared with the surface fallback shader.</summary>
public sealed class SurfaceFallbackWorkerTests
{
    #region Public API
    /// <summary>GPU requests and commits preserve their explicit storage strides.</summary>
    [Fact]
    public void StorageLayoutsMatchShaderRecords()
    {
        // Arrange: the record types define the host side of the shader storage contract.

        // Act and assert: required byte strides remain stable.
        Assert.Equal(64, Marshal.SizeOf<SurfaceFallbackRequest>());
        // Assert
        Assert.Equal(32, Marshal.SizeOf<SurfaceFallbackCommit>());
    }
    #endregion
}







