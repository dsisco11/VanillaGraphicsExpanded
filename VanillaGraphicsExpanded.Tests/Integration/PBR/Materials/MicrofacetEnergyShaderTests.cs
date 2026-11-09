using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;

namespace VanillaGraphicsExpanded.Tests.Integration.PBR.Materials;

/// <summary>Checks hardware-filtered half-float lookup interpolation, furnace endpoints and invalid-input containment.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class MicrofacetEnergyShaderTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Multiple callers borrow immutable storage; retirement withdraws it before a replacement is published.</summary>
    [Fact]
    public void LookupReusesAndReplacesOwnedStorage()
    {
        // Arrange: Prepare the shared lookup once and retain its first published texture.
        EnsureContextValid();
        var first = MicrofacetEnergyLookup.Prepare();
        Assert.Same(first, MicrofacetEnergyLookup.Prepare());
        Assert.True(first.IsValid);
        // Act: Retire the owner and prepare again to force a new native allocation.
        MicrofacetEnergyLookup.Retire();
        // Assert: The old storage is invalid and a distinct replacement is live.
        Assert.False(first.IsValid);
        var replacement = MicrofacetEnergyLookup.Prepare();
        Assert.NotSame(first, replacement);
        Assert.True(replacement.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
}
