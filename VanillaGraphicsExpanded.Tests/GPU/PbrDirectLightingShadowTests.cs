using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks direct sunlight visibility using depth comparisons and production sampler bindings.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrDirectLightingShadowTests : LumOnShaderFunctionalTestBase
{
    #region Scenario resources

    // Each test owns one specialization through Programs; receiver inputs are rebound for every draw.
    private PBRDirectLightingShaderProgram? receiverProgram;

    #endregion

    /// <summary>Uses the shared headless graphics context.</summary>
    public PbrDirectLightingShadowTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Visibility contracts

    /// <summary>Propagated sunlight scales both solar lobes and preserves local light and emission.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void PropagatedSunlightScalesOnlySolarLighting(float skyVisibility)
    {
        EnsureShaderTestAvailable();
        var sun = RenderReceiver(1f, 100f, 100f);
        var local = RenderReceiver(1f, 100f, 100f, sunlight: 0f, pointLight: true, emission: .3f);
        var mixed = RenderReceiver(1f, 100f, 100f, pointLight: true, emission: .3f, skyVisibility: skyVisibility);
        Assert.True(sun[0] > .01f && sun[4] > .01f);
        float factor = Math.Clamp(skyVisibility, 0f, 1f);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(MathF.Abs(mixed[channel] - (local[channel] + sun[channel] * factor)), 0f, .002f);
            Assert.InRange(MathF.Abs(mixed[4 + channel] - (local[4 + channel] + sun[4 + channel] * factor)), 0f, .002f);
            Assert.Equal(local[8 + channel], mixed[8 + channel]);
        }
    }

    /// <summary>Full near coverage shadows terrain-like receivers even when the far cascade is completely clear.</summary>
    [Theory]
    [InlineData(.2f, .5f, .1f, true)]
    [InlineData(.4f, .2f, .1f, true)]
    [InlineData(.4f, .2f, .1f, false)]
    public void NearOcclusionOverridesClearFarCascade(float red, float green, float blue, bool upward)
    {
        EnsureShaderTestAvailable();
        float[] color = [red, green, blue, 1f];
        var lit = RenderReceiver(1f, 100f, 100f, farDepth: 1f, color: color, upward: upward);
        var shadowed = RenderReceiver(0f, 100f, 100f, farDepth: 1f, color: color, upward: upward);
        Assert.True(lit[0] > .01f && lit[1] > .01f && lit[2] > .01f);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(shadowed[channel], 0f, .0001f);
            Assert.InRange(shadowed[4 + channel], 0f, .0001f);
        }
    }

    /// <summary>Complete occlusion removes both sun lobes throughout near, far, and overlapping coverage.</summary>
    [Theory]
    [InlineData(100f, 0f)]
    [InlineData(0f, 100f)]
    [InlineData(2f, 100f)]
    public void FullyOccludedSunProducesNoDirectRadiance(float nearRange, float farRange)
    {
        EnsureShaderTestAvailable();
        var lit = RenderReceiver(1f, nearRange, farRange);
        var shadowed = RenderReceiver(0f, nearRange, farRange);

        Assert.True(lit[0] > .4f / MathF.PI && lit[4] > .001f, "The control receiver must have diffuse and specular sunlight.");
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(shadowed[channel], 0f, .0001f);
            Assert.InRange(shadowed[4 + channel], 0f, .0001f);
        }
    }

    /// <summary>Sun occlusion leaves a held point light and material emission unchanged.</summary>
    [Fact]
    public void SunOcclusionPreservesPointLightingAndEmission()
    {
        EnsureShaderTestAvailable();
        var pointOnly = RenderReceiver(1f, 100f, 0f, sunlight: 0f, pointLight: true, emission: .8f);
        var shadowedSunAndPoint = RenderReceiver(0f, 100f, 0f, pointLight: true, emission: .8f);

        Assert.True(pointOnly[0] > .4f && pointOnly[4] > .001f && pointOnly[8] > .3f);
        for (int attachment = 0; attachment < 3; attachment++)
        for (int channel = 0; channel < 3; channel++)
        {
            int index = attachment * 4 + channel;
            Assert.InRange(MathF.Abs(pointOnly[index] - shadowedSunAndPoint[index]), 0f, .0001f);
        }
    }

    /// <summary>Disabling shadow intensity retains direct sunlight even behind the depth occluder.</summary>
    [Fact]
    public void DisabledShadowsPreserveSunlight()
    {
        EnsureShaderTestAvailable();
        var lit = RenderReceiver(1f, 100f, 0f);
        var disabled = RenderReceiver(0f, 100f, 0f, intensity: 0f);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(MathF.Abs(lit[channel] - disabled[channel]), 0f, .0001f);
            Assert.InRange(MathF.Abs(lit[4 + channel] - disabled[4 + channel]), 0f, .0001f);
        }
    }

    /// <summary>Shadow intensity fades diffuse sunlight but does not weaken full PCF occlusion for specular.</summary>
    [Fact]
    public void HalfShadowIntensityRetainsHalfDiffuseAndRemovesSpecular()
    {
        EnsureShaderTestAvailable();
        var lit = RenderReceiver(1f, 100f, 0f);
        var halfShadow = RenderReceiver(0f, 100f, 0f, intensity: .5f);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(MathF.Abs(lit[channel] * .5f - halfShadow[channel]), 0f, .0001f);
            Assert.InRange(halfShadow[4 + channel], 0f, .0001f);
        }
    }

    #endregion

    #region Explicit receiver position

    /// <summary>First-person lighting uses the physical receiver position despite visibility-depth and projection changes.</summary>
    [Theory]
    [InlineData(0f, 1f, false)]
    [InlineData(1f, 1f, false)]
    [InlineData(1f, 0f, true)]
    public void ExplicitReceiverPositionPreservesSunShadowsAndPointLighting(float shadowDepth, float sunlight, bool pointLight)
    {
        EnsureShaderTestAvailable();
        var baseline = RenderReceiver(shadowDepth, 100f, 0f, sunlight: sunlight, pointLight: pointLight);
        var firstPerson = RenderReceiver(shadowDepth, 100f, 0f, sunlight: sunlight, pointLight: pointLight,
            explicitPosition: true, visibilityDepth: .4f, projectionOffset: 2f);
        // Compare both diffuse and specular lobes, including a completely shadowed receiver.
        for (int attachment = 0; attachment < 2; attachment++)
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(MathF.Abs(baseline[attachment * 4 + channel] - firstPerson[attachment * 4 + channel]), 0f, .0001f);

        if (shadowDepth == 0f)
            Assert.InRange(firstPerson[0], 0f, .0001f);
        else
            Assert.True(firstPerson[0] > .01f && firstPerson[4] > .001f);
    }

    /// <summary>Ordinary receivers continue reconstructing depth and do not consume the position attachment.</summary>
    [Fact]
    public void UnmarkedReceiverRetainsDepthReconstruction()
    {
        EnsureShaderTestAvailable();
        var baseline = RenderReceiver(1f, 100f, 0f, sunlight: 0f, pointLight: true);
        var changed = RenderReceiver(1f, 100f, 0f, sunlight: 0f, pointLight: true,
            visibilityDepth: .4f, projectionOffset: 2f);
        Assert.True(MathF.Abs(baseline[0] - changed[0]) > .05f,
            "An unmarked receiver must still respond to reconstructed position changes.");
    }
    #endregion

    #region Receiver rendering

    /// <summary>Renders a front-facing receiver with deterministic cascade coverage and reads all radiance buffers.</summary>
    private float[] RenderReceiver(float shadowDepth, float nearRange, float farRange,
        float sunlight = 1f, bool pointLight = false, float emission = 0f, float intensity = 1f,
        float? farDepth = null, float[]? color = null, bool upward = false,
        bool explicitPosition = false, float visibilityDepth = 0f, float projectionOffset = 0f, float skyVisibility = 1f)
    {
        var program = receiverProgram ??= Programs.Create<PBRDirectLightingShaderProgram>();
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f, 3);
        using var albedo = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, color ?? [.6f, .6f, .6f, 1f]);
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [visibilityDepth]);
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, upward ? [.5f, 1f, .5f, 1f] : [.5f, .5f, 1f, explicitPosition ? -1f : 1f]);
        using var position = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, [0f, 0f, -1f, 1f]);
        using var environment = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, [0f, 0f, 0f, skyVisibility]);
        using var material = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, [color is null ? .4f : .9f, 0f, emission, 1f]);
        // A depth-format texture is required by sampler2DShadow. The production resource setters
        // bind the comparison sampler; the test must not repair or replace that binding itself.
        using var shadow = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        GL.TextureSubImage2D(shadow.TextureId, 0, 0, 0, 1, 1, PixelFormat.DepthComponent, PixelType.Float, new[] { shadowDepth });
        using var farShadow = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        GL.TextureSubImage2D(farShadow.TextureId, 0, 0, 0, 1, 1, PixelFormat.DepthComponent, PixelType.Float, new[] { farDepth ?? shadowDepth });
        program.PrimaryScene = albedo.TextureId;
        program.PrimaryDepth = depth.TextureId;
        using var surface = LayeredTestTexture.Create(normal, material, environment);
        program.GBufferSurface = surface;
        program.GBufferPosition = position.TextureId;
        program.ShadowMapNear = shadow.TextureId;
        program.ShadowMapFar = farShadow.TextureId;
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] inverseProjection = (float[])identity.Clone();
        inverseProjection[12] = projectionOffset;
        program.InvProjectionMatrix = inverseProjection;
        program.InvModelViewMatrix = identity;
        // Center depth zero reconstructs (0,0,-1), which maps to shadow UV/depth (.5,.5,.5).
        // Its distance of one gives full near coverage at range 100, and .65 near/.35 far at range 2.
        float[] shadowMatrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, .5f, .5f, 1.5f, 1];
        program.ToShadowMapSpaceMatrixNear = shadowMatrix;
        program.ToShadowMapSpaceMatrixFar = shadowMatrix;
        program.ZPlanesAndShadowRanges = (.1f, 100f, nearRange, farRange);
        program.ShadowZExtendNear = 1f;
        program.ShadowZExtendFar = 1f;
        program.DropShadowIntensity = intensity;
        program.LightDirection = upward ? new(0f, 1f, 0f) : new(0f, 0f, 1f);
        program.RgbaLightIn = new(sunlight, sunlight, sunlight);
        program.RgbaAmbientIn = new(0f, 0f, 0f);
        program.SetPointLights(pointLight ? 1 : 0, pointLight ? [0f, 0f, 0f] : null, pointLight ? [1f, 1f, 1f] : null);
        output.BindWithViewport();
        TestFramework.RenderQuadTo(program, output);

        var result = new float[12];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, output.FboId);
        for (int attachment = 0; attachment < 3; attachment++)
        {
            GL.ReadBuffer((ReadBufferMode)((int)ReadBufferMode.ColorAttachment0 + attachment));
            float[] pixel = new float[4];
            GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.Float, pixel);
            Array.Copy(pixel, 0, result, attachment * 4, 4);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GpuFramebuffer.Unbind();
        return result;
    }

    #endregion
}
