using VanillaGraphicsExpanded.Rendering.RHI;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Component.Support.Lighting;


using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Celestial;

/// <summary>Checks finite-source solar limits and cone selection against independent geometry.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrSolarBrdfTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Numerical and material contracts
    /// <summary>Zero angular extent and fully rough materials retain the directional GGX response.</summary>
    [Theory]
    [InlineData(.04f, 0f)]
    [InlineData(.25f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(1f, .021377339f)]
    public void DirectionalLimitsMatchTheExistingBrdf(float roughness, float radius)
    {
        // Arrange: Select surface roughness and solar disk radius, including the directional limit.
        // Act and assert: Evaluate the cone-integrated response and compare it with the existing BRDF limit.
        EnsureContextValid();
        Vector3 normal = Vector3.UnitZ;
        Vector3 view = Vector3.Normalize(new(.3f, .1f, 1));
        Vector3 light = Vector3.Normalize(new(-.2f, .15f, 1));
        float[] actual = Render(normal, view, light, roughness, radius, 0);
        float[] expected = Render(normal, view, light, roughness, radius, 1);
        Assert.Equal(expected, actual);
    }

    /// <summary>Mirror alignment stays finite across the supported material range and broadens without a peak spike.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.04f)]
    [InlineData(.1f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void AlignedSolarResponseIsFiniteAndBounded(float roughness)
    {
        // Arrange: Align view, normal, and sun direction at the selected roughness.
        // Act and assert: Integrate the solar disk response and require finite values within physical bounds.
        EnsureContextValid();
        float[] finite = Render(Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, roughness, AtmosphereSolarDisk.AngularRadius, 0);
        float[] directional = Render(Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Math.Max(.04f, roughness), 0, 1);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.True(float.IsFinite(finite[channel]));
            Assert.InRange(finite[channel], 0, directional[channel] * 1.001f);
        }
        // Independent normal-incidence GGX algebra includes the view-dependent finite-source
        // normalization, preserving roughness and each channel's one Fresnel factor.
        float[] f0 = [.04f, .5f, 1f];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = FiniteSolarBrdfReference.NormalIncidenceExpected(f0[channel], roughness);
            Assert.InRange(finite[channel], expected * .9995f - .00001f, expected * 1.0005f + .00001f);
        }
    }

    #endregion

    #region Cone geometry
    /// <summary>Selected directions stay inside the solar cone and match a dense independent boundary search.</summary>
    [Theory]
    [InlineData(.7f, .2f, .35f, .2f)]
    [InlineData(.95f, 0f, -.93f, .1f)]
    [InlineData(.2f, -.6f, .6f, .2f)]
    [InlineData(.01f, .01f, -.015f, .005f)]
    public void RepresentativeDirectionMatchesConeGeometry(float vx, float vy, float lx, float ly)
    {
        // Arrange: Select representative view and light directions relative to the authored solar cone.
        // Act and assert: Compare the production response with independent cone geometry.
        EnsureContextValid();
        Vector3 normal = Vector3.UnitZ;
        Vector3 view = Vector3.Normalize(new(vx, vy, MathF.Sqrt(Math.Max(.0001f, 1 - vx * vx - vy * vy))));
        Vector3 light = Vector3.Normalize(new(lx, ly, MathF.Sqrt(Math.Max(.0001f, 1 - lx * lx - ly * ly))));
        AssertConeSelection(normal, view, light, MathF.Sin(AtmosphereSolarDisk.AngularRadius));
    }

    /// <summary>Four refinement steps preserve cone alignment for an oblique disk at a near-horizon view.</summary>
    [Fact]
    public void GrazingObliqueConeMatchesIndependentBoundarySearch()
    {
        // Arrange: Use an oblique solar disk and near-horizon view where the cone boundary needs refinement.
        // Act and assert: Apply the four-step production refinement and compare it with an independent boundary search.
        EnsureContextValid();
        float viewAngle = 89.9f * MathF.PI / 180;
        Vector3 view = new(MathF.Sin(viewAngle), 0, MathF.Cos(viewAngle));
        Vector3 reflection = Vector3.Reflect(-view, Vector3.UnitZ);
        Vector3 tangent = new(view.Z, 0, view.X);
        float offset = 8 * AtmosphereSolarDisk.AngularRadius;
        float azimuth = 1.4f;
        Vector3 light = reflection * MathF.Cos(offset)
            + MathF.Sin(offset) * (tangent * MathF.Cos(azimuth) + Vector3.UnitY * MathF.Sin(azimuth));
        float effectiveSine = MathF.Sin(AtmosphereSolarDisk.AngularRadius) * (1 - .04f * .04f);
        AssertConeSelection(Vector3.UnitZ, view, light, effectiveSine);
    }

    /// <summary>Mirror directions remain finite when view cosine is zero or close to the surface horizon.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.000001f)]
    [InlineData(.0001f)]
    public void GrazingMirrorResponseRemainsFinite(float viewCosine)
    {
        // Arrange: Select a near-grazing view for a smooth mirror response.
        // Act and assert: Evaluate the production BRDF and require finite grazing output.
        EnsureContextValid();
        Vector3 view = Vector3.Normalize(new(1, 0, viewCosine));
        Vector3 light = Vector3.Reflect(-view, Vector3.UnitZ);
        float[] actual = Render(Vector3.UnitZ, view, light, .04f, AtmosphereSolarDisk.AngularRadius, 0);
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
        if (viewCosine == 0) Assert.Equal(new[] { 0f, 0f, 0f, 1f }, actual);
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Checks a GPU cone direction against an independent dense circular-boundary maximum.</summary>
    private void AssertConeSelection(Vector3 normal, Vector3 view, Vector3 light, float sine)
    {
        float[] rendered = Render(normal, view, light, .5f, sine, 2);
        Vector3 selected = new(rendered[0], rendered[1], rendered[2]);
        Assert.InRange(selected.Length(), .99999f, 1.00001f);
        Assert.True(Vector3.Dot(selected, light) >= MathF.Sqrt(1 - sine * sine) - .000002f);
        double actual = Vector3.Dot(normal, Vector3.Normalize(view + selected));
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(light, Math.Abs(light.Z) < .9 ? Vector3.UnitZ : Vector3.UnitX));
        Vector3 bitangent = Vector3.Cross(light, tangent);
        double expected = Vector3.Dot(normal, Vector3.Normalize(view + light));
        // Search independently across the full circular boundary, also considering the exact
        // mirror direction when it is inside the disk. This does not repeat the Newton solver.
        for (int sample = 0; sample < 32768; sample++)
        {
            double angle = sample * (2 * Math.PI / 32768);
            Vector3 candidate = light * MathF.Sqrt(1 - sine * sine) + sine * ((float)Math.Cos(angle) * tangent + (float)Math.Sin(angle) * bitangent);
            expected = Math.Max(expected, Vector3.Dot(normal, Vector3.Normalize(view + candidate)));
        }
        Vector3 reflection = Vector3.Reflect(-view, normal);
        if (Vector3.Dot(reflection, light) >= MathF.Sqrt(1 - sine * sine)) expected = 1;
        Assert.InRange(actual, expected - .000002, expected + .000002);
    }

    /// <summary>Evaluates current production helper text in a float32 headless render target.</summary>
    private float[] Render(Vector3 normal, Vector3 view, Vector3 light, float roughness, float radius, int mode)
    {
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<SolarBrdfProgram>();
        using var target = CreateRenderTarget(1, 1, TextureFormat.Rgba32f);
        program.Capture(normal, view, light, roughness, radius, mode);

        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([new(0, 2, VertexComponentFormat.Float,
            VertexInterpretation.Floating, 0, 0, 4 * sizeof(float))]);
        using var vertices = commands.CreateVertexBuffer(new RHIBufferDescription
        { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.StaticDraw, Label = "Tests.PbrSolarBrdf.Quad" }).RequireResource();
        // Retain the original six vertices and their unused texture coordinates byte for byte.
        vertices.UploadData([-1f, -1f, 0f, 0f, 1f, -1f, 1f, 0f,
            1f, 1f, 1f, 1f, -1f, -1f, 0f, 0f, 1f, 1f, 1f, 1f, -1f, 1f, 0f, 1f]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo> { [0] = vertices });
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear,
                Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.PbrSolarBrdf", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 6));
            commands.EndRenderPass();
        });
        return ReadPixelsFloat(target);
    }

    #endregion
}
