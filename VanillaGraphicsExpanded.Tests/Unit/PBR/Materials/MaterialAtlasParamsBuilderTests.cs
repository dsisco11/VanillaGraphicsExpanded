using System.Runtime.Intrinsics.X86;

using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasParamsBuilderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(24)]
    [InlineData(27)]
    public void FillRgbTripletsScalar_FillsExpectedTriplets(int length)
    {
        var buffer = new float[length];

        const float r = 0.25f;
        const float g = 0.5f;
        const float b = 0.75f;

        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(buffer, r, g, b);

        AssertRgbTriplets(buffer, r, g, b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(24)]
    [InlineData(27)]
    public void FillRgbTripletsVector128Sse_FillsExpectedTriplets(int length)
    {
        Assert.SkipWhen(!Sse.IsSupported, "SSE not supported on this platform");

        var buffer = new float[length];

        const float r = 0.125f;
        const float g = 0.625f;
        const float b = 0.875f;

        MaterialAtlasParamsBuilder.FillRgbTripletsVector128Sse(buffer, r, g, b);

        AssertRgbTriplets(buffer, r, g, b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(24)]
    [InlineData(27)]
    [InlineData(48)]
    [InlineData(51)]
    public void FillRgbTripletsVector256Avx_FillsExpectedTriplets(int length)
    {
        Assert.SkipWhen(!Avx.IsSupported, "AVX not supported on this platform");

        var buffer = new float[length];

        const float r = 0.1f;
        const float g = 0.2f;
        const float b = 0.3f;

        MaterialAtlasParamsBuilder.FillRgbTripletsVector256Avx(buffer, r, g, b);

        AssertRgbTriplets(buffer, r, g, b);
    }

    private static void AssertRgbTriplets(ReadOnlySpan<float> buffer, float r, float g, float b)
    {
        Assert.Equal(0, buffer.Length % 3);

        for (int i = 0; i < buffer.Length; i += 3)
        {
            Assert.Equal(r, buffer[i + 0]);
            Assert.Equal(g, buffer[i + 1]);
            Assert.Equal(b, buffer[i + 2]);
        }
    }
}
