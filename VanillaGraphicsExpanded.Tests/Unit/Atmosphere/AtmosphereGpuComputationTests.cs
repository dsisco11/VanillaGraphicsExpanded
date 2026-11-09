using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Numerics;
using System.Collections.Immutable;
using System.Reflection;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Verifies atmosphere compute admission limits without creating a graphics context.</summary>
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereGpuComputationTests
{
    #region Public API
    /// <summary>Rejects each insufficient capability independently, including bounded output storage.</summary>
    [Fact]
    public void UnsupportedCapabilitiesRejectGpuAdmission()
    {
        // Arrange: evaluate the documented maximum output against one fully supported capability set.
        Assert.Equal((4 + 128 * 96 * (2 + 3 * AtmosphereAerialPerspective.Depth) + 512) * 16,
            AtmosphereGpuComputation.MaximumOutputBytes);
        // Act: remove each required capability or capacity independently.
        // Assert: every single missing requirement rejects admission.
        Assert.True(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(false, true, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, false, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, false, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 63, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 63, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 191, 3, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 192, 2, AtmosphereGpuComputation.MaximumOutputBytes));
        Assert.False(AtmosphereGpuComputation.Supports(true, true, true, 64, 64, 192, 3, AtmosphereGpuComputation.MaximumOutputBytes - 1));
    }
    #endregion
}
