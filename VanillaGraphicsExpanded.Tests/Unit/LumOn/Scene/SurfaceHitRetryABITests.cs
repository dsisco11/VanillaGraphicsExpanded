using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies the retained hit record layout consumed by shader readback.</summary>
public sealed class SurfaceHitRetryABITests
{
    #region Public API
    /// <summary>The retained GPU record preserves the exact bounded byte layout and field offsets.</summary>
    [Fact]
    public void GpuRecordLayoutMatchesReadbackContract()
    {
        // Arrange: identify the shader-visible record and fields.

        // Act and assert: size and offsets match the readback contract.
        Assert.Equal(4176, Marshal.SizeOf<SurfaceHitCapture>());
        // Assert
        Assert.Equal(64, Marshal.OffsetOf<SurfaceHitCapture>(nameof(SurfaceHitCapture.Complete)).ToInt32());
        Assert.Equal(80, Marshal.OffsetOf<SurfaceHitCapture>(nameof(SurfaceHitCapture.Queries)).ToInt32());
    }
    #endregion
}










