using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Executes the owned binary sky program against controlled atmospheric and engine inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereOwnedSkyTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{

    #region Public API
    #region Color and effects
    /// <summary>Owned celestial composition retains producer airglow without subtracting a second stellar background and preserves local fog.</summary>
    [Fact]
    public void OwnedCelestialCompositionRetainsAirglowWithoutDaylightOrCloudFade()
    {
        EnsureShaderTestAvailable();
        // Arrange: publish a quantized source and verify its decoded upload before composing the owned celestial term.
        using var sky=new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(),TestFramework);
        float total=.0004f/MathF.PI,stellar=0;
        sky.PublishConstant(new(total));
        // Act: read the source encoding and render upward, fogged, and below-horizon rays.
        // Compare draw composition to the actual binary16 upload rather than treating
        // its subnormal quantization as a shader subtraction error.
        float decoded=GpuLocalRead.Scalar((GpuTexture)AtmosphereModSystem.SkyTexture.Resource!,0)
            /AtmosphereModSystem.Lighting.LookupStorageScale;
        Assert.InRange(MathF.Abs(decoded-total),0,MathF.Pow(2,-24)/AtmosphereModSystem.Lighting.LookupStorageScale);
        foreach(float daylight in new[]{0f,.5f,1f})
        {
            using var upwardImage=sky.Draw(Vector3.UnitY,true,daylight:daylight,ownedCelestial:true,replacedBackground:stellar);float[] upward=upwardImage.Texture.ReadPixelsRegion(0,0,1,1);
            Assert.All(upward[..3],value=>Assert.InRange(value,decoded-1e-7f,decoded+1e-7f));
            Assert.Equal(0,upward[3]);
            using var fogImage=sky.Draw(Vector3.UnitY,true,daylight:daylight,fogMin:.3f,ownedCelestial:true,replacedBackground:stellar);float[] fog=fogImage.Texture.ReadPixelsRegion(0,0,1,1);
            Assert.InRange(fog[3],.29999f,.30001f);
        }
        using var belowImage=sky.Draw(-Vector3.UnitY,true,ownedCelestial:true,replacedBackground:stellar);float[] below=belowImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: decoded source, rendered RGB and fog alpha preserve their independent contracts.
        Assert.All(below[..3],value=>Assert.InRange(value,decoded-1e-7f,decoded+1e-7f));
    }
    /// <summary>The installed owned shader independently reconstructs solar and lunar angular lobes from three bands.</summary>
    [Fact]
    public void BinarySkyReconstructsIndependentCelestialDirections()
    {
        EnsureShaderTestAvailable();
        // Arrange: use distinguishable solar and lunar bands with the shared background removed.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishNight();
        // Act: sample each celestial direction from the same published bands.
        foreach (Vector3 direction in new[] {Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ})
        {
            using var actualImage=sky.Draw(direction, true);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
            // Assert: both directional lobes match independently evaluated angular transport.
            Vector3 expected = new Vector3(.001f) + new Vector3(.01f, 0, 0) * AtmosphereMieTransport.Factor(Vector3.Dot(direction, Vector3.UnitX))
                + new Vector3(0, .02f, 0) * AtmosphereMieTransport.Factor(Vector3.Dot(direction, Vector3.UnitY));
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(Math.Abs(actual[channel] - new[] {expected.X, expected.Y, expected.Z}[channel]), 0, .00002f);
        }
    }

    /// <summary>The installed owned shader retains physical sky and bounds only its authored effect additions.</summary>
    [Theory]
    [InlineData(0f, 0f)] [InlineData(1f, 0f)] [InlineData(0f, 2f)] [InlineData(1f, 2f)]
    public void PhysicalSkyAndAuthoredEffectsFitIndependentEnvelope(float underwater, float perception)
    {
        EnsureShaderTestAvailable();
        // Arrange: seed high physical radiance so the independent envelope checks the combined effect bound.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework, liquidDepth: 0);
        sky.PublishConstant(new(80, 40, 20));
        float bound = 80 + VanillaGraphicsExpanded.PBR.SceneColor.SceneColorCompatibilityBounds.PhysicalSky(perception, .5f, 2);
        // Act: render representative upward and oblique directions with the requested effect strengths.
        foreach (Vector3 direction in new[] { Vector3.UnitZ, new Vector3(.3f, .2f, -.9f) })
        {
            using var actualImage=sky.Draw(Vector3.Normalize(direction), true, underwater: underwater, nightVision: 2, psychedelic: perception);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
            // Assert: every color channel remains inside the physical-plus-effect envelope.
            Assert.All(actual[..3], value => Assert.InRange(value, 0, bound));
        }
    }

    /// <summary>Retains radiance above one in linear mode and resolves and dithers exactly once in legacy mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BinaryFullscreenSkySelectsColorConvention(bool linear)
    {
        EnsureShaderTestAvailable();
        // Arrange: keep the same HDR source while toggling only the output color convention.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(8, 4, 2));
        // Act: render one HDR sample through the selected color convention.
        using var actualImage=sky.Draw(Vector3.UnitZ, linear);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: resolve/dither the nonlinear path once and preserve unrelated attachments.
        for (int channel = 0; channel < 3; channel++)
        {
            float radiance = 8f / (1 << channel);
            float expected = linear ? radiance : Display(radiance, 8);
            Assert.InRange(actual[channel], expected - .00002f, expected + .00002f);
        }
        Assert.Equal(1, actual[3]);
        Assert.Equal(new float[] { 0, 0, 0, 1 }, sky.Target[1].ReadPixels());
        foreach (int attachment in new[] { 2, 3, 4, 5, 7 })
            ImageAssertions.Comparisons.CompareConstant(sky.Target[attachment], (float)attachment).AssertEqual();
        using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, sky.Target[6].TextureId))
        {
            uint[] patch = new uint[4];
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, patch);
            Assert.All(patch, value => Assert.Equal(6u, value));
        }
    }

    /// <summary>Leaves transparent night sky for stars, then increases sky coverage through twilight to day.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.35f)]
    [InlineData(1f)]
    public void BinaryFullscreenSkyPreservesTwilightCoverage(float daylight)
    {
        EnsureShaderTestAvailable();
        // Arrange: publish a fixed lookup and vary only daylight coverage.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(2, 1, .5f));
        // Act: render the same source at the selected daylight coverage.
        using var actualImage=sky.Draw(Vector3.UnitZ, true, daylight: daylight);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: alpha tracks the twilight factor while source RGB stays unchanged.
        Assert.InRange(actual[3], daylight - .00001f, daylight + .00001f);
        Assert.InRange(actual[0], 1.99999f, 2.00001f);
    }

    /// <summary>Preserves the engine fog minimum before twilight blends coverage toward opaque daylight.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BinaryFullscreenSkyPreservesFogCoverage(bool linear)
    {
        EnsureShaderTestAvailable();
        // Arrange: combine an engine fog minimum with twilight in each output mode.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(2, 1, .5f));
        // Act: render with the fog minimum applied before twilight coverage.
        using var actualImage=sky.Draw(Vector3.UnitY, linear, daylight: .2f, fogMin: .25f);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: output alpha combines the engine floor and twilight coverage.
        Assert.InRange(actual[3], .39999f, .40001f);
    }

    /// <summary>Uses virtual sky distance and projected effect depth rather than the fullscreen triangle's far depth.</summary>
    [Fact]
    public void FullscreenPreservesVirtualFlatFogDepth()
    {
        EnsureShaderTestAvailable();
        // Arrange: choose a projection with a known virtual radius and projected depth.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(Vector3.One);
        Matrix4x4 projection = Perspective(1.2f, 1);
        projection.M33 = -1100f / 900; projection.M43 = -200000f / 900;
        Matrix4x4 transform = Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ) * projection;
        // Act: render with the virtual sky transform and authored flat-fog parameters.
        using var actualImage=sky.Draw(Vector3.UnitY, true, daylight: 0, transform: transform, flatFogDensity: -.001f, flatFogStart: 300);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: alpha follows virtual projected depth rather than fullscreen-triangle depth.
        // At radius 250, depth is 2/3; the vertical exponential contributes less than the depth term.
        float expected = MathF.Max(.001f * (2f / 3) * 300 / 3.3f, 1 - MathF.Exp(-.05f));
        Assert.InRange(MathF.Abs(actual[3] - expected), 0, .00001f);
    }

    /// <summary>Decodes authored liquid and night-vision colors only at the scene-linear boundary.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BinaryFullscreenSkyPreservesLiquidAndNightVision(bool linear, bool underwater)
    {
        EnsureShaderTestAvailable();
        // Arrange: independently toggle liquid coverage and retain a fixed authored night-vision tint.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework, liquidDepth: 0);
        sky.PublishConstant(new(2, 1, .5f));
        // Act: render fixed source radiance across liquid and color-mode combinations.
        using var actualImage=sky.Draw(Vector3.UnitZ, linear, underwater: underwater ? 1 : 0, nightVision: .5f);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: liquid tint, night vision and display transfer are applied at their respective boundaries.
        float murk = underwater ? 0 : 1 - .1f / 100;
        float[] tint = [.2f, .1f, .05f];
        float[] night = [.05f, .25f, .05f];
        for (int channel = 0; channel < 3; channel++)
        {
            float radiance = 2f / (1 << channel);
            float baseColor = linear ? radiance : Encode(radiance / 3);
            float expected = baseColor * (1 - murk) + (linear ? 32 * Decode(tint[channel]) : tint[channel]) * murk
                + (linear ? 32 * Decode(night[channel]) : night[channel]);
            if (!linear) expected = Math.Clamp(expected - .4921875f / 255, 0, 1);
            Assert.InRange(actual[channel], expected - .00005f, expected + .00005f);
        }
    }

    /// <summary>Retains spatial perception tint without changing twilight coverage or producing invalid output.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BinaryFullscreenSkyPreservesPerceptionEffect(bool linear)
    {
        EnsureShaderTestAvailable();
        // Arrange: capture the same direction and daylight with and without the spatial tint.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(.7f, .3f, .1f));
        Vector3 direction = Vector3.Normalize(new Vector3(.3f, .7f, .4f));
        // Act: capture baseline and perceived fragments at the same direction.
        using var originalImage=sky.Draw(direction, linear, daylight: .4f);float[] original=originalImage.Texture.ReadPixelsRegion(0,0,1,1);
        using var affectedImage=sky.Draw(direction, linear, daylight: .4f, psychedelic: .75f);float[] affected=affectedImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: perception changes RGB while preserving coverage and finite output.
        Assert.Equal(original[3], affected[3]);
        Assert.Contains(Enumerable.Range(0, 3), channel => MathF.Abs(original[channel] - affected[channel]) > .0001f);
        Assert.All(affected, value => Assert.True(float.IsFinite(value) && value >= 0));
    }

    #endregion
    #region Atmospheric lookup
    /// <summary>Checks every pixel against independent camera rays, including asymmetric wide fields and orthographic views.</summary>
    [Theory]
    [InlineData(.5f, false)]
    [InlineData(1.9f, false)]
    [InlineData(1.2f, true)]
    public void FullscreenReconstructionCoversRotatedCameraAndCorners(float fov, bool orthographic)
    {
        EnsureShaderTestAvailable();
        // Arrange: populate a direction-dependent lookup and rotate an asymmetric camera projection.
        const int width = 7, height = 5;
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework, width: width, height: height);
        Vector3 sun = Vector3.Normalize(new Vector3(.3f, .8f, -.4f));
        float[] radiance = new float[16 * 8 * 4], mie = new float[radiance.Length];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 16; x++)
        {
            int i = (y * 16 + x) * 4;
            float lobe = .05f * AtmosphereMieTransport.Factor(Vector3.Dot(AtmosphereMieTransport.Direction(x, y, 16, 8, 0), sun));
            radiance[i] = radiance[i + 1] = radiance[i + 2] = .2f + lobe;
            mie[i] = mie[i + 1] = mie[i + 2] = .05f;
            radiance[i + 3] = mie[i + 3] = 1;
        }
        sky.Publish(radiance, mie, 16, 8, 0, sun);
        Matrix4x4 view = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateRotationX(-.4f);
        Assert.True(Matrix4x4.Invert(view, out var inverseView));
        Matrix4x4 projection = orthographic ? Matrix4x4.CreateOrthographic(9, 5, .1f, 100) : Perspective(fov, (float)width / height);
        if (!orthographic) { projection.M31 = .2f; projection.M32 = -.15f; }
        using var actual = sky.Draw(-Vector3.UnitZ, true, sun: sun, transform: view * projection);
        // Act: read back the complete image for the independent analytic-ray comparison.
        var expectedPixels=new float[width*height*4];var tolerances=new float[expectedPixels.Length];
        // Analytic perspective rays avoid reproducing the shader's inverse-projection algorithm.
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float nx = 2f * (x + .5f) / width - 1, ny = 2f * (y + .5f) / height - 1;
            Vector3 cameraRay = orthographic ? -Vector3.UnitZ : new((nx + projection.M31) / projection.M11, (ny + projection.M32) / projection.M22, -1);
            Vector3 ray = Vector3.Normalize(Vector3.TransformNormal(cameraRay, inverseView));
            float expected = .2f + .05f * AtmosphereMieTransport.Factor(Vector3.Dot(ray, sun));
            int i = (y * width + x) * 4;
            for (int channel = 0; channel < 3; channel++) { expectedPixels[i+channel]=expected;tolerances[i+channel]=.0001f; }
            expectedPixels[i+3]=1;
        }
        // Assert: all image pixels match the ray-based oracle within its declared tolerance.
        ImageAssertions.Comparisons.CompareFloating(actual.Texture,expectedPixels,tolerances).AssertEqual();
    }

    /// <summary>Tracks the published non-axis-aligned sun instead of leaving the Mie lobe at a fixed pole.</summary>
    [Fact]
    public void BinaryLookupUsesPublishedMieDirection()
    {
        EnsureShaderTestAvailable();
        // Arrange: publish a non-axis-aligned solar lobe and choose a displaced comparison ray.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        Vector3 sun = Vector3.Normalize(new Vector3(.3f, .8f, .4f));
        const int width = 16, height = 8;
        float[] pixels = new float[width * height * 4], mie = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            float factor = AtmosphereMieTransport.Factor(Vector3.Dot(AtmosphereMieTransport.Direction(x, y, width, height, 0), sun));
            pixels[i] = pixels[i + 1] = pixels[i + 2] = .2f + .05f * factor;
            mie[i] = mie[i + 1] = mie[i + 2] = .05f;
            pixels[i + 3] = mie[i + 3] = 1;
        }
        sky.Publish(pixels, mie, width, height, 0, sun);
        // Act: sample the lobe center and a known angular offset.
        Vector3 away = Vector3.Normalize(sun + Vector3.Normalize(Vector3.Cross(sun, Vector3.UnitY)) * MathF.Tan(MathF.PI / 6));
        using var centerImage=sky.Draw(sun, true, sun: sun);float[] center=centerImage.Texture.ReadPixelsRegion(0,0,1,1);
        using var offsetImage=sky.Draw(away, true, sun: sun);float[] offset=offsetImage.Texture.ReadPixelsRegion(0,0,1,1);
        // Assert: the lobe follows the published direction rather than an axis default.
        Assert.InRange(center[0], .2f + .05f * AtmosphereMieTransport.Factor(1) - .0001f, .2f + .05f * AtmosphereMieTransport.Factor(1) + .0001f);
        Assert.InRange(offset[0], .2f + .05f * AtmosphereMieTransport.Factor(Vector3.Dot(sun, away)) - .0001f, .2f + .05f * AtmosphereMieTransport.Factor(Vector3.Dot(sun, away)) + .0001f);
        Assert.True(center[0] > offset[0]);
    }

    /// <summary>Reconstructs all elevation rows, a depressed horizon and both sides of the azimuth seam.</summary>
    [Theory]
    [InlineData(.001f, 24, false)]
    [InlineData(.001f, 24, true)]
    [InlineData(99f, 9, false)]
    [InlineData(99f, 9, true)]
    public void BinaryLookupReconstructsRowsAndWrapsSeam(float altitude, int height, bool linear)
    {
        EnsureShaderTestAvailable();
        // Arrange: encode row and column identities so vertical reconstruction and azimuth wrapping are observable.
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        const int width = 4;
        float[] pixels = new float[width * height * 4];
        float[] mie = new float[pixels.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            float angular = AtmosphereMieTransport.Factor(AtmosphereMieTransport.Direction(x, y, width, height, horizon).Y);
            pixels[i] = (float)y / (height - 1) + .05f * angular;
            pixels[i + 1] = x * .25f + .05f * angular;
            pixels[i + 2] = .05f * angular; pixels[i + 3] = 1;
            mie[i] = mie[i + 1] = mie[i + 2] = .05f; mie[i + 3] = 1;
        }
        sky.Publish(pixels, mie, width, height, horizon);
        // Act: sample vertical rows and both sides of the azimuth seam.
        foreach (float v in new[] { 0f, .25f, .499f, .5f, .501f, .75f, 1f })
        foreach (float azimuth in new[] { -.00001f, 0f, .00001f })
        {
            float elevation = AtmosphereSkyMapping.Elevation(v, horizon);
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            using var actualImage=sky.Draw(direction, linear, horizon: horizon);float[] actual=actualImage.Texture.ReadPixelsRegion(0,0,1,1);
            // Assert: row interpolation, horizon mapping and seam wrapping stay within the independent oracle.
            float lobe = .05f * AtmosphereMieTransport.Factor(direction.Y);
            float peak = Math.Max(v, .375f) + lobe;
            float expectedRed = linear ? v + lobe : Display(v + lobe, peak);
            float expectedGreen = linear ? .375f + lobe : Display(.375f + lobe, peak);
            Assert.True(MathF.Abs(actual[0] - expectedRed) <= .0008f, $"v={v}, azimuth={azimuth}, actual={actual[0]}, expected={expectedRed}");
            Assert.InRange(MathF.Abs(actual[1] - expectedGreen), 0, .0002f);
        }
    }
    #endregion
    #endregion

    #region Private
    #region Camera transforms
    /// <summary>Builds an OpenGL perspective matrix independently of production matrix helpers.</summary>
    private static Matrix4x4 Perspective(float fov, float aspect)
    {
        float scale = 1 / MathF.Tan(fov / 2);
        return new(scale / aspect, 0, 0, 0, 0, scale, 0, 0, 0, 0, -100.1f / 99.9f, -1, 0, 0, -20f / 99.9f, 0);
    }

    /// <summary>Serializes row-vector matrices as the equivalent OpenGL column-major transforms.</summary>
    private static float[] Elements(Matrix4x4 m) =>
        [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
         m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    /// <summary>Allows floating-point inversion differences while checking every transform component.</summary>
    private static void AssertClose(float[] expected, float[] actual)
    {
        for (int i = 0; i < expected.Length; i++) Assert.InRange(MathF.Abs(expected[i] - actual[i]), 0, .00002f);
    }

    #endregion
    #region Color transfer
    /// <summary>Computes the established shared display transfer and first-pixel ordered dither independently.</summary>
    private static float Display(float value, float peak) => Math.Clamp(Encode(value / (1 + peak)) - .4921875f / 255, 0, 1);
    /// <summary>Encodes one display-linear component using the standard piecewise sRGB transfer.</summary>
    private static float Encode(float value) => value <= .0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
    /// <summary>Decodes one authored sRGB component at the spatial-effect boundary.</summary>
    private static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);

    #endregion

    /// <summary>Owns typed fixture inputs while submitting the production sky executable through a complete RHI pass.</summary>
    private sealed class FullscreenSky : IDisposable
    {
        private readonly AtmosphereSkyShaderProgram program;
        private readonly AtmosphereModSystem atmosphere = new();
        private readonly FixtureUniformInputs inputs = new(112);
        private readonly VgeFrameUniformBuffer cameraInputs = new();
        private readonly PostprocessDraw draw = new();

        private readonly DynamicTexture2D depth;
        private readonly int width, height;
        internal GpuFramebuffer Target { get; }

        #region Public API
        #region Initialization
        /// <summary>Allocates all eight production outputs with integer patch metadata and controlled liquid depth.</summary>
        internal FullscreenSky(AtmosphereSkyShaderProgram program, ShaderTestFramework framework, float liquidDepth = 1, int width = 1, int height = 1)
        {
            this.width = width; this.height = height;
            this.program = program;
            Target = framework.CreateTestGBuffer(width, height, TextureFormat.Rgba32f, TextureFormat.Rgba32f,
                TextureFormat.Rgba32f, TextureFormat.Rgba32f, TextureFormat.Rgba32f,
                TextureFormat.Rgba32f, TextureFormat.Rgba32ui, TextureFormat.Rgba32f);
            depth = framework.CreateTexture(width, height, TextureFormat.R32f, Enumerable.Repeat(liquidDepth, width * height).ToArray());
        }
        #endregion

        #region Lookup publication
        /// <summary>Publishes constant radiance without a directional Mie contribution.</summary>
        internal void PublishConstant(Vector3 radiance)
        {
            float[] pixels = new float[4 * 2 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            { pixels[i] = radiance.X; pixels[i + 1] = radiance.Y; pixels[i + 2] = radiance.Z; pixels[i + 3] = 1; }
            Publish(pixels, new float[pixels.Length], 4, 2, 0);
        }
        /// <summary>Publishes distinguishable fixed transport coefficients for independent solar and lunar directions.</summary>
        internal void PublishNight()
        {
            const int width = 4, height = 2;
            float[] pixels = new float[width * height * 4];
            float[] solar = new float[pixels.Length], lunar = new float[pixels.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var direction = AtmosphereMieTransport.Direction(x, y, width, height, 0);
                int index = (y * width + x) * 4;
                pixels[index] = .001f + .01f * AtmosphereMieTransport.Factor(Vector3.Dot(direction, Vector3.UnitX));
                pixels[index + 1] = .001f + .02f * AtmosphereMieTransport.Factor(Vector3.Dot(direction, Vector3.UnitY));
                pixels[index + 2] = .001f; pixels[index + 3] = 1;
                solar[index] = .01f; solar[index + 3] = 1;
                lunar[index + 1] = .02f; lunar[index + 3] = 1;
            }
            atmosphere.Publish(new(Vector3.UnitX, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.Create(pixels))
            {
                Width = width, Height = height, HasNightSources = true, Moon = Vector3.UnitY,
                SkyMie = ImmutableArray.Create(solar), SkyMoonMie = ImmutableArray.Create(lunar),
                AerialMoonMie = ImmutableArray.Create(0f, 0f, 0f, 1f)
            });
        }
        /// <summary>Uses the real atmosphere owner to publish its split lookup and sampler policy.</summary>
        internal void Publish(float[] pixels, float[] mie, int width, int height, float horizon, Vector3? sun = null)
        {
            atmosphere.Publish(new(sun ?? Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.CreateRange(pixels))
                { Width = width, Height = height, HorizonElevation = horizon, SkyMie = ImmutableArray.CreateRange(mie) });
        }
        #endregion

        #region Rendering
        /// <summary>Centers the requested direction under the inverse view-projection transform and captures a single fragment.</summary>
        internal GpuImageSnapshot Draw(Vector3 direction, bool linear, float daylight = 1, float underwater = 0, float nightVision = 0, float horizon = 0, Vector3? sun = null, float psychedelic = 0, float fogMin = 0, Matrix4x4? transform = null, float flatFogDensity = 0, float flatFogStart = 0,bool ownedCelestial=false,float replacedBackground=0)
        {
            Vector3 up = MathF.Abs(direction.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY;
            Matrix4x4 viewProjection = transform ?? Matrix4x4.CreateLookAt(Vector3.Zero, direction, up) * Perspective(1.2f, 1);
            Assert.True(Matrix4x4.Invert(viewProjection, out var inverse));
            float[] identity = Elements(Matrix4x4.Identity);
            cameraInputs.Capture(Elements(viewProjection), identity, Elements(inverse), identity,
                Elements(viewProjection), Elements(viewProjection), new(width, height), 0, 0,
                Vector3.Zero, Vector3.Zero, 0, new(.1f, 100));
            program.FrameInputs = cameraInputs;
            Vector3 source = sun ?? AtmosphereModSystem.Lighting.Sun;
            inputs.Vector(0, source.X, source.Y, source.Z); inputs.Float(12, horizon);
            inputs.Vector(16, 0, fogMin, flatFogDensity); inputs.Float(28, flatFogStart);
            inputs.Vector(32, daylight, 0, 0); inputs.Float(44, 0);
            inputs.Vector(48, .5f, .25f, .125f); inputs.Float(60, underwater);
            inputs.Vector(64, nightVision, psychedelic, 0); inputs.Float(76, linear ? 1 : 0);
            inputs.Vector(80, 1 / AtmosphereModSystem.Lighting.LookupStorageScale, AtmosphereModSystem.Lighting.HasNightSources ? 3 : 2, ownedCelestial?1:0);
            inputs.Float(92,replacedBackground);
            Vector3 moon = AtmosphereModSystem.Lighting.Moon;
            inputs.Vector(96, moon.X, moon.Y, moon.Z);
            program.SkyLookup = AtmosphereModSystem.SkyTexture;
            program.LiquidDepth = depth;
            ClearMetadataAttachments();
            var pass = FramebufferTestSetup.DescribeAttachments(Target,
                [new(AttachmentIndex: 0), new(AttachmentIndex: 1)]);
            var bindings = program.CaptureBindings();
            ulong skyInputsIdentity = GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "SkyInputs");
            var typedInputs = bindings.Uniforms.Select(binding => binding.Identity == skyInputsIdentity
                ? new RHIUniformBinding(skyInputsIdentity, inputs.Capture())
                : binding);
            var capturedBindings = bindings.WithUniforms(typedInputs);
            var pipeline = draw.Prepare(program, pass);
            var commands = RHICommandContext.Current;
            using var boundary = commands.EnterBoundary(new("Tests.AtmosphereOwnedSky", true,
                graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
            boundary.Run(() => draw.Submit(commands, pipeline, pass, capturedBindings));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            return new GpuImageSnapshot(Target[0]);
        }
        #endregion

        #region Lifetime
        /// <summary>Retires the test publication and geometry before their shared context closes.</summary>
        public void Dispose()
        { inputs.Dispose(); cameraInputs.Dispose(); draw.Dispose(); atmosphere.Dispose(); }
        #endregion

        #endregion
        #region Private
        /// <summary>Restores recognizable values in all non-sky outputs through typed clear attachments.</summary>
        private void ClearMetadataAttachments()
        {
            var colors = Enumerable.Range(2, 6).Select(attachment => new RHIColorAttachment(
                AttachmentIndex: attachment,
                Load: AttachmentLoad.Clear,
                Clear: attachment == 6
                    ? ColorClearValue.UInt(6, 6, 6, 6)
                    : ColorClearValue.Float(attachment, attachment, attachment, attachment)));
            var pass = FramebufferTestSetup.DescribeAttachments(Target, colors);
            var commands = RHICommandContext.Current;
            using var boundary = commands.EnterBoundary(new("Tests.AtmosphereOwnedSky.Metadata", true,
                operations: BoundaryOperations.RenderPasses));
            boundary.Run(() => { commands.BeginRenderPass(pass); commands.EndRenderPass(); });
        }
        #endregion
        #endregion
    }
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
