using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks mask rank data, immutable upload and independent cloud-resource retirement.</summary>
[Collection("GPU")][Trait("Category", "GpuIntegration")]
public sealed class CloudBlueNoiseTextureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Every temporal slice is a complete rank permutation in persistent owned storage.</summary>
    [Fact]
    public void MasksHaveCompleteRanksAndStableOwnedStorage()
    {
        EnsureContextValid();
        using var texture = CloudBlueNoiseTexture.Create();
        int id = texture.TextureId, side = CloudBlueNoiseTexture.Side, pixels = side * side;
        Assert.Equal(TextureFormat.R32f, texture.InternalFormat);
        Assert.Equal(side, texture.Width); Assert.Equal(side * 8, texture.Height);
        using var assertions = new GpuImageAssertions();
        assertions.CheckRanks(texture, side, 8).AssertRanks(8);
        texture.EnsureCreated(); Assert.Equal(id, texture.TextureId);
        texture.Dispose(); Assert.True(texture.IsDisposed); Assert.False(texture.IsValid);
        Assert.Throws<ObjectDisposedException>(texture.EnsureCreated);
        using var replacement = CloudBlueNoiseTexture.Create(); Assert.True(replacement.IsValid);
    }

    #endregion
}
