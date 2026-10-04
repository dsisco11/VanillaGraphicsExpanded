using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks bounded UV receiver selection against independently projected optical geometry.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterUvRefractionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    private const int Width = 128;
    private const int Height = 64;

    #region Public API
    #region Optical projection
    /// <summary>Flat normal incidence stays undistorted, while opposite local wave slopes bend in opposite directions.</summary>
    [Theory]
    [InlineData(-.3f)]
    [InlineData(0f)]
    [InlineData(.3f)]
    public void LocalWaveSlopeControlsSignedDisplacement(float slope)
    {
        var projection = Projection(60,2);
        Vector3 surface = new(0,0,-2), normal = Vector3.Normalize(new Vector3(slope,0,1));
        var result = Render(surface,normal,projection,10,false,false);
        Vector3 direction = Refract(-Vector3.UnitZ,normal,1 / 1.333f);
        Vector2 expectedUv = Project(surface + direction * (-8 / direction.Z),projection);
        float displacement = result[4][2] - .5f;
        Assert.Equal(-Math.Sign(slope),Math.Sign(displacement));
        Assert.InRange(MathF.Abs(displacement - (expectedUv.X - .5f)),0,.00001f);
        Assert.Equal(1,result[0][0]); Assert.Equal(1,result[0][3]);
        Assert.InRange(MathF.Abs(result[2][0] - (5 + 2 * displacement)),0,.00002f);
    }

    /// <summary>Snell displacement responds to actual projection and preserves HDR at either background resolution.</summary>
    [Theory]
    [InlineData(45, .5f, false)]
    [InlineData(45, .5f, true)]
    [InlineData(45, 2f, false)]
    [InlineData(45, 2f, true)]
    [InlineData(90, .5f, false)]
    [InlineData(90, .5f, true)]
    [InlineData(90, 2f, false)]
    [InlineData(90, 2f, true)]
    [InlineData(60, 1.7f, false, .23f)]
    [InlineData(60, 1.7f, true, .23f)]
    [InlineData(75, 1.7f, false, -.17f)]
    [InlineData(75, 1.7f, true, -.17f)]
    public void ProjectedSnellOffsetMatchesCoordinateRadiance(float fieldOfView, float aspect, bool half, float shift = 0)
    {
        var projection = Projection(fieldOfView, aspect);
        projection.M31 = shift; projection.M32 = shift * -.5f;
        Vector3 surface = new(0,0,-2), normal = Vector3.Normalize(new Vector3(-.3f,.1f,1));
        Vector3 direction = Refract(Vector3.Normalize(surface), normal, 1 / 1.333f);
        float cosine = -Vector3.Dot(direction,normal);
        // The opaque receiver is independently authored at z=-10. Its ray
        // intersection is independent of reduced sampling offsets and water tilt.
        Vector3 endpoint = surface + direction * (-8 / direction.Z);
        Vector2 expectedUv = Project(endpoint, projection);
        var result = Render(surface, normal, projection, 10, half, false);
        Assert.Equal(1,result[0][0]); Assert.Equal(2,result[0][1]); Assert.Equal(1,result[0][3]);
        // Reduced source provenance changes the retained vertices, not the query
        // ray. Cached geometry interpolation must recover the physical coordinate
        // when that coordinate lies inside the actual source support.
        Vector2 sourceUv = expectedUv;
        Assert.InRange(MathF.Abs(result[2][0] - (4 + 2 * sourceUv.X)),0,.0002f);
        Assert.InRange(MathF.Abs(result[2][1] - (2 + sourceUv.Y)),0,.0002f);
        Assert.True(result[2][0] > 1);
        Assert.InRange(MathF.Abs(result[1][2] + 10),0,.0002f);
        Vector3 expectedPosition = Position(sourceUv,10,projection);
        float expectedLength = -Vector3.Dot(expectedPosition - surface,normal) / cosine;
        Assert.InRange(MathF.Abs(result[0][2] - expectedLength),0,.0003f);
        Assert.Equal(2,result[3][3]);
        Assert.InRange(MathF.Abs(result[4][2] - expectedUv.X),0,.00002f);
        Assert.InRange(MathF.Abs(result[4][3] - expectedUv.Y),0,.00002f);
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(MathF.Abs(result[3][channel] - direction[channel]),0,.00001f);
    }

    /// <summary>Physical Snell displacement naturally approaches zero with shallow receiver separation.</summary>
    [Theory]
    [InlineData(.03f)]
    [InlineData(.1f)]
    [InlineData(.3f)]
    public void ThinWaterRetainsThicknessScaledSnellDisplacement(float separation)
    {
        var projection = Projection(60,2);
        // Keep all four source taps physically submerged. The opaque plane's
        // exact intersection determines displacement, with no arbitrary ramp.
        Vector3 surface = new(0,0,-2), normal = Vector3.Normalize(new Vector3(-.1f,0,1));
        Vector3 direction = Refract(Vector3.Normalize(surface),normal,1 / 1.333f);
        Vector2 expectedUv = Project(surface + direction * (-separation / direction.Z),projection);
        var result = Render(surface,normal,projection,2 + separation,false,false);
        Assert.Equal(1,result[0][0]); Assert.Equal(1,result[0][3]);
        Assert.InRange(MathF.Abs(result[2][0] - (4 + 2 * expectedUv.X)),0,.00002f);
        Assert.InRange(MathF.Abs(result[4][2] - expectedUv.X),0,.0000002f);
        Assert.Equal(2,result[3][3]);
    }

    #endregion

    #region Receiver eligibility
    /// <summary>Unavailable distorted coverage keeps the validated seed without importing a foreground color.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void InvalidCandidateKeepsUndistortedSeed(bool half, bool foreground)
    {
        var projection = Projection(60,2);
        Vector3 surface = new(0,0,-2), normal = Vector3.Normalize(new Vector3(-.7f,0,1));
        var result = Render(surface,normal,projection,10,half,false,(colors,depths) =>
        {
            // The positive-X distorted footprint is separated from the center seed.
            // Cover that entire region, including half-resolution reduction footprints.
            for (int y = 0; y < Height; y++)
            for (int x = Width / 2 + 3; x < Width; x++)
            {
                int index = y * Width + x;
                colors[index * 4] = 1000;
                if (foreground) depths[index] = DeviceDepth(1);
                else colors[index * 4 + 3] = 0;
            }
        });
        Assert.Equal(1,result[0][0]); Assert.Equal(1,result[0][3]); Assert.Equal(2,result[3][3]);
        Assert.InRange(MathF.Abs(result[2][0] - (5 - (half ? 1f / Width : 0))),0,.00002f);
    }

    /// <summary>Reduced source-hull correction rejects fresh foreground and unrelated depth-layer corners.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdjacentSourceRepairRejectsUnsupportedNewCorners(bool foreground)
    {
        var projection = Projection(60,2);
        Vector3 surface = new(0,0,-2), normal = Vector3.Normalize(new Vector3(-.25f,0,1));
        Vector3 direction = Refract(-Vector3.UnitZ,normal,1/1.333f);
        Vector2 query = Project(surface + direction*(-8/direction.Z),projection);
        int nominalOrigin = (int)MathF.Floor(query.X*(Width/2)-.5f);
        // Constant depth selects each reduced cell's first full-size source.
        // The optical query is inside the nominal footprint but beyond its
        // retained right vertex, so actual-coverage repair needs source x=68.
        Assert.Equal(32,nominalOrigin);
        float sourceRight = (2*(nominalOrigin+1)+.5f)/Width;
        Assert.True(query.X > sourceRight);
        Assert.True(query.X < (nominalOrigin+1.5f)/(Width/2));
        Assert.Equal(68,2*(nominalOrigin+2));
        var result = Render(surface,normal,projection,10,true,false,(colors,depths) =>
        {
            for (int y = 0; y < Height; y++)
            for (int x = 68; x < Width; x++)
            {
                int pixel = y*Width+x;
                colors[pixel*4] = 1000;
                depths[pixel] = DeviceDepth(foreground ? 1 : 20);
            }
        });
        Assert.Equal(1,result[0][0]); Assert.Equal(2,result[0][1]);
        Assert.InRange(MathF.Abs(result[1][2]+10),0,.0002f);
        // Rejected repair preserves only the already validated candidate's
        // coordinate field; neither new corner may enter HDR interpolation.
        float representedUv = query.X-.5f/Width;
        Assert.InRange(MathF.Abs(result[2][0]-(4+2*representedUv)),0,.00002f);
        Assert.Equal(2,result[3][3]);
    }

    /// <summary>TIR and absent seed support cannot become transmitted UV receivers.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableSeedAndTirDoNotTransmit(bool tir)
    {
        var normal = tir ? Vector3.Normalize(new Vector3(.9f,0,.3f)) : Vector3.UnitZ;
        var result = Render(new(0,0,-2),normal,Projection(60,2),10,false,tir,
            tir ? null : (colors,_) => Array.Clear(colors));
        Assert.Equal(0,result[0][0]); Assert.Equal(0,result[0][1]);
        Assert.Equal(tir ? 0 : 1,result[3][3]);
    }

    #endregion

    #region Boundary and transport
    /// <summary>Exit transport uses the water camera segment while preserving the refracted air direction.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnderwaterLengthIsCameraToInterface(bool half)
    {
        Vector3 surface = new(.2f,0,-2), normal = Vector3.UnitZ;
        var result = Render(surface,normal,Projection(60,2),10,half,true);
        Assert.Equal(1,result[0][0]); Assert.Equal(2,result[0][1]);
        Assert.InRange(MathF.Abs(result[0][2] - surface.Length()),0,.00001f);
        Vector3 expected = Refract(Vector3.Normalize(surface),normal,1.333f);
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(MathF.Abs(result[3][channel] - expected[channel]),0,.00001f);
    }

    /// <summary>An offscreen estimate keeps supported near-edge transmission without clamping or confidence loss.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffscreenEstimateKeepsNearEdgeSeed(bool half)
    {
        var projection = Projection(60,2);
        Vector3 surface = Position(new(.98f,.5f),2,projection);
        Vector3 normal = Vector3.Normalize(new Vector3(-.9f,0,.4f));
        var result = Render(surface,normal,projection,10,half,false);
        Assert.Equal(1,result[0][0]); Assert.Equal(1,result[0][3]);
        Assert.Equal(1,result[3][3]);
        Assert.InRange(MathF.Abs(result[2][0] - (5.96f - (half ? 1f / Width : 0))),0,.00002f);
        Assert.InRange(MathF.Abs(result[4][2] - .98f),0,.00001f);
    }

    /// <summary>Approximate transport remains bounded even when the visible receiver is much farther away.</summary>
    [Fact]
    public void LongWaterPathIsCappedAtThirtyTwoMetres()
    {
        var result = Render(new(0,0,-2),Vector3.UnitZ,Projection(60,2),60,false,false);
        Assert.Equal(1,result[0][0]); Assert.Equal(32,result[0][2]);
        Assert.Equal(1,result[0][3]); Assert.Equal(2,result[3][3]);
    }
    #endregion
    #endregion

    #region Private
    #region GPU rendering
    /// <summary>Draws the unmodified UV receiver with optional production reduction and deterministic input fields.</summary>
    private float[][] Render(Vector3 surface, Vector3 normal, Matrix4x4 projection, float metres,
        bool half, bool underwater, Action<float[],float[]>? edit = null)
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
        var program = Programs.Create<WaterUvRefractionShaderProgram>();
        var inputs = (IWaterUvRefractionBindings)program;
        inputs.Surface = surface; inputs.Normal = normal; inputs.Projection = projection;
        Assert.True(Matrix4x4.Invert(projection, out var inverse));
        inputs.InverseProjection = inverse;
        inputs.FrameSize = new(Width,Height); inputs.Underwater = underwater ? 1 : 0;
        inputs.Color = reducedColor ?? color; inputs.Depth = reducedDepth ?? depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program,target);
        return [target[0].ReadPixels(),target[1].ReadPixels(),target[2].ReadPixels(),target[3].ReadPixels(),target[4].ReadPixels()];
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

    /// <summary>Reconstructs a planar receiver directly from camera focal scales and axial depth.</summary>
    private static Vector3 Position(Vector2 uv, float metres, Matrix4x4 projection) =>
        new((uv.X * 2 - 1 + projection.M31) * metres / projection.M11,
            (uv.Y * 2 - 1 + projection.M32) * metres / projection.M22,-metres);

    /// <summary>Applies Snell's law independently, including its total internal reflection condition.</summary>
    private static Vector3 Refract(Vector3 incident, Vector3 normal, float eta)
    {
        float cosine = -Vector3.Dot(normal,incident);
        float square = 1 - eta * eta * (1 - cosine * cosine);
        return square < 0 ? Vector3.Zero : eta * incident + (eta * cosine - MathF.Sqrt(square)) * normal;
    }

    /// <summary>Converts axial metres to conventional hardware depth for the independently authored receiver.</summary>
    private static float DeviceDepth(float metres) => .5f * (1 + (100.1f - 20 / metres) / 99.9f);
    #endregion
    #endregion
}
