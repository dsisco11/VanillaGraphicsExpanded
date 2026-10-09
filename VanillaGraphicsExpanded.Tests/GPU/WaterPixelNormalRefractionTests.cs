using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies the lowest-quality production selector uses bounded normal-detail distortion.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterPixelNormalRefractionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    private const int Width = 128;
    private const int Height = 64;

    #region Public API
    /// <summary>Matching geometric and shading normals preserve the screen coordinate at oblique incidence.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingNormalsHaveNoOffset(bool half)
    {
        var projection = Projection(60, 2);
        Vector3 surface = new(1, -.5f, -2);
        Vector3 normal = Vector3.Normalize(new Vector3(.1f, .2f, 1));
        var result = Render(surface, normal, projection, 10, half, false, baseNormal: normal);
        var uv = Project(surface, projection);
        Assert.Equal(1, result[0][0]);
        Assert.Equal(1, result[3][3]);
        Assert.InRange(MathF.Abs(result[2][0] - (4 + 2 * uv.X)), 0, .0002f);
        Assert.InRange(MathF.Abs(result[4][2] - uv.X), 0, .00001f);
        Assert.InRange(MathF.Abs(result[4][3] - uv.Y), 0, .00001f);
    }

    /// <summary>Wave detail controls signed offsets independently of distant receiver depth and image resolution.</summary>
    [Theory]
    [InlineData(-.5f, false)]
    [InlineData(.5f, false)]
    [InlineData(-.5f, true)]
    [InlineData(.5f, true)]
    public void WaveOffsetSaturatesWithDepth(float slope, bool half)
    {
        var projection = Projection(60, 2);
        Vector3 normal = Vector3.Normalize(new Vector3(slope, 0, 1));
        float expected = -normal.X * projection.M11 * .02f;
        foreach (float depth in new[] { 2.4f, 10f, 60f })
        {
            var result = Render(new(0, 0, -2), normal, projection, depth, half, false);
            Assert.Equal(1, result[0][0]);
            Assert.Equal(2, result[3][3]);
            Assert.InRange(MathF.Abs(result[4][2] - .5f - expected), 0, .00001f);
        }
    }

    /// <summary>Unavailable seed support and total internal reflection never publish transmission.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSeedOrTirRejectsTransmission(bool tir)
    {
        var normal = tir ? Vector3.Normalize(new Vector3(.9f, 0, .3f)) : Vector3.UnitZ;
        var result = Render(new(0, 0, -2), normal, Projection(60, 2), 10, false, tir,
            tir ? null : (colors, _) => Array.Clear(colors));
        Assert.Equal(0, result[0][0]);
    }
    /// <summary>Shallow separation softens detail distortion.</summary>
    [Theory]
    [InlineData(.06f)]
    [InlineData(.15f)]
    public void ShallowWaterSoftensOffset(float separation)
    {
        var projection = Projection(60, 2);
        Vector3 normal = Vector3.Normalize(new Vector3(-.1f, 0, 1));
        var result = Render(new(0, 0, -2), normal, projection, 2 + separation, false, false);
        float expected = -normal.X * projection.M11 * .02f * separation / .3f;
        Assert.Equal(1, result[0][0]);
        Assert.InRange(MathF.Abs(result[4][2] - .5f - expected), 0, .00001f);
    }

    /// <summary>Invalid displaced footprints retain seed radiance without foreground contamination.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InvalidCandidateKeepsSeed(bool half, bool foreground)
    {
        var projection = Projection(30, .5f);
        Vector3 normal = Vector3.Normalize(new Vector3(-.7f, 0, 1));
        var result = Render(new(0, 0, -2), normal, projection, 10, half, false, (colors, depths) =>
        {
            // Preserve the seed footprint and invalidate every displaced tap.
            for (int y = 0; y < Height; y++)
            for (int x = Width / 2 + 3; x < Width; x++)
            {
                int pixel = y * Width + x;
                colors[pixel * 4] = 1000;
                if (foreground) depths[pixel] = DeviceDepth(1);
                else colors[pixel * 4 + 3] = 0;
            }
        });
        Assert.Equal(1, result[0][0]);
        Assert.InRange(MathF.Abs(result[2][0] - 5), 0, .00002f);
    }
    #endregion

    #region Private
    #region GPU rendering
    /// <summary>Draws the lowest-quality production selector with optional production reduction and deterministic input fields.</summary>
    private float[][] Render(Vector3 surface, Vector3 normal, Matrix4x4 projection, float metres,
        bool half, bool underwater, Action<float[],float[]>? edit = null, Vector3? baseNormal = null)
    {
        EnsureShaderTestAvailable();
        using var color = DynamicTexture2D.Create(Width,Height,PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(Width,Height,PixelInternalFormat.R32f);
        float[] colors = new float[Width * Height * 4];
        float[] depths = Enumerable.Repeat(DeviceDepth(metres),Width * Height).ToArray();
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int offset = (y * Width + x) * 4;
            colors[offset] = 4 + 2 * (x + .5f) / Width;
            colors[offset + 1] = 2 + (y + .5f) / Height;
            colors[offset + 2] = .5f; colors[offset + 3] = 1;
        }
        edit?.Invoke(colors,depths);
        WaterReceiverTestInputs.EncodeDepthValidity(colors, depths);
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        using var reducedColor = half ? DynamicTexture2D.Create(Width / 2,Height / 2,PixelInternalFormat.Rgba32f) : null;
        using var reducedDepth = half ? DynamicTexture2D.Create(Width / 2,Height / 2,PixelInternalFormat.Rgba32f) : null;
        using var reducedTarget = half ? GpuFramebuffer.CreateMRT([reducedColor!,reducedDepth!]) : null;
        if (half)
        {
            var reduction = Programs.Create<WaterRefractionReductionShaderProgram>();
            reduction.SourceColor = color; reduction.SourceDepth = depth;
            TestFramework.RenderQuadTo(reduction,reducedTarget!);
        }
        var program = Programs.Create<WaterPixelNormalRefractionShaderProgram>();
        var inputs = (IWaterPixelNormalRefractionBindings)program;
        inputs.BaseNormal = baseNormal ?? Vector3.UnitZ; inputs.Surface = surface; inputs.Normal = normal; 
        Assert.True(Matrix4x4.Invert(projection, out var inverse));
        using var frameCamera = TestFrameCamera.CreateFromProjection(projection, Width, Height);
        program.FrameInputs = frameCamera;
        inputs.Underwater = underwater ? 1 : 0;
        inputs.Color = reducedColor ?? color; inputs.Depth = reducedDepth ?? depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program,target);
        return [target[0].ReadPixels(),target[1].ReadPixels(),target[2].ReadPixels(),target[3].ReadPixels(),target[4].ReadPixels(),target[5].ReadPixels()];
    }

    #endregion

    #region Independent optical references
    /// <summary>Builds a conventional OpenGL projection without borrowing shader calculations.</summary>
    private static Matrix4x4 Projection(float degrees, float aspect)
    {
        var matrix = Matrix4x4.CreatePerspectiveFieldOfView(degrees * MathF.PI / 180,aspect,.1f,100);
        matrix.M33 = -100.1f / 99.9f; matrix.M43 = -20f / 99.9f;
        return matrix;
    }

    /// <summary>Projects a physical view-space point into normalized image coordinates.</summary>
    private static Vector2 Project(Vector3 point, Matrix4x4 projection)
    {
        Vector4 clip = Vector4.Transform(new Vector4(point,1),projection);
        return new(clip.X / clip.W * .5f + .5f,clip.Y / clip.W * .5f + .5f);
    }

    /// <summary>Converts axial metres to conventional hardware depth for the independently authored receiver.</summary>
    private static float DeviceDepth(float metres) => .5f * (1 + (100.1f - 20 / metres) / 99.9f);
    #endregion
    #endregion
}




