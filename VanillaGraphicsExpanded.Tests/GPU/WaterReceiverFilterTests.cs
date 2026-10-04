using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks eligible receiver support without foreground leakage or fictional depth layers.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterReceiverFilterTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Both resolution layouts preserve shared color/position support across rejected taps and sloped receivers.</summary>
    [Theory]
    [InlineData(false, "foreground")]
    [InlineData(true, "foreground")]
    [InlineData(false, "invalid")]
    [InlineData(true, "invalid")]
    [InlineData(false, "layers")]
    [InlineData(true, "layers")]
    [InlineData(false, "slope")]
    [InlineData(true, "slope")]
    [InlineData(false, "nonfinite")]
    [InlineData(true, "nonfinite")]
    [InlineData(true, "provenance")]
    [InlineData(false, "slopeclipped")]
    [InlineData(true, "slopeclipped")]
    [InlineData(false, "all-alpha")]
    [InlineData(true, "all-alpha")]
    [InlineData(false, "all-nan-rgb")]
    [InlineData(true, "all-nan-rgb")]
    [InlineData(false, "all-inf-rgb")]
    [InlineData(true, "all-inf-rgb")]
    [InlineData(false, "all-nan-alpha")]
    [InlineData(true, "all-nan-alpha")]
    [InlineData(false, "all-inf-alpha")]
    [InlineData(true, "all-inf-alpha")]
    [InlineData(false, "inconsistent")]
    [InlineData(true, "inconsistent")]
    public void FilterKeepsOnlyCompatibleReceiverSupport(bool half, string scenario)
    {
        EnsureShaderTestAvailable();
        const int fullWidth = 5, fullHeight = 3;
        int width = half ? 3 : fullWidth, height = half ? 2 : fullHeight;
        int depthChannels = half ? 4 : 1;
        using var color = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(width, height, half ? PixelInternalFormat.Rgba32f : PixelInternalFormat.R32f);
        float[] colors = new float[width * height * 4], depths = new float[width * height * depthChannels];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            bool anchor = x == 0 && y == 0;
            float metres = scenario == "foreground" && anchor ? 1 : scenario == "layers" && !anchor ? 20
                : scenario == "slope" ? 10 + x * .04f + y * .06f : scenario == "slopeclipped" ? 10 + x * .04f + y * .3f : 10;
            depths[index * depthChannels] = DeviceDepth(metres);
            if (half)
            {
                depths[index * 4 + 1] = (Math.Min(x * 2 + 1, fullWidth - 1) + .5f) / fullWidth;
                depths[index * 4 + 2] = (Math.Min(y * 2 + 1, fullHeight - 1) + .5f) / fullHeight;
                depths[index * 4 + 3] = scenario == "invalid" ? 0 : 1;
            }
            colors[index * 4] = scenario.StartsWith("slope") ? metres : metres is 1 or 20 ? 1000 : 8;
            colors[index * 4 + 1] = 2; colors[index * 4 + 2] = .5f;
            colors[index * 4 + 3] = scenario == "invalid" ? 0 : 1;
        }
        // Independently reject sky, nonfinite depth, invalid coverage and foreground
        // in the queried footprint; mixed defects must leave valid support uncontaminated.
        if (scenario == "invalid")
        {
            Array.Fill(colors, 1f);
            depths[0] = 1;
            depths[depthChannels] = float.NaN;
            colors[width * 4 + 3] = 0;
            depths[(width + 1) * depthChannels] = DeviceDepth(1);
            if (half) for (int i = 0; i < width * height; i++) depths[i * 4 + 3] = 1;
        }
        if (scenario == "nonfinite") colors[0] = float.NaN;
        if (scenario == "inconsistent") colors[0] = float.NaN;
        if (scenario == "provenance") depths[1] = float.NaN;
        if (scenario.StartsWith("all-"))
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                // Valid hardware depth must not substitute for the existing color
                // validity contract, including nonfinite alpha and RGB channels.
                if (scenario == "all-alpha") colors[pixel * 4 + 3] = 0;
                if (scenario == "all-nan-rgb") colors[pixel * 4] = float.NaN;
                if (scenario == "all-inf-rgb") colors[pixel * 4 + 1] = float.PositiveInfinity;
                if (scenario == "all-nan-alpha") colors[pixel * 4 + 3] = float.NaN;
                if (scenario == "all-inf-alpha") colors[pixel * 4 + 3] = float.PositiveInfinity;
            }
        if (scenario != "inconsistent") WaterReceiverTestInputs.EncodeDepthValidity(colors, depths, depthChannels);
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        var program = Programs.Create<WaterReceiverFilterShaderProgram>();
        var inputs = (IWaterReceiverFilterBindings)program;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, fullWidth / (float)fullHeight, .1f, 100);
        projection.M33 = -100.1f / 99.9f; projection.M43 = -20f / 99.9f;
        Assert.True(Matrix4x4.Invert(projection, out var inverse));
        inputs.InverseProjection = inverse;
        inputs.SampleUv = new(.75f / width, .75f / height);
        inputs.Surface = new(0,0,-2); inputs.Normal = Vector3.UnitZ;
        inputs.FullFrameSize = new(fullWidth, fullHeight);
        inputs.Color = color; inputs.Depth = depth;
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        float[] position = target[0].ReadPixels(), radiance = target[1].ReadPixels();
        bool rejected = scenario is "invalid" or "inconsistent" || scenario.StartsWith("all-");
        Assert.Equal(rejected ? 0 : 1, position[3]);
        float[] work = target[2].ReadPixels();
        Assert.InRange(work[0], 4, 6);
        Assert.Equal(rejected ? (scenario == "inconsistent" ? 1 : 0)
            : scenario is "foreground" or "nonfinite" or "provenance" ? 3
            : scenario == "layers" ? 1 : scenario == "slopeclipped" ? 2 : 4, work[1]);
        Assert.Equal(rejected ? 0 : 1, work[2]);
        Assert.Equal(0, work[3]);
        if (rejected)
        {
            Assert.Equal(0, radiance[3]);
            Assert.All(radiance, value => Assert.True(float.IsFinite(value)));
            return;
        }
        Assert.Equal(1, radiance[3]);
        if (scenario.StartsWith("slope"))
        {
            // Spatial fractions are one quarter on each axis; all four slope taps
            // fit the first slope (10.025m); the steeper slope excludes the second row
            // and renormalizes the first row to the independently expected 10.01m.
            Assert.InRange(MathF.Abs(-position[2] - (scenario == "slope" ? 10.025f : 10.01f)), 0, .0002f);
            Assert.InRange(MathF.Abs(radiance[0] + position[2]), 0, .0002f);
        }
        else
        {
            Assert.InRange(MathF.Abs(position[2] + 10), 0, .0002f);
            Assert.Equal(8, radiance[0]);
        }
        Assert.Equal(2, radiance[1]); Assert.Equal(.5f, radiance[2]);
        if (scenario == "layers")
        {
            // Only the anchor's layer survives. Its original full-resolution texel,
            // not the reduced texel center, must define reconstructed XY as well as Z.
            Vector2 originalUv = half ? new(1.5f / fullWidth, 1.5f / fullHeight) : new(.5f / fullWidth, .5f / fullHeight);
            Vector4 expected = Vector4.Transform(new Vector4(originalUv.X * 2 - 1, originalUv.Y * 2 - 1, DeviceDepth(10) * 2 - 1, 1), inverse);
            Assert.InRange(MathF.Abs(position[0] - expected.X / expected.W), 0, .0002f);
            Assert.InRange(MathF.Abs(position[1] - expected.Y / expected.W), 0, .0002f);
        }
    }
    #endregion

    #region Public API - Grazing receivers
    /// <summary>A real oblique floor retains slope support while unrelated deeper color stays excluded.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GrazingPlaneUsesCompatibleSupportOnly(bool half, bool unrelatedLayer)
    {
        EnsureShaderTestAvailable();
        const int fullSize = 256;
        int size = half ? fullSize / 2 : fullSize;
        int channels = half ? 4 : 1;
        using var color = DynamicTexture2D.Create(size, size, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(size, size, half ? PixelInternalFormat.Rgba32f : PixelInternalFormat.R32f);
        float[] colors = new float[size * size * 4], depths = new float[size * size * channels];
        Vector3 normal = new(0, MathF.Cos(MathF.PI / 12), MathF.Sin(MathF.PI / 12));
        int origin = size / 2;
        Vector3 expectedPosition = Vector3.Zero, expectedRadiance = Vector3.Zero;
        float totalWeight = 0;
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
        {
            int px = origin + x, py = origin + y, index = py * size + px;
            Vector2 uv = half ? new((px * 2 + 1.5f) / fullSize, (py * 2 + 1.5f) / fullSize)
                : new((px + .5f) / fullSize, (py + .5f) / fullSize);
            Vector3 ray = new((uv.X * 2 - 1) / MathF.Sqrt(3), (uv.Y * 2 - 1) / MathF.Sqrt(3), -1);
            // Independently intersect n.P=-5. The isolated farther layer remains
            // below water but must never enter this surface's weighted result.
            float axialDepth = -5 / Vector3.Dot(normal, ray);
            bool excluded = unrelatedLayer && x == 1 && y == 1;
            if (excluded) axialDepth *= 2;
            depths[index * channels] = DeviceDepth(axialDepth);
            if (half)
            {
                depths[index * 4 + 1] = uv.X; depths[index * 4 + 2] = uv.Y;
                depths[index * 4 + 3] = 1;
            }
            Vector3 radiance = excluded ? new(1000,0,0) : new(axialDepth, 2, .5f);
            colors[index * 4] = radiance.X; colors[index * 4 + 1] = radiance.Y;
            colors[index * 4 + 2] = radiance.Z; colors[index * 4 + 3] = 1;
            if (excluded) continue;
            float weight = (x == 0 ? .75f : .25f) * (y == 0 ? .75f : .25f);
            expectedPosition += ray * axialDepth * weight;
            expectedRadiance += radiance * weight;
            totalWeight += weight;
        }
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, .1f, 100);
        projection.M33 = -100.1f / 99.9f; projection.M43 = -20f / 99.9f;
        Assert.True(Matrix4x4.Invert(projection, out var inverse));
        var program = Programs.Create<WaterReceiverFilterShaderProgram>();
        var inputs = (IWaterReceiverFilterBindings)program;
        inputs.SampleUv = new((origin + .75f) / size);
        inputs.Surface = new(0,0,-2); inputs.Normal = normal;
        inputs.InverseProjection = inverse; inputs.FullFrameSize = new(fullSize);
        inputs.Color = color; inputs.Depth = depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        var position = target[0].ReadPixels(); var sampled = target[1].ReadPixels();
        Assert.Equal(1,position[3]); Assert.Equal(1,sampled[3]);
        expectedPosition /= totalWeight; expectedRadiance /= totalWeight;
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(MathF.Abs(position[channel] - expectedPosition[channel]),0,.001f);
            Assert.InRange(MathF.Abs(sampled[channel] - expectedRadiance[channel]),0,.00001f);
        }
    }
    #endregion

    #region Public API - Depth discontinuities
    /// <summary>Two constant-depth columns cannot masquerade as a continuous steep plane from four coplanar corners.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoplanarFootprintDoesNotBridgeIndependentDepthColumns(bool half)
    {
        EnsureShaderTestAvailable();
        const int fullSize = 16;
        int size = half ? fullSize/2 : fullSize;
        using var color = DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(size,size,half ? PixelInternalFormat.Rgba32f : PixelInternalFormat.R32f);
        var colors = new float[size*size*4];
        var depths = new float[size*size*(half ? 4 : 1)];
        int origin = size/2;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int pixel = y*size+x;
            bool anchorLayer = x <= origin;
            float metres = anchorLayer ? 2.2f : 2.5f;
            depths[pixel*(half ? 4 : 1)] = DeviceDepth(metres);
            if (half)
            {
                depths[pixel*4+1] = (x*2+.5f)/fullSize;
                depths[pixel*4+2] = (y*2+.5f)/fullSize;
                depths[pixel*4+3] = 1;
            }
            colors[pixel*4] = anchorLayer ? 8 : 1000;
            colors[pixel*4+3] = 1;
        }
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,1,.1f,100);
        projection.M33 = -100.1f/99.9f; projection.M43 = -20f/99.9f;
        Assert.True(Matrix4x4.Invert(projection,out var inverse));
        var program = Programs.Create<WaterReceiverFilterShaderProgram>();
        var inputs = (IWaterReceiverFilterBindings)program;
        inputs.InverseProjection = inverse; inputs.FullFrameSize = new(fullSize);
        inputs.SampleUv = new((origin+.75f)/size);
        inputs.Surface = new(0,0,-2); inputs.Normal = Vector3.UnitZ;
        inputs.Color = color; inputs.Depth = depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program,target);
        // Four queried corners are geometrically coplanar even though the two
        // plateaus are disconnected. Outside neighbors disprove the invented slope.
        Assert.InRange(MathF.Abs(target[0].ReadPixels()[2]+2.2f),0,.0001f);
        Assert.InRange(MathF.Abs(target[1].ReadPixels()[0]-8),0,.0001f);
    }
    #endregion

    #region Private
    /// <summary>Maps axial metres to conventional OpenGL hardware depth independently of the receiver shader.</summary>
    private static float DeviceDepth(float metres) => .5f * (1 + (100.1f - 20 / metres) / 99.9f);
    #endregion
}
