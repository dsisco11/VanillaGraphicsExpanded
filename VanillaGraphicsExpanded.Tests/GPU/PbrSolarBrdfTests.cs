using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks finite-source solar limits and cone selection against independent geometry.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
            float expected = NormalIncidenceExpected(f0[channel], roughness);
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
        EnsureContextValid();
        Vector3 view = Vector3.Normalize(new(1, 0, viewCosine));
        Vector3 light = Vector3.Reflect(-view, Vector3.UnitZ);
        float[] actual = Render(Vector3.UnitZ, view, light, .04f, AtmosphereSolarDisk.AngularRadius, 0);
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
        if (viewCosine == 0) Assert.Equal(new[] { 0f, 0f, 0f, 1f }, actual);
    }
    #endregion

    #region Source agreement and numerical reference
    /// <summary>The shader half-angle remains identical to the authoritative atmosphere emitter.</summary>
    [Fact]
    public void SolarAngularRadiusMatchesTheAtmosphereEmitter()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_finite_disk_brdf.glsl"));
        var match = Regex.Match(source, @"VGE_SOLAR_ANGULAR_RADIUS\s*=\s*([0-9.]+)");
        Assert.True(match.Success);
        Assert.Equal(AtmosphereSolarDisk.AngularRadius, float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Derives normal-incidence finite solar response independently in double precision.</summary>
    internal static float NormalIncidenceExpected(float f0, float roughness)
    {
        double r = Math.Clamp(roughness, .04, 1);
        double alpha = r * r;
        double alphaSquared = alpha * alpha;
        double sine = Math.Sin(AtmosphereSolarDisk.AngularRadius) * (1 - r * r);
        double extended = alphaSquared + .25 * sine * (3 * alpha + sine) / 1.001;
        return (float)(f0 / (4 * Math.PI * extended));
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
        string directory = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string fragment = "#version 330 core\n" + Expand(Path.Combine(directory, "pbr_finite_disk_brdf.glsl")) + """
            uniform vec3 normal, view, light;
            uniform float roughness, radius;
            uniform int mode;
            layout(location=0) out vec4 result;
            void main(){
                vec3 value = mode == 2 ? VgeFiniteDiskRepresentativeDirection(normal,view,light,radius)
                    : mode == 1 ? cookTorranceBRDF(normal,view,light,vec3(.04,.5,1),roughness)
                    : VgeFiniteDiskSpecularBRDF(normal,view,light,vec3(.04,.5,1),roughness,radius);
                result=vec4(value,1);
            }
            """;
        int vertex = Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0) in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int pixel = Compile(ShaderType.FragmentShader, fragment);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, pixel);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var target = CreateRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.FboId);
            GL.Viewport(0, 0, 1, 1);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest);
            GL.ColorMask(true, true, true, true);
            GL.UseProgram(program);
            GL.Uniform3(GL.GetUniformLocation(program, "normal"), normal.X, normal.Y, normal.Z);
            GL.Uniform3(GL.GetUniformLocation(program, "view"), view.X, view.Y, view.Z);
            GL.Uniform3(GL.GetUniformLocation(program, "light"), light.X, light.Y, light.Z);
            GL.Uniform1(GL.GetUniformLocation(program, "roughness"), roughness);
            GL.Uniform1(GL.GetUniformLocation(program, "radius"), radius);
            GL.Uniform1(GL.GetUniformLocation(program, "mode"), mode);
            RenderFullscreenQuad(program);
            return ReadPixelsFloat(target);
        }
        finally
        {
            GL.DeleteProgram(program);
            GL.DeleteShader(vertex);
            GL.DeleteShader(pixel);
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Expands only local production imports while retaining their normal include guards.</summary>
    private static string Expand(string path)
    {
        string source = File.ReadAllText(path);
        return Regex.Replace(source, "@import \\\"([^\\\"]+)\\\"", match => Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));
    }

    /// <summary>Compiles the diagnostic stage and exposes complete driver compilation failures.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        Assert.True(compiled != 0, GL.GetShaderInfoLog(shader));
        return shader;
    }
    #endregion
}
