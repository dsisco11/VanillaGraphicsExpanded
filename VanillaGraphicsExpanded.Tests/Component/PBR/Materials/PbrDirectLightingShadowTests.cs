using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using Xunit;


using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;
/// <summary>Checks direct sunlight visibility using depth comparisons and production sampler bindings.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrDirectLightingShadowTests : LumOnShaderFunctionalTestBase
{
    #region Public API
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
        // Arrange: Set controlled propagated sky visibility alongside an independent point-light contribution.
        // Act and assert: Render direct light and verify visibility scales solar lighting alone.
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
        // Arrange: Provide colored near-cascade occlusion and a clear far cascade for the selected ray direction.
        // Act and assert: Render both cascade inputs and require the near occluder to control solar visibility.
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
        // Arrange: Place the receiver behind fully occluding cascade depths with selected range metadata.
        // Act and assert: Evaluate direct lighting and require the fully occluded sun to contribute no radiance.
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
        // Arrange: Block solar visibility while retaining point illumination and material emission.
        // Act and assert: Render composition and verify occlusion removes only the sun contribution.
        EnsureShaderTestAvailable();
        var pointOnly = RenderReceiver(1f, 100f, 0f, sunlight: 0f, pointLight: true, emission: .8f);
        var shadowedSunAndPoint = RenderReceiver(0f, 100f, 0f, pointLight: true, emission: .8f);

        // The unit engine point payload is calibrated candela, evaluated one metre from this normal-incidence receiver.
        var response = MicrofacetEnergyReference.NormalResponse((float)(Half).4f, .04);
        float expectedDiffuse = (float)((float)(Half).6f * (1-response.Reflection) / Math.PI
            * LightingCalibration.EnginePointCandelaPerUnit);
        Assert.InRange(pointOnly[0], expectedDiffuse - .0001f, expectedDiffuse + .0001f);
        Assert.True(pointOnly[4] > .001f && pointOnly[8] > .3f);
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
        // Arrange: Place the receiver behind a depth occluder with shadow intensity disabled.
        // Act and assert: Render direct lighting and verify the disabled shadow policy retains sunlight.
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
        // Arrange: Configure half shadow intensity over a fully occluded solar receiver.
        // Act and assert: Verify diffuse sunlight is halved while the PCF-occluded specular lobe remains absent.
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
        // Arrange: Supply explicit receiver position with selected shadow depth, solar irradiance, and optional point light.
        // Act and assert: Render using the explicit position and compare retained shadow and point-light contributions.
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
        // Arrange: Leave receiver-position metadata unmarked and provide ordinary depth for reconstruction.
        // Act and assert: Render the receiver and verify the production path continues using reconstructed depth.
        EnsureShaderTestAvailable();
        var baseline = RenderReceiver(1f, 100f, 0f, sunlight: 0f, pointLight: true);
        var changed = RenderReceiver(1f, 100f, 0f, sunlight: 0f, pointLight: true,
            visibilityDepth: .4f, projectionOffset: 2f);
        Assert.True(MathF.Abs(baseline[0] - changed[0]) > .05f,
            "An unmarked receiver must still respond to reconstructed position changes.");
    }
    #endregion

    #endregion

    #region Private
    #region Scenario resources
    // Each test owns one specialization through Programs; receiver inputs are rebound for every draw.
    private PBRDirectLightingShaderProgram? receiverProgram;
    #endregion

    #region Receiver rendering

    /// <summary>Renders a front-facing receiver with deterministic cascade coverage and reads all radiance buffers.</summary>
    private float[] RenderReceiver(float shadowDepth, float nearRange, float farRange,
        float sunlight = 1f, bool pointLight = false, float emission = 0f, float intensity = 1f,
        float? farDepth = null, float[]? color = null, bool upward = false,
        bool explicitPosition = false, float visibilityDepth = 0f, float projectionOffset = 0f, float skyVisibility = 1f)
    {
        var program = receiverProgram ??= Programs.Create<PBRDirectLightingShaderProgram>();
        using var output = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f, 3);
        using var albedo = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, color ?? [.6f, .6f, .6f, 1f]);
        using var depth = TestFramework.CreateTexture(1, 1, TextureFormat.R32f, [visibilityDepth]);
        using var normal = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, upward ? [.5f, 1f, .5f, 1f] : [.5f, .5f, 1f, explicitPosition ? -1f : 1f]);
        using var position = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [0f, 0f, -1f, 1f]);
        using var environment = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [0f, 0f, 0f, skyVisibility]);
        using var material = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, [color is null ? .4f : .9f, 0f, emission, 0f]);
        // A depth-format texture is required by sampler2DShadow. The production resource setters
        // bind the comparison sampler; the test must not repair or replace that binding itself.
        using var shadow = new DepthTexture(1, 1, TextureFormat.DepthComponent32f);
        GL.TextureSubImage2D(shadow.TextureId, 0, 0, 0, 1, 1, PixelFormat.DepthComponent, PixelType.Float, new[] { shadowDepth });
        using var farShadow = new DepthTexture(1, 1, TextureFormat.DepthComponent32f);
        GL.TextureSubImage2D(farShadow.TextureId, 0, 0, 0, 1, 1, PixelFormat.DepthComponent, PixelType.Float, new[] { farDepth ?? shadowDepth });
        program.PrimaryScene = albedo;
        program.PrimaryDepth = depth;
        using var surface = LayeredTestTexture.Create(ImageAssertions.Programs, normal, material, environment);
        program.GBufferSurface = surface;
        program.GBufferPosition = position;
        program.ShadowMapNear = shadow;
        program.ShadowMapFar = farShadow;
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] inverseProjection = (float[])identity.Clone();
        inverseProjection[12] = projectionOffset;
        using var frameCamera = TestFrameCamera.CreateFromInverseView(inverseProjection, identity);
        program.FrameInputs = frameCamera;
        // Center depth zero reconstructs (0,0,-1), which maps to shadow UV/depth (.5,.5,.5).
        // Its distance of one gives full near coverage at range 100, and .65 near/.35 far at range 2.
        float[] shadowMatrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, .5f, .5f, 1.5f, 1];
        program.ToShadowMapSpaceMatrixNear = shadowMatrix;
        program.ToShadowMapSpaceMatrixFar = shadowMatrix;
        program.ShadowRanges = (nearRange, farRange);
        program.ShadowZExtendNear = 1f;
        program.ShadowZExtendFar = 1f;
        program.DropShadowIntensity = intensity;
        program.LightDirection = upward ? new(0f, 1f, 0f) : new(0f, 0f, 1f);
        program.RgbaLightIn = new(sunlight, sunlight, sunlight);
        program.RgbaAmbientIn = new(0f, 0f, 0f);
        using var sharedLights = new VgeLightsUniformBuffer();
        sharedLights.Capture(pointLight ? 1 : 0, pointLight ? [0f, 0f, 0f] : [], pointLight ? [1f, 1f, 1f] : []);
        program.LightsInputs = sharedLights;
        FramebufferTestSetup.BindForEngineRendering(output);
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
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
    }
    #endregion
}
