using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks shared forward lighting at shadow, emission and view-space point-light boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrForwardSurfaceNumericalTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public PbrForwardSurfaceNumericalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Public API
    /// <summary>Checks explicit solar inputs, shadow occlusion, emission, view-space points and enclosed aerial transport.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    public void ForwardBoundaryMatchesIndependentRadiance(int scenario)
    {
        EnsureContextValid();
        float[] view = scenario == 3
            ? [0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, 1]
            : [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] actual = RenderScenario(scenario, view);
        float factor = scenario is 1 or 9 or 15 ? 64f
            : scenario is 2 or 3 or 8 or 14 ? LightingCalibration.EnginePointCandelaPerUnit : 0f;
        float[,] radiance = new float[4, 3];
        var lookup = MicrofacetTextureReference.Sample(MicrofacetEnergyLookup.Prepare().Width, .5f, 1);
        double eLow = lookup.Value.X - lookup.Error.X, eHigh = lookup.Value.X + lookup.Error.X;
        double efLow = Math.Max(0, lookup.Value.Y - lookup.Error.Y), efHigh = lookup.Value.Y + lookup.Error.Y;
        double gainLow = 1 + .04 * (1 - eHigh) / eHigh, gainHigh = 1 + .04 * (1 - eLow) / eLow;
        double reflectedLow = gainLow * (eLow * .04 + efLow * .96), reflectedHigh = gainHigh * (eHigh * .04 + efHigh * .96);
        // Propagate only declared texture precision through the material-energy coefficients; physical inputs and source arithmetic stay fixed.
        var responses = new[]
        {
            (Reflection: reflectedLow, Gain: gainLow), (Reflection: reflectedLow, Gain: gainHigh),
            (Reflection: reflectedHigh, Gain: gainLow), (Reflection: reflectedHigh, Gain: gainHigh)
        };
        for (int variant = 0; variant < responses.Length; variant++)
        {
            var response = responses[variant];
            float diffuseWeight = (float)(1 - response.Reflection);
            float specularGain = (float)response.Gain;
            for (int channel = 0; channel < 3; channel++)
            {
                float irradiance = scenario == 4 ? new[] { .2f, .4f, .8f }[channel] / MathF.PI : factor;
                float linear = (channel + 1) * .2f * irradiance;
                float normalSpecular = .16f * specularGain / MathF.PI;
                if (scenario is 2 or 3 or 8 or 14)
                    linear = (diffuseWeight * (channel + 1) * .2f / MathF.PI + normalSpecular) * factor;
                if (scenario == 4)
                    linear = (diffuseWeight * (channel + 1) * .2f / MathF.PI
                        + PbrSolarBrdfTests.NormalIncidenceExpected(.04f, .5f) * specularGain)
                        * new[] { .2f, .4f, .8f }[channel];
                if (scenario == 6) linear = new[] { .2f, .4f, .8f }[channel]
                    * (float)(diffuseWeight * (channel + 1) * .2 + response.Reflection);
                if (scenario == 10) linear = (diffuseWeight * (channel + 1) * .2f / MathF.PI
                    + PbrSolarBrdfTests.NormalIncidenceExpected(.04f, .5f) * specularGain) * (.3f + channel * .2f);
                if (scenario == 12) linear = new[] { .04f, .09f, .16f }[channel];
                if (scenario >= 16)
                {
                    double lux = (scenario == 18 ? 2000 : 1000) / (4 * Math.PI * (scenario == 17 ? 4 : 1));
                    linear = (float)((diffuseWeight * (channel + 1) * .2 / Math.PI + normalSpecular) * lux);
                }
                radiance[variant, channel] = linear;
            }
        }
        // Assert: forward scene lighting remains unexposed radiance; display conversion belongs to the final pass.
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(actual[channel], Enumerable.Range(0, 4).Min(variant => radiance[variant, channel]) - .0001f,
                Enumerable.Range(0, 4).Max(variant => radiance[variant, channel]) + .0001f);
        Assert.InRange(actual[3], .36999f, .37001f);
    }
    #endregion

    #region Private
    /// <summary>Draws the declared direct-helper probe with production frame, light, texture and storage owners.</summary>
    private float[] RenderScenario(int scenario, float[] view)
    {
        using var programs = new ComponentShaderPrograms();
        string identity = scenario is >= 6 and <= 9 ? "tests/pbr-forward-surface-legacy-environment" : "tests/pbr-forward-surface";
        var program = programs.Create<PbrForwardSurfaceProbeProgram>(identity: identity);
        using var frame = new VgeFrameUniformBuffer();
        using var lights = new VgeLightsUniformBuffer();
        lights.Capture(scenario is 2 or 3 or 8 or 14 ? 1 : 0, [0, 0, 0], [4, 4, 4]);
        if (scenario >= 16)
            lights.CaptureAuthored([new(new(0, 0, scenario == 17 ? 0 : -1), Vector3.One,
                scenario == 18 ? 2000 : 1000, 100)]);

        using var framework = new ShaderTestFramework();
        using var neutralShaftOcclusion = framework.CreateTexture(1, 1, TextureFormat.R32f, [0f]);
        using var atmosphereOwner = new AtmosphereModSystem();
        float[] scatter = scenario is 12 or 13 ? [.04f, .09f, .16f, 1f] : [0f, 0f, 0f, 1f];
        atmosphereOwner.Publish(new(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            ImmutableArray.Create(0f, 0f, 0f, 1f))
        {
            Width = 1,
            Height = 1,
            AerialRadiance = ImmutableArray.CreateRange(Enumerable.Range(0, 24).SelectMany(_ => scatter)),
            AerialAttenuation = ImmutableArray.CreateRange(Enumerable.Range(0, 24).SelectMany(_ => new[] { 0f, 0f, 0f, 1f }))
        });
        var aerialRadiance = AtmosphereModSystem.AerialRadianceTexture
            ?? throw new InvalidOperationException("The authored aerial-radiance texture was not published.");
        var aerialAttenuation = AtmosphereModSystem.AerialAttenuationTexture
            ?? throw new InvalidOperationException("The authored aerial-attenuation texture was not published.");
        using var depth = new DepthTexture(1, 1, TextureFormat.DepthComponent32f);
        depth.UploadDataImmediate([scenario is 4 or 5 or 10 ? 1f : 0f]);
        using var lighting = new TestFrameLightingPublication();
        program.Capture(scenario, view, frame, lights, lighting.Buffer, depth, aerialRadiance,
            aerialAttenuation, neutralShaftOcclusion);

        using var target = CreateRenderTarget(1, 1, TextureFormat.Rgba32f);
        var commands = RHICommandContext.Current;
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var layout = new RHIVertexLayout([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        // Act: submit the declared helper through its retained owner and one complete RHI render pass.
        using var boundary = commands.EnterBoundary(new("Tests.PbrForwardSurface", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 3));
            commands.EndRenderPass();
        });
        return target[0].ReadPixels();
    }
    #endregion
}
