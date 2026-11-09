using System.Runtime.Intrinsics.X86;

using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks scalar and SIMD RGB triplet fillers produce the same packed channel layout.</summary>
[Trait("Category", "Unit")]
public sealed class MaterialAtlasParamsBuilderTests
{
    #region Public API
    /// <summary>The scalar filler writes each RGB triplet across empty and partial vector lengths.</summary>
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
        // Arrange: allocate a buffer whose size is a multiple of one RGB pixel.
        var buffer = new float[length];

        const float r = 0.25f;
        const float g = 0.5f;
        const float b = 0.75f;

        // Act: fill the buffer through the scalar path.
        MaterialAtlasParamsBuilder.FillRgbTripletsScalar(buffer, r, g, b);

        // Assert: every pixel stores the expected channel order.
        AssertRgbTriplets(buffer, r, g, b);
    }

    /// <summary>The SSE filler writes expected triplets for supported hardware.</summary>
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
        // Arrange: skip only when this CPU cannot execute the requested instruction set.
        Assert.SkipWhen(!Sse.IsSupported, "SSE not supported on this platform");

        var buffer = new float[length];

        const float r = 0.125f;
        const float g = 0.625f;
        const float b = 0.875f;

        // Act: fill the test buffer through the SSE implementation.
        MaterialAtlasParamsBuilder.FillRgbTripletsVector128Sse(buffer, r, g, b);

        // Assert: SIMD stores preserve the scalar RGB ordering.
        AssertRgbTriplets(buffer, r, g, b);
    }

    /// <summary>The AVX filler writes expected triplets for supported hardware and vector tails.</summary>
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
        // Arrange: include lengths that exercise vector-sized chunks and leftover pixels.
        Assert.SkipWhen(!Avx.IsSupported, "AVX not supported on this platform");

        var buffer = new float[length];

        const float r = 0.1f;
        const float g = 0.2f;
        const float b = 0.3f;

        // Act: fill the test buffer through the AVX implementation.
        MaterialAtlasParamsBuilder.FillRgbTripletsVector256Avx(buffer, r, g, b);

        // Assert: SIMD stores preserve the scalar RGB ordering.
        AssertRgbTriplets(buffer, r, g, b);
    }
    #endregion

    #region Private
    /// <summary>Checks buffer alignment and every RGB channel in each packed pixel.</summary>
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
    #endregion
}
