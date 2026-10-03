using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks clean world restoration in the production composite without altering first-person presentation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionOverlayCompositionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Restores only overlay pixels, retaining invalid metadata when no clean world capture exists.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void CleanCaptureRestoresWorldBehindFirstPersonPixels(bool lumon, bool overlay, bool captured)
    {
        EnsureShaderTestAvailable();
        using var fixedFunction = StateCache.Current.CaptureLegacyFixedFunctionState();
        var program = Programs.Create<PBRCompositeShaderProgram>(value =>
        {
            value.LumOnEnabled = lumon;
            value.EnablePbrComposite = false;
            value.EnableShortRangeAo = false;
        });
        using var direct = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [6f, 3f, 2f, 1f]);
        using var zero = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [0f, 0f, 0f, 0f]);
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [overlay ? .01f : .8f]);
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.5f, 1f, .5f, overlay ? -1f : 1f]);
        using var position = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [0f, 0f, -.1f, 1f]);
        using var cleanColor = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var cleanDepth = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f);
        cleanColor.UploadDataImmediate([2f, 1f, .5f, 1f]);
        cleanDepth.UploadDataImmediate([.75f]);
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.R32f);
        using var atmosphere = new AtmosphereModSystem();
        var snapshot = new AtmosphereLighting(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            ImmutableArray.Create(0f, 0f, 0f, 1f)) { Width = 1, Height = 1,
            AerialRadiance = ImmutableArray.CreateRange(new float[96]),
            AerialAttenuation = ImmutableArray.CreateRange(new float[96]) };
        atmosphere.Publish(snapshot);
        program.DirectDiffuse = direct;
        program.DirectSpecular = zero;
        program.Emissive = zero;
        program.IndirectDiffuse = zero;
        program.GBufferAlbedo = zero.TextureId;
        program.GBufferMaterial = zero;
        program.GBufferNormal = normal;
        program.GBufferPosition = position.TextureId;
        program.GBufferEnvironment = zero;
        program.PrimaryDepth = depth.TextureId;
        program.InvProjectionMatrix = [1,0,0,0, 0,1,0,0, 0,0,20,0, 0,0,0,1];
        program.ViewMatrix = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        program.SetAtmosphere(snapshot);
        program.SetWaterVolume(null);
        program.SetUnderwater(false);
        program.RefractionSourceEnabled = true;
        program.PreOverlaySourceEnabled = captured;
        // Missing capture deliberately leaves optional samplers absent; the availability guard must prevent fetches.
        program.PreOverlayColor = captured ? cleanColor : null;
        program.PreOverlayDepth = captured ? cleanDepth : null;
        program.FogDensityIn = 0;
        program.FogMinIn = 0;
        program.RgbaFogIn = new(0, 0, 0, 0);
        TestFramework.RenderQuadTo(program, target);
        float[] presentation = target[0].ReadPixels();
        float[] source = target[1].ReadPixels();
        float[] sourceDepth = target[2].ReadPixels();
        bool restored = overlay && captured;
        float[] expected = restored ? [2f, 1f, .5f] : [6f, 3f, 2f];
        // The visible first-person composite retains its own lighting; only the immutable refraction pair is restored.
        for (int channel = 0; channel < 3; ++channel)
        {
            Assert.InRange(MathF.Abs(presentation[channel] - new float[] { 6f, 3f, 2f }[channel]), 0, .0001f);
            Assert.InRange(MathF.Abs(source[channel] - expected[channel]), 0, .0001f);
        }
        Assert.Equal(overlay && !captured ? 0f : 1f, source[3]);
        Assert.InRange(MathF.Abs(sourceDepth[0] - (restored ? .75f : overlay ? .01f : .8f)), 0, .000001f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        // The fixture's raw texture helpers do not own VGE's resource-slot cache.
        StateCache.Current.InvalidateAll();
    }

    /// <summary>Capture eligibility excludes shadows, other players, body modes and unrelated stages.</summary>
    [Fact]
    public void CapturePolicyPreservesAllEngineDrawPaths()
    {
        foreach (bool enabled in new[] { false, true })
        foreach (bool self in new[] { false, true })
        foreach (bool shadow in new[] { false, true })
        foreach (EnumRenderStage stage in Enum.GetValues<EnumRenderStage>())
        foreach (RenderMode mode in Enum.GetValues<RenderMode>())
            Assert.Equal(enabled && self && !shadow && stage == EnumRenderStage.Opaque && mode == RenderMode.FirstPerson,
                WaterRefractionCapture.ShouldCapture(enabled, stage, shadow, self, mode));
    }
    #endregion
}
