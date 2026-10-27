using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks shared immutable jitter provisioning independently of either rendering consumer.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class PmjJitterTextureUploadTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Typed readback matches generic sequence packing and ordinary frames retain owned storage.</summary>
    [Fact]
    public void UploadMatchesPackedSequenceAndRetainsIndependentStorage()
    {
        EnsureContextValid();const int count=256;const uint seed=0xA5B35705u;
        var config=new PmjConfig(count,seed,PmjVariant.Pmj02,PmjOutputKind.Vector2F32,true,0,false);
        ushort[] expected=PmjConversions.ToRg16UNormInterleaved(PmjGenerator.Generate(config));
        using var texture=PmjJitterTexture.Create(count,seed);
        Assert.Equal(TextureFormat.Rg16,texture.InternalFormat);
        Assert.Equal(count,texture.Width);Assert.Equal(1,texture.Height);
        Assert.Equal(TextureFilterMode.Nearest,texture.FilterMode);
        float[] actual=texture.ReadPixels();Assert.Equal(expected.Length,actual.Length);
        for(int i=0;i<expected.Length;i++)Assert.Equal(expected[i],(ushort)MathF.Round(actual[i]*65535));
        int id=texture.TextureId;texture.EnsureCreated();Assert.Equal(id,texture.TextureId);
        using var independent=PmjJitterTexture.Create(count,seed);
        Assert.NotEqual(id,independent.TextureId);texture.Dispose();Assert.False(texture.IsValid);
        Assert.Throws<ObjectDisposedException>(texture.EnsureCreated);
        Assert.True(independent.IsValid);Assert.Equal(actual,independent.ReadPixels());
    }
    #endregion
}
