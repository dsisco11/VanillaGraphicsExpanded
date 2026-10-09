using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes the owned binary sky program against controlled atmospheric and engine inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereOwnedSkyTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    #region Engine inputs
    /// <summary>Captures altitude and fivefold wind animation while shared camera fields remain outside the sky effect block.</summary>
    [Fact]
    public void CaptureBuildsCameraRelativeInputs()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var worldData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        var entity = new EntityPlayer();
        entity.Pos.SetPos(0, 123, 0);
        typeof(ClientWorldPlayerData).GetField("entityplayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(worldData, entity);
        typeof(ClientPlayer).GetField("worlddata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, worldData);
        api.SetupGet(value => value.World.Player).Returns(player);
        api.SetupGet(value => value.World.SeaLevel).Returns(100);
        float[] projection = Elements(Perspective(1.2f, 1.7f));
        Matrix4x4 camera = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateRotationX(-.3f);
        camera.Translation = new Vector3(12, -7, 99);
        float[] view = Elements(camera);
        float[] originalView = (float[])view.Clone();
        api.SetupGet(value => value.Render.CurrentProjectionMatrix).Returns(projection);
        api.SetupGet(value => value.Render.CurrentModelviewMatrix).Returns(view);
        api.SetupGet(value => value.Render.FrameWidth).Returns(1280);
        api.SetupGet(value => value.Render.FrameHeight).Returns(720);
        var uniforms = new DefaultShaderUniforms
        {
            FogWaveCounter = 7, ZNear = .1f, ZFar = 1000,
            WaterMurkColor = new Vec4f(.2f, .3f, .4f, .9f), CameraUnderwater = .8f,
            WindWaveCounter = 2, PsychedelicStrength = .25f, NightVisionStrength = .5f,
            FlagFogDensity = -.01f, FlatFogStartYPos = 4, PlayerPos = new Vec3f(0, 3, 0)
        };
        typeof(DefaultShaderUniforms).GetField("SkyDaylight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(uniforms, .4f);
        api.SetupGet(value => value.Render.ShaderUniforms).Returns(uniforms);
        var lighting = new AtmosphereLighting(Vector3.UnitY, Vector3.Zero, Vector3.Zero,
            Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty) { HorizonElevation = -.2f };
        using var inputs = new AtmosphereSkyInputs();
        inputs.Capture(api.Object, lighting, true);
        float[] actual = MemoryMarshal.Cast<byte, float>(inputs.Bytes).ToArray();
        Assert.Equal(80, inputs.SizeBytes);
        Assert.Equal(originalView, view);
        view[12] = -345; view[13] = 900; view[14] = 12;
        inputs.Capture(api.Object, lighting, true);
        Assert.Equal(actual, MemoryMarshal.Cast<byte, float>(inputs.Bytes).ToArray());
        Assert.Equal(new float[] { 0, 1, 0, -.2f }, actual[..4]);
        Assert.Equal(23, actual[10]);
        Assert.Equal(7, actual[11]);
        Assert.Equal(new float[] { .2f, .3f, .4f, .8f }, actual[12..16]);
        Assert.Equal(new float[] { .5f, .25f, 10, 1 }, actual[16..20]);
    }

    #endregion
    #region Color and effects
    /// <summary>Retains radiance above one in linear mode and resolves and dithers exactly once in legacy mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BinaryFullscreenSkySelectsColorConvention(bool linear)
    {
        EnsureShaderTestAvailable();
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(8, 4, 2));
        float[] actual = sky.Draw(Vector3.UnitZ, linear);
        for (int channel = 0; channel < 3; channel++)
        {
            float radiance = 8f / (1 << channel);
            float expected = linear ? radiance : Display(radiance, 8);
            Assert.InRange(actual[channel], expected - .00002f, expected + .00002f);
        }
        Assert.Equal(1, actual[3]);
        Assert.Equal(new float[] { 0, 0, 0, 1 }, sky.Target[1].ReadPixels());
        foreach (int attachment in new[] { 2, 3, 4, 5, 7 })
            Assert.All(sky.Target[attachment].ReadPixels(), value => Assert.Equal((float)attachment, value));
        using (StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, sky.Target[6].TextureId))
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
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(2, 1, .5f));
        float[] actual = sky.Draw(Vector3.UnitZ, true, daylight: daylight);
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
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(2, 1, .5f));
        float[] actual = sky.Draw(Vector3.UnitY, linear, daylight: .2f, fogMin: .25f);
        Assert.InRange(actual[3], .39999f, .40001f);
    }

    /// <summary>Uses virtual sky distance and projected effect depth rather than the fullscreen triangle's far depth.</summary>
    [Fact]
    public void FullscreenPreservesVirtualFlatFogDepth()
    {
        EnsureShaderTestAvailable();
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(Vector3.One);
        Matrix4x4 projection = Perspective(1.2f, 1);
        projection.M33 = -1100f / 900; projection.M43 = -200000f / 900;
        Matrix4x4 transform = Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ) * projection;
        float[] actual = sky.Draw(Vector3.UnitY, true, daylight: 0, transform: transform, flatFogDensity: -.001f, flatFogStart: 300);
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
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework, liquidDepth: 0);
        sky.PublishConstant(new(2, 1, .5f));
        float[] actual = sky.Draw(Vector3.UnitZ, linear, underwater: underwater ? 1 : 0, nightVision: .5f);
        float murk = underwater ? 0 : 1 - .1f / 100;
        float[] tint = [.2f, .1f, .05f];
        float[] night = [.05f, .25f, .05f];
        for (int channel = 0; channel < 3; channel++)
        {
            float radiance = 2f / (1 << channel);
            float baseColor = linear ? radiance : Encode(radiance / 3);
            float expected = baseColor * (1 - murk) + (linear ? Decode(tint[channel]) : tint[channel]) * murk
                + (linear ? Decode(night[channel]) : night[channel]);
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
        using var sky = new FullscreenSky(Programs.Create<AtmosphereSkyShaderProgram>(), TestFramework);
        sky.PublishConstant(new(.7f, .3f, .1f));
        Vector3 direction = Vector3.Normalize(new Vector3(.3f, .7f, .4f));
        float[] original = sky.Draw(direction, linear, daylight: .4f);
        float[] affected = sky.Draw(direction, linear, daylight: .4f, psychedelic: .75f);
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
        float[] actual = sky.Draw(-Vector3.UnitZ, true, sun: sun, transform: view * projection);
        // Analytic perspective rays avoid reproducing the shader's inverse-projection algorithm.
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float nx = 2f * (x + .5f) / width - 1, ny = 2f * (y + .5f) / height - 1;
            Vector3 cameraRay = orthographic ? -Vector3.UnitZ : new((nx + projection.M31) / projection.M11, (ny + projection.M32) / projection.M22, -1);
            Vector3 ray = Vector3.Normalize(Vector3.TransformNormal(cameraRay, inverseView));
            float expected = .2f + .05f * AtmosphereMieTransport.Factor(Vector3.Dot(ray, sun));
            int i = (y * width + x) * 4;
            for (int channel = 0; channel < 3; channel++) Assert.InRange(MathF.Abs(actual[i + channel] - expected), 0, .0001f);
            Assert.Equal(1, actual[i + 3]);
        }
    }

    /// <summary>Tracks the published non-axis-aligned sun instead of leaving the Mie lobe at a fixed pole.</summary>
    [Fact]
    public void BinaryLookupUsesPublishedMieDirection()
    {
        EnsureShaderTestAvailable();
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
        Vector3 away = Vector3.Normalize(sun + Vector3.Normalize(Vector3.Cross(sun, Vector3.UnitY)) * MathF.Tan(MathF.PI / 6));
        float[] center = sky.Draw(sun, true, sun: sun);
        float[] offset = sky.Draw(away, true, sun: sun);
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
        foreach (float v in new[] { 0f, .25f, .499f, .5f, .501f, .75f, 1f })
        foreach (float azimuth in new[] { -.00001f, 0f, .00001f })
        {
            float elevation = AtmosphereSkyMapping.Elevation(v, horizon);
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            float[] actual = sky.Draw(direction, linear, horizon: horizon);
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

    /// <summary>Owns a controlled triangle while using the production executable, texture publication and block layout.</summary>
    private sealed class FullscreenSky : IDisposable
    {
        private readonly AtmosphereSkyShaderProgram program;
        private readonly AtmosphereModSystem atmosphere = new();
        private readonly FixtureUniformInputs inputs = new(80);
        private readonly VgeFrameUniformBuffer cameraInputs = new();
        private readonly GpuVao vao = GpuVao.Create();

        private readonly DynamicTexture2D depth;
        private readonly int width, height;
        internal GpuFramebuffer Target { get; }

        #region Public API
        /// <summary>Allocates all eight production outputs with integer patch metadata and controlled liquid depth.</summary>
        internal FullscreenSky(AtmosphereSkyShaderProgram program, ShaderTestFramework framework, float liquidDepth = 1, int width = 1, int height = 1)
        {
            this.width = width; this.height = height;
            this.program = program;
            Target = framework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
                PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
                PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32ui, PixelInternalFormat.Rgba32f);
            depth = framework.CreateTexture(width, height, PixelInternalFormat.R32f, Enumerable.Repeat(liquidDepth, width * height).ToArray());
        }
        /// <summary>Publishes constant radiance without a directional Mie contribution.</summary>
        internal void PublishConstant(Vector3 radiance)
        {
            float[] pixels = new float[4 * 2 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            { pixels[i] = radiance.X; pixels[i + 1] = radiance.Y; pixels[i + 2] = radiance.Z; pixels[i + 3] = 1; }
            Publish(pixels, new float[pixels.Length], 4, 2, 0);
        }
        /// <summary>Uses the real atmosphere owner to publish its split lookup and sampler policy.</summary>
        internal void Publish(float[] pixels, float[] mie, int width, int height, float horizon, Vector3? sun = null)
        {
            atmosphere.Publish(new(sun ?? Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.CreateRange(pixels))
                { Width = width, Height = height, HorizonElevation = horizon, SkyMie = ImmutableArray.CreateRange(mie) });
        }
        /// <summary>Centers the requested direction under the inverse view-projection transform and captures a single fragment.</summary>
        internal float[] Draw(Vector3 direction, bool linear, float daylight = 1, float underwater = 0, float nightVision = 0, float horizon = 0, Vector3? sun = null, float psychedelic = 0, float fogMin = 0, Matrix4x4? transform = null, float flatFogDensity = 0, float flatFogStart = 0)
        {
            Vector3 up = MathF.Abs(direction.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY;
            Matrix4x4 viewProjection = transform ?? Matrix4x4.CreateLookAt(Vector3.Zero, direction, up) * Perspective(1.2f, 1);
            Assert.True(Matrix4x4.Invert(viewProjection, out var inverse));
            float[] identity = Elements(Matrix4x4.Identity);
            cameraInputs.Capture(Elements(viewProjection), identity, Elements(inverse), identity,
                Elements(viewProjection), Elements(viewProjection), new(width, height), 0, 0,
                Vector3.Zero, Vector3.Zero, 0, new(.1f, 100));
            program.FrameInputs = cameraInputs;
            Vector3 source = sun ?? Vector3.UnitY;
            inputs.Vector(0, source.X, source.Y, source.Z); inputs.Float(12, horizon);
            inputs.Vector(16, 0, fogMin, flatFogDensity); inputs.Float(28, flatFogStart);
            inputs.Vector(32, daylight, 0, 0); inputs.Float(44, 0);
            inputs.Vector(48, .5f, .25f, .125f); inputs.Float(60, underwater);
            inputs.Vector(64, nightVision, psychedelic, 0); inputs.Float(76, linear ? 1 : 0);
            program.SkyLookup = AtmosphereModSystem.SkyTextureId;
            program.LiquidDepth = depth.TextureId;
            Target.BindWithViewport();
            // Metadata belongs to other passes; preserve recognizable contents while drawing the sky.
            GL.DrawBuffers(8, Enumerable.Range(0, 8).Select(i => DrawBuffersEnum.ColorAttachment0 + i).ToArray());
            for (int attachment = 2; attachment < 8; attachment++)
            {
                if (attachment == 6) GL.ClearBuffer(ClearBuffer.Color, attachment, new uint[] { 6, 6, 6, 6 });
                else GL.ClearBuffer(ClearBuffer.Color, attachment, Enumerable.Repeat((float)attachment, 4).ToArray());
            }
            GL.DrawBuffers(2, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 });
            StateCache.Current.Apply(new GlPipelineDesc(defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable).With(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.CullFaceEnable).With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask), nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthWriteMask), depthWriteMask: false));
            using (program.UseScope())
            using (vao.BindScope())
            {
                inputs.Publish();
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            return Target[0].ReadPixels();
        }
        /// <summary>Retires the test publication and geometry before their shared context closes.</summary>
        public void Dispose()
        { inputs.Dispose(); cameraInputs.Dispose(); vao.Dispose(); atmosphere.Dispose(); }
        #endregion
    }
    #endregion
}
