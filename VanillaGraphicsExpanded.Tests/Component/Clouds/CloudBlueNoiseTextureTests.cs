using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;



namespace VanillaGraphicsExpanded.Tests.Component.Clouds;

/// <summary>Checks mask rank data, immutable upload and independent cloud-resource retirement.</summary>
[Collection("GPU")][Trait("Category", "GpuIntegration")]
public sealed class CloudBlueNoiseTextureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    /// <summary>Every temporal slice is a complete rank permutation in persistent owned storage.</summary>
    [Fact]
    public void MasksHaveCompleteRanksAndStableOwnedStorage()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        // Act: create the owned rank texture and upload all authored temporal slices.
        using var texture = CloudBlueNoiseTexture.Create();
        int id = texture.TextureId, side = CloudBlueNoiseTexture.Side, pixels = side * side;
        // Assert: validate format, complete ranks, stable reuse, disposal, and independent replacement ownership.
        Assert.Equal(TextureFormat.R32f, texture.InternalFormat);
        Assert.Equal(side, texture.Width); Assert.Equal(side * 8, texture.Height);
        var assertions = ImageAssertions;
        assertions.Statistics.CheckRanks(texture, side, 8).AssertRanks(8);
        texture.EnsureCreated(); Assert.Equal(id, texture.TextureId);
        texture.Dispose(); Assert.True(texture.IsDisposed); Assert.False(texture.IsValid);
        Assert.Throws<ObjectDisposedException>(texture.EnsureCreated);
        using var replacement = CloudBlueNoiseTexture.Create(); Assert.True(replacement.IsValid);
    }

    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("ranks", 0, 0);
    }
    #endregion
}
