using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Noise;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnPmjJitterTextureUploadTests : RenderTestBase
{
    public LumOnPmjJitterTextureUploadTests(HeadlessGLFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public void EnsureCreated_AllocatesRg16Texture_OfExpectedSize()
    {
        EnsureContextValid();

        const int cycleLength = 256;
        const uint seed = 123u;

        using var tex = LumOnPmjJitterTexture.Create(cycleLength, seed);
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        AssertTexture2DLevelFormatAndSize(
            stage: "PMJ jitter texture",
            textureId: tex.TextureId,
            mipLevel: 0,
            expectedInternalFormat: PixelInternalFormat.Rg16,
            expectedWidth: cycleLength,
            expectedHeight: 1);

        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    [Fact]
    public void EnsureCreated_UploadMatchesCpuPackedSequence()
    {
        EnsureContextValid();

        const int cycleLength = 256;
        const uint seed = 0xA5B35705u;

        var config = new PmjConfig(
            SampleCount: cycleLength,
            Seed: seed,
            Variant: PmjVariant.Pmj02,
            OutputKind: PmjOutputKind.Vector2F32,
            OwenScramble: true,
            Salt: 0u,
            Centered: false);

        PmjSequence seq = PmjGenerator.Generate(config);
        ushort[] expected = PmjConversions.ToRg16UNormInterleaved(seq);

        using var tex = LumOnPmjJitterTexture.Create(cycleLength, seed);
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        ushort[] actual = ReadBackRg16UnormInterleaved(tex.TextureId, cycleLength);

        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.AsSpan().SequenceEqual(actual));

        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    private static ushort[] ReadBackRg16UnormInterleaved(int textureId, int width)
    {
        Assert.True(textureId != 0, "textureId is 0");
        Assert.True(width > 0, "width must be > 0");

        ushort[] data = new ushort[checked(width * 2)];

        GL.GetInteger(GetPName.TextureBinding2D, out int prevBinding);
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

        GL.GetTexImage(
            target: TextureTarget.Texture2D,
            level: 0,
            format: PixelFormat.Rg,
            type: PixelType.UnsignedShort,
            pixels: data);

        GL.BindTexture(TextureTarget.Texture2D, prevBinding);
        return data;
    }

    private static void AssertTexture2DLevelFormatAndSize(
        string stage,
        int textureId,
        int mipLevel,
        PixelInternalFormat expectedInternalFormat,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.True(textureId != 0, $"{stage}: textureId is 0");

        GL.GetInteger(GetPName.TextureBinding2D, out int prevBinding);
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureInternalFormat, out int internalFormat);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(TextureTarget.Texture2D, mipLevel, GetTextureParameter.TextureHeight, out int height);

        GL.BindTexture(TextureTarget.Texture2D, prevBinding);

        Assert.Equal((int)expectedInternalFormat, internalFormat);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }
}
