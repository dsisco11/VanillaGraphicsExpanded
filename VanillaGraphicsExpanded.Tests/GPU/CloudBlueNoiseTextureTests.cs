using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks spatial blue-noise quality, immutable upload and independent cloud-resource retirement.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudBlueNoiseTextureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Every temporal slice is a complete rank permutation with suppressed low spatial frequencies.</summary>
    [Fact]
    public void MasksHaveSpatialStructureAndStableOwnedStorage()
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
            // For independent uniform white noise each nonzero Fourier coefficient has expected
            // energy pixels/12. A small low-frequency band detects a mere shuffled/hash mask.
            double energy = 0; int modes = 0;
            for (int ky = -2; ky <= 2; ky++) for (int kx = -2; kx <= 2; kx++)
            {
                if (kx == 0 && ky == 0) continue;
                double real = 0, imaginary = 0;
                for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
                {
                    double angle = 2 * Math.PI * (kx * x + ky * y) / side;
                    double centered = slice[y * side + x] - .5;
                    real += centered * Math.Cos(angle); imaginary += centered * Math.Sin(angle);
                }
                energy += real * real + imaginary * imaginary; modes++;
            }
            Assert.InRange(energy / modes, 0, pixels / 12.0 * .25);
        }
        texture.EnsureCreated(); Assert.Equal(id, texture.TextureId);
        texture.Dispose(); Assert.False(GL.IsTexture(id)); Assert.Throws<ObjectDisposedException>(texture.EnsureCreated);
        using var replacement = CloudBlueNoiseTexture.Create(); Assert.True(replacement.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Reset and disposal release the primary noise separately from the retained secondary sequence.</summary>
    [Fact]
    public void ReconstructionResetRetiresPrimaryNoise()
    {
        EnsureContextValid();
        using var reconstruction = new CloudVolumeReconstruction(Moq.Mock.Of<Vintagestory.API.Client.ICoreClientAPI>());
        var field = typeof(CloudVolumeReconstruction).GetField("primaryNoise", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        using var texture = CloudBlueNoiseTexture.Create(); int id = texture.TextureId;
        field.SetValue(reconstruction, texture); reconstruction.Reset();
        Assert.Null(field.GetValue(reconstruction)); Assert.False(GL.IsTexture(id));
        using var replacement = CloudBlueNoiseTexture.Create(); int next = replacement.TextureId;
        field.SetValue(reconstruction, replacement); reconstruction.Dispose(); Assert.False(GL.IsTexture(next));
    }
    #endregion
}
