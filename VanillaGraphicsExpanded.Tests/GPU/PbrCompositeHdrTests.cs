using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Constrains lighting composition before the separate display resolve.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrCompositeHdrTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared graphics context.</summary>
    public PbrCompositeHdrTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Scene-linear composition
    /// <summary>Radiance remains linear through composition, physical aerial transport, enclosure gating and decoded engine fog.</summary>
    [Theory]
    [InlineData(0f, false, 1f, false)]
    [InlineData(0f, false, 1f, true)]
    [InlineData(0.5f, false, 1f, false)]
    [InlineData(0.5f, false, 1f, true)]
    [InlineData(0f, true, 1f, false)]
    [InlineData(0f, true, 1f, true)]
    [InlineData(0f, true, 0f, false)]
    [InlineData(0f, true, 0f, true)]
    public void CompositePreservesHdrAndMixesLinearFog(float fog, bool atmosphere, float skyVisibility, bool explicitPosition)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRCompositeShaderProgram>(p =>
        {
            p.LumOnEnabled = false; p.EnablePbrComposite = false; p.EnableShortRangeAo = false;
        });
        using var direct = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 2f, 1f, .5f, 1f });
        using var specular = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .5f, .25f, .125f, 1f });
        using var emission = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 4f, 2f, 1f, 1f });
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { explicitPosition ? .2f : .75f });
        using var unused = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .1f, .2f, .3f, 1f });
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .5f, 1f, .5f, explicitPosition ? -1f : 1f });
        using var position = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 0f, 0f, 1000f, 1f });
        using var environment = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] {0f,0f,0f,skyVisibility});
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var atmosphereOwner = new AtmosphereModSystem();
        float[] scatter = atmosphere ? [.2f,.3f,.4f,1f] : [0f,0f,0f,1f];
        float[] loss = atmosphere ? [.5f,.6f,.7f,1f] : [0f,0f,0f,1f];
        var snapshot = new AtmosphereLighting(Vector3.UnitY, Vector3.One, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.Create(0f,0f,0f,1f))
        { Width=1, Height=1, AerialRadiance=ImmutableArray.CreateRange(Enumerable.Range(0,24).SelectMany(_=>scatter)), AerialAttenuation=ImmutableArray.CreateRange(Enumerable.Range(0,24).SelectMany(_=>loss)) };
        atmosphereOwner.Publish(snapshot);
        output.BindWithViewport();
        {
            program.DirectDiffuse = direct; program.DirectSpecular = specular; program.Emissive = emission;
            program.IndirectDiffuse = unused; program.GBufferAlbedo = unused.TextureId;
            program.GBufferMaterial = unused; program.GBufferNormal = normal;
            program.GBufferPosition = position.TextureId;
            program.PrimaryDepth = depth.TextureId;
            program.GBufferEnvironment = environment;
            program.InvProjectionMatrix = [1,0,0,0, 0,1,0,0, 0,0,2000,0, 0,0,0,1];
            program.SetAtmosphere(snapshot);
            // Publish the complete frame state, including the absence of a water-volume capture.
            program.SetWaterVolume(null);
            program.SetUnderwater(fog > 0);
            program.ViewMatrix = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
            program.FogDensityIn = fog > 0 ? .0002f : 0; program.FogMinIn = fog; program.RgbaFogIn = new(.5f, .25f, .125f, 1);
            TestFramework.RenderQuadTo(program, output);
        }
        var actual = output[0].ReadPixels();
        float[] light = [6.5f, 3.25f, 1.625f];
        float[] fogColor = [.5f, .25f, .125f];
        float fogAmount = fog > 0 ? fog + 1f - MathF.Exp(-1000f * .0002f) : 0f;
        for (int channel = 0; channel < 3; channel++)
        {
            float transmission = 1 - loss[channel] * skyVisibility;
            float expected = (light[channel] * transmission + scatter[channel] * skyVisibility) * (1 - fogAmount) + Linear(fogColor[channel]) * fogAmount;
            Assert.InRange(actual[channel], expected - .004f, expected + .004f);
        }
        Assert.True(actual[0] > 1);
    }
    #endregion

    #region Water composition
    /// <summary>Preserves channel-dependent water transmission before display, in both lighting modes, without legacy underwater fog.</summary>
    [Theory]
    [InlineData(false, false, 1f, true)]
    [InlineData(true, false, 1f, true)]
    [InlineData(false, true, 1f, true)]
    [InlineData(true, true, 1f, true)]
    [InlineData(false, true, 0f, true)]
    [InlineData(true, true, 0f, true)]
    [InlineData(false, true, 1f, false)]
    [InlineData(true, true, 1f, false)]
    [InlineData(false, false, 1f, true, true, true)]
    [InlineData(true, false, 1f, true, true, true)]
    [InlineData(false, true, 1f, true, true, true)]
    [InlineData(true, true, 1f, true, true, true)]
    [InlineData(false, true, 1f, true, true, false)]
    [InlineData(true, true, 1f, true, true, false)]
    [InlineData(false, false, 1f, true, false, true, true)]
    [InlineData(true, false, 1f, true, false, true, true)]
    public void WaterTransmissionStaysRgbAndSceneLinear(bool lumon, bool underwater, float density, bool mappedCamera, bool sky = false, bool knownExit = true, bool heldOverlay = false)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRCompositeShaderProgram>(p => {
            p.LumOnEnabled = lumon; p.EnablePbrComposite = false; p.EnableShortRangeAo = false;
        });
        using var direct = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [6f, 3f, 2f, 1f]);
        using var zero = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [0f, 0f, 0f, 0f]);
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [sky ? 1f : .75f]);
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.5f, 1f, .5f, 1f]);
        // Sky reconstruction reaches 20 metres; an exit at three metres subtracts the remaining 17.
        float signedLength = underwater ? (knownExit ? (sky ? -17 : -7) : 0) : 2;
        var medium = new WaterMedium(new Vector3(.1f, .2f, .3f), Vector3.Zero, density, 0);
        var extinction = medium.AbsorptionPerMetre;
        using var optical = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f,
            [extinction.X * signedLength, extinction.Y * signedLength, extinction.Z * signedLength, signedLength]);
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [0f, 0f, 0f, underwater && knownExit ? -1f : 0f]);
        using var output = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.R32f);
        using var atmosphereOwner = new AtmosphereModSystem();
        var snapshot = new AtmosphereLighting(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            ImmutableArray.Create(0f, 0f, 0f, 1f)) { Width = 1, Height = 1,
            AerialRadiance = ImmutableArray.CreateRange(new float[96]),
            AerialAttenuation = ImmutableArray.CreateRange(new float[96]) };
        atmosphereOwner.Publish(snapshot);
        output.BindWithViewport();
        program.DirectDiffuse = direct; program.DirectSpecular = zero; program.Emissive = zero;
        program.IndirectDiffuse = zero; program.GBufferAlbedo = sky ? direct.TextureId : zero.TextureId; program.GBufferMaterial = zero;
        program.GBufferNormal = normal; program.GBufferPosition = zero.TextureId;
        program.GBufferEnvironment = zero; program.PrimaryDepth = depth.TextureId;
        program.InvProjectionMatrix = [1,0,0,0, 0,1,0,0, 0,0,20,0, 0,0,0,1];
        program.ViewMatrix = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        program.SetAtmosphere(snapshot);
        program.SetUnderwater(underwater);
        program.RefractionSourceEnabled = true;
        program.FogDensityIn = 10; program.FogMinIn = 1; program.RgbaFogIn = new(1, 1, 1, 1);
        program.SetWaterVolume(new WaterVolumeFrame(optical, source, underwater && mappedCamera ? medium : null));
        var cpuParameters = ((IPBRCompositeShaderProgramBindings)program).Parameters.Bytes.ToArray();
        Assert.Equal(272, cpuParameters.Length);
        Assert.Equal(1f, BitConverter.ToSingle(cpuParameters, 236));
        Assert.Equal(underwater && mappedCamera ? 1f : 0f, BitConverter.ToSingle(cpuParameters, 252));
        TestFramework.RenderQuadTo(program, output);
        if (heldOverlay)
        {
            // The capture policy leaves the opaque world capture intact before first-person overlay writes.
            Assert.True(VanillaGraphicsExpanded.PBR.Liquids.WaterRefractionCapture.ShouldCapture(true,
                Vintagestory.API.Client.EnumRenderStage.Opaque, false, true, Vintagestory.GameContent.RenderMode.FirstPerson));
            depth.UploadDataImmediate([.01f]);
            normal.UploadDataImmediate([.5f, 1f, .5f, -1f]);
            direct.UploadDataImmediate([100f, 20f, 10f, 1f]);
            Assert.Equal(.01f, depth.ReadPixels()[0]);
        }
        var actual = output[0].ReadPixels();
        var rawSource = output[1].ReadPixels();
        var sourceDepth = output[2].ReadPixels();
        // The published source bypasses straight-path medium transport and retains its matching hardware depth.
        Assert.Equal(sky ? 0f : 1f, rawSource[3]);
        Assert.InRange(MathF.Abs(sourceDepth[0] - (sky ? 1f : .75f)), 0, .000001f);
        if (!sky)
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(MathF.Abs(rawSource[channel] - new float[] { 6f, 3f, 2f }[channel]), 0, .0001f);
        float distance = underwater ? 3 : 2;
        float[] coefficients = [extinction.X, extinction.Y, extinction.Z];
        float[] background = [6, 3, 2];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = sky && !knownExit ? background[channel]
                : underwater && !mappedCamera ? 1 : background[channel] * MathF.Exp(-coefficients[channel] * distance);
            Assert.InRange(actual[channel], expected - .0001f, expected + .0001f);
        }
        if (!underwater || mappedCamera) Assert.True(actual[0] > 1); // No extra display boundary.
    }
    #endregion

    #region Transfer reference
    /// <summary>Decodes the documented sRGB fog input independently.</summary>
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    #endregion
}
