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
    /// <summary>Uses the shared headless graphics context.</summary>
    public PbrDirectLightingShadowTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Visibility contracts

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

        Assert.True(lit[0] > .4f && lit[4] > .001f, "The control receiver must have diffuse and specular sunlight.");
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

    /// <summary>Partial engine shadow intensity scales sunlight while preserving its intentional fade.</summary>
    [Fact]
    public void HalfShadowIntensityRetainsHalfSunlight()
    {
        EnsureShaderTestAvailable();
        var lit = RenderReceiver(1f, 100f, 0f);
        var halfShadow = RenderReceiver(0f, 100f, 0f, intensity: .5f);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(MathF.Abs(lit[channel] * .5f - halfShadow[channel]), 0f, .0001f);
            Assert.InRange(MathF.Abs(lit[4 + channel] * .5f - halfShadow[4 + channel]), 0f, .0001f);
        }
    }

    #endregion

    #region Receiver rendering

    /// <summary>Renders a front-facing receiver with deterministic cascade coverage and reads all radiance buffers.</summary>
    private float[] RenderReceiver(float shadowDepth, float nearRange, float farRange,
        float sunlight = 1f, bool pointLight = false, float emission = 0f, float intensity = 1f)
    {
        var program = Programs.Create<PBRDirectLightingShaderProgram>();
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f, 3);
        using var albedo = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, [.6f, .6f, .6f, 1f]);
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [0f]);
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, [.5f, .5f, 1f, 1f]);
        using var material = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, [.4f, 0f, emission, 1f]);
        // A depth-format texture is required by sampler2DShadow. The production resource setters
        // bind the comparison sampler; the test must not repair or replace that binding itself.
        using var shadow = DynamicTexture2D.CreateDepth(1, 1, PixelInternalFormat.DepthComponent32f);
        GL.TextureSubImage2D(shadow.TextureId, 0, 0, 0, 1, 1, PixelFormat.DepthComponent, PixelType.Float, new[] { shadowDepth });
        using var use = program.UseScope();
        program.PrimaryScene = albedo.TextureId;
        program.PrimaryDepth = depth.TextureId;
        program.GBufferNormal = normal.TextureId;
        program.GBufferMaterial = material.TextureId;
        program.ShadowMapNear = shadow.TextureId;
        program.ShadowMapFar = shadow.TextureId;
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        program.InvProjectionMatrix = identity;
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
        program.LightDirection = new(0f, 0f, 1f);
        program.RgbaLightIn = new(sunlight, sunlight, sunlight);
        program.RgbaAmbientIn = new(0f, 0f, 0f);
        program.SetPointLights(pointLight ? 1 : 0, pointLight ? [0f, 0f, 0f] : null, pointLight ? [1f, 1f, 1f] : null);
        output.BindWithViewport();
        TestFramework.RenderQuad(program);

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


