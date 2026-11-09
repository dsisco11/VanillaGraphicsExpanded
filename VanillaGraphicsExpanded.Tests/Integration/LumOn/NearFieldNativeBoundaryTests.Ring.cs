using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.WorldPartition;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Verifies compact 3D texture transfer and unpack-state restoration through OpenGL.</summary>
public sealed partial class NearFieldNativeBoundaryTests
{
    #region Public API
    /// <summary>Compact 3D uploads preserve non-four-byte row data and the caller's unpack alignment.</summary>
    [Fact]
    public void ReadinessUpload_UsesPackedBytesAndRestoresAlignment()
    {
        // Arrange: Prepare a tightly packed 3x3x3 payload and record the caller's alignment.
        // Act: Upload all 27 bytes to the native 3D texture.
        // Assert: Read back the exact payload and verify alignment is restored.
        EnsureShaderTestAvailable();
        using var scene = new ControlledTraceGpuScene(48);
        var data = Enumerable.Range(0, 27).Select(i => (byte)(i % 3)).ToArray();
        GL.GetInteger(GetPName.UnpackAlignment, out int originalAlignment);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
        try
        {
            scene.Regions.UploadDataImmediate(data, 0, 0, 0, 3, 3, 3);
            GL.GetInteger(GetPName.UnpackAlignment, out int alignment);
            Assert.Equal(8, alignment);
            ImageAssertions.Comparisons.CompareBits(scene.Regions,data.Select(value=>(uint)value).ToArray()).AssertEqual();
        }
        finally { GL.PixelStore(PixelStoreParameter.UnpackAlignment, originalAlignment); }
    }
    #endregion
}
