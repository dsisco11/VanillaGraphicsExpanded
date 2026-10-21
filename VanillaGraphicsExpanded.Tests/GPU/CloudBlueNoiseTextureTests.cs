using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks mask rank data, immutable upload and independent cloud-resource retirement.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
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
        Assert.Equal(PixelInternalFormat.R32f, texture.InternalFormat);
        Assert.Equal(side, texture.Width); Assert.Equal(side * 8, texture.Height);
        float[] values = new float[pixels * 8];
        GL.GetInteger(GetPName.TextureBinding2D, out int previous); GL.BindTexture(TextureTarget.Texture2D, id);
        GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Red, PixelType.Float, values);
        GL.BindTexture(TextureTarget.Texture2D, previous);
        for (int frame = 0; frame < 8; frame++)
        {
            float[] slice = values.AsSpan(frame * pixels, pixels).ToArray();
            Assert.Equal(Enumerable.Range(0, pixels).Select(rank => (rank + .5f) / pixels), slice.Order());
            if (frame > 0) Assert.NotEqual(values.AsSpan(0, pixels).ToArray(), slice);

        }
        texture.EnsureCreated(); Assert.Equal(id, texture.TextureId);
        texture.Dispose(); Assert.False(GL.IsTexture(id)); Assert.Throws<ObjectDisposedException>(texture.EnsureCreated);
        using var replacement = CloudBlueNoiseTexture.Create(); Assert.True(replacement.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
}
