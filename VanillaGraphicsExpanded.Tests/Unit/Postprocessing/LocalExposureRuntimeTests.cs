using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks local exposure rejects invalid ownership candidates before storage publication and disposes idempotently.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class LocalExposureRuntimeTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Disabled and malformed candidates publish no storage, and repeated disposal remains allocation-free.</summary>
    [Fact]
    public void DisabledAndInvalidCandidatesCannotPublishStorage()
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var scene = Scene();
        using var owner = new LocalExposureRenderer();
        // Act: submit invalid policies through the renderer; xUnit invokes each delegate.
        Assert.Throws<InvalidOperationException>(() => owner.Render(Programs.Api, scene, Candidate() with { Enabled = false }));
        Assert.Equal(0, owner.Generation);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Render(Programs.Api, scene, Candidate() with { RangeWidth = float.NaN }));
        // Assert
        Assert.Equal(0, owner.Generation);
        Assert.Throws<InvalidOperationException>(() => owner.BaseTexture);

        // Act: dispose once, then repeat disposal while measuring for unexpected allocations.
        owner.Dispose();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 16; i++) owner.Dispose();
        // Assert
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    #endregion

    #region Private
/// <summary>Creates one valid radiance texel for the admission checks.</summary>
    private DynamicTexture2D Scene() => TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [.01f, .01f, .01f, 1f]);

    /// <summary>Declares a valid local exposure policy before applying one invalid or disabled override.</summary>
    private static LocalExposureParameters Candidate() => new(true, 2, 2, 1, .5f, .5f, 4, 4, false);
    #endregion
}
