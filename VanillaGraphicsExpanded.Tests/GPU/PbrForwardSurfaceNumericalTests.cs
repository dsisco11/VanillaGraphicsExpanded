using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Numerics;
using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks shared forward lighting at shadow, emission and view-space point-light boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrForwardSurfaceNumericalTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public PbrForwardSurfaceNumericalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Numerical ownership
    /// <summary>Checks explicit solar inputs, shadow occlusion, one emission contribution, view-space points and enclosed aerial transport.</summary>
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
        string directory = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string common = File.ReadAllText(Path.Combine(directory, "pbr_common.glsl"))
            .Replace("@import \"./common_constants.glsl\"", File.ReadAllText(Path.Combine(directory, "common_constants.glsl")));
        string header = """
            #version 330 core
            #extension GL_ARB_shading_language_420pack : require
            uniform int vge_pbrRoute;
            #define SHADOWQUALITY 1
            #define DYNLIGHTS 1
            #define VGE_SURFACE_VIEW surfaceView
            layout(std140) uniform TestInputs { mat4 surfaceView; };
            uniform sampler2DShadow shadowMapFar;
            vec4 shadowCoordsFar = vec4(0.5, 0.5, 0.8, 1.0);
            float shadowIntensity = 1.0;
            // A newer engine direction must not replace the published atmospheric generation.
            vec3 lightPosition = vec3(0,0,-1);
            vec3 vge_atmosphereSunDirection = vec3(0,0,1);
            float vge_atmosphereDirectRadius = 0.0;
            vec3 vge_viewPosition = vec3(0,0,-2);
            vec3 vge_blockIrradiance = vec3(0);
            vec3 vge_sunIrradiance = vec3(0.2,0.4,0.8);
            vec3 vge_atmosphereSolar = vec3(0.2,0.4,0.8);
            vec3 vge_atmosphereEnvironment = vec3(0);
            vec4 vge_atmosphereAerialParams = vec4(.001,0,0,16);

            float vge_skyVisibility = 1.0;
            vec4 rgbaFog = vec4(0);
            layout(location=0) out vec4 outColor;
            """;
        if (scenario == 5 || scenario >= 7) header = header.Replace("vec3(0.2,0.4,0.8)", "vec3(0.0)");
        if (scenario >= 10) header = header.Replace("vec3 vge_atmosphereSolar = vec3(0.0)", "vec3 vge_atmosphereSolar = vec3(0.3,0.5,0.7)");
        if (scenario >= 16) header = header.Replace("vec3 vge_atmosphereSolar = vec3(0.3,0.5,0.7)", "vec3 vge_atmosphereSolar = vec3(0.0)");
        if (scenario == 13) header = header.Replace("float vge_skyVisibility = 1.0", "float vge_skyVisibility = 0.0");
        header += $"\n#define VGE_PBR_FORWARD_LUMON {(scenario is >= 6 and <= 9 ? 0 : 1)}\n";
        string aerial = File.ReadAllText(Path.Combine(directory, "atmosphere_aerial.glsl"))
            .Replace("@import \"./atmosphere_sky_mapping.glsl\"", File.ReadAllText(Path.Combine(directory, "atmosphere_sky_mapping.glsl")))
            .Replace("@import \"./atmosphere_aerial_mapping.glsl\"", File.ReadAllText(Path.Combine(directory, "atmosphere_aerial_mapping.glsl")));
        string source = header + "\n" + aerial + "\n" + File.ReadAllText(Path.Combine(directory, "pbr_color.glsl")) + "\n" + common + "\n"
            + Expand(Path.Combine(directory, "pbr_photometric.glsl")) + "\n"
            + Expand(Path.Combine(directory, "pbr_direct_brdf.glsl")) + "\n"
            + Expand(Path.Combine(directory, "pbr_environment.glsl")) + "\n"
            + Expand(Path.Combine(directory, "pbr_forward_surface.glsl"))
            + $"\nvoid main() {{ outColor = vec4(VgeForwardSurface(vec3(0.2,0.4,0.6), {(scenario == 3 ? "vec3(1,0,0)" : "vec3(0,0,1)")}, vec3(0.5,0,{(scenario is 1 or 9 or 15 ? "2.0" : "0.0")}), 0.0), 0.37); }}";
        int vertex = Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0) in vec2 position; void main(){gl_Position=vec4(position,0,1);}");
        int fragment = Compile(ShaderType.FragmentShader, source);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            Helpers.MicrofacetTextureReference.Bind();
            using var frame = new VgeFrameUniformBuffer();
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Frame, frame.Capture()));
            using var inputs = new FixtureUniformInputs(program, 64);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "vge_pbrRoute"), 2);
            float[] view = scenario == 3 ? [0,0,1,0, 0,1,0,0, -1,0,0,0, 0,0,0,1] : [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
            inputs.Matrix(0, view);
            inputs.Publish();
            using var lights = new VgeLightsUniformBuffer();
            lights.Capture(scenario is 2 or 3 or 8 or 14 ? 1 : 0, [0, 0, 0], [4, 4, 4]);
            if (scenario >= 16) lights.CaptureAuthored([new(new(0, 0, scenario == 17 ? 0 : -1), Vector3.One,
                scenario == 18 ? 2000 : 1000, 100)]);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Lights, lights.Capture()));
            GL.Uniform1(GL.GetUniformLocation(program, "shadowMapFar"), 0);
            using var atmosphereOwner = new AtmosphereModSystem();
            float[] scatter = scenario is 12 or 13 ? [.04f,.09f,.16f,1f] : [0f,0f,0f,1f];
            atmosphereOwner.Publish(new(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.Create(0f,0f,0f,1f))
                { Width=1, Height=1, AerialRadiance=ImmutableArray.CreateRange(Enumerable.Range(0,24).SelectMany(_=>scatter)), AerialAttenuation=ImmutableArray.CreateRange(Enumerable.Range(0,24).SelectMany(_=>new[]{0f,0f,0f,1f})) });
            using var radianceBinding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 11, AtmosphereModSystem.AerialRadianceTextureId);
            using var attenuationBinding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 12, AtmosphereModSystem.AerialAttenuationTextureId);
            GL.Uniform1(GL.GetUniformLocation(program, "vge_atmosphereAerialRadiance"),11);
            GL.Uniform1(GL.GetUniformLocation(program, "vge_atmosphereAerialAttenuation"),12);
            using var framework = new ShaderTestFramework();
            using var depth = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.DepthComponent32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            // The generic float uploader assumes color channels; explicitly initialize the depth comparison input.
            GL.ClearTexImage(depth.TextureId, 0, PixelFormat.DepthComponent, PixelType.Float, new[] { scenario is 4 or 5 or 10 ? 1f : 0f });
            using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, depth, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(((GpuTextureRef)depth).Target))]);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, 0, GpuSamplers.ShadowCompareLinearClamp.SamplerId);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float factor = scenario is 1 or 9 or 15 ? 64f : scenario is 2 or 3 or 8 or 14 ? LightingCalibration.EnginePointCandelaPerUnit : 0f;
            float[,] radiance = new float[4,3];
            var lookup=MicrofacetTextureReference.Sample(MicrofacetEnergyLookup.Prepare().Width,.5f,1);
            double eLow=lookup.Value.X-lookup.Error.X,eHigh=lookup.Value.X+lookup.Error.X;
            double efLow=Math.Max(0,lookup.Value.Y-lookup.Error.Y),efHigh=lookup.Value.Y+lookup.Error.Y;
            double gainLow=1+.04*(1-eHigh)/eHigh,gainHigh=1+.04*(1-eLow)/eLow;
            double reflectedLow=gainLow*(eLow*.04+efLow*.96),reflectedHigh=gainHigh*(eHigh*.04+efHigh*.96);
            // Propagate only the declared texture precision through the material-energy
            // coefficients. Source lux, physical BRDF and the arithmetic margin stay fixed.
            var responses=new[]{(Reflection:reflectedLow,Gain:gainLow),(Reflection:reflectedLow,Gain:gainHigh),
                (Reflection:reflectedHigh,Gain:gainLow),(Reflection:reflectedHigh,Gain:gainHigh)};
            for(int variant=0;variant<responses.Length;variant++)
            {
            var response=responses[variant];
            float diffuseWeight = (float)(1-response.Reflection);
            float specularGain = (float)response.Gain;
            for (int channel = 0; channel < 3; channel++)
            {
                float irradiance = scenario == 4 ? new[] { .2f, .4f, .8f }[channel] / MathF.PI : factor;
                float linear = (channel + 1) * .2f * irradiance;
                // Direct specular receives W once; diffuse uses the remaining integrated energy.
                float normalSpecular = .16f * specularGain / MathF.PI;
                if (scenario is 2 or 3 or 8 or 14)
                    linear = (diffuseWeight * (channel + 1) * .2f / MathF.PI + normalSpecular) * factor;
                if (scenario == 4)
                    linear = (diffuseWeight * (channel + 1) * .2f / MathF.PI + PbrSolarBrdfTests.NormalIncidenceExpected(.04f, .5f)*specularGain)
                        * new[] { .2f, .4f, .8f }[channel];
                if (scenario == 6) linear = new[] { .2f,.4f,.8f }[channel]
                    * (float)(diffuseWeight * (channel + 1) * .2 + response.Reflection);
                if (scenario == 10) linear = (diffuseWeight * (channel + 1) * .2f / MathF.PI
                    + PbrSolarBrdfTests.NormalIncidenceExpected(.04f, .5f)*specularGain) * (.3f + channel * .2f);
                if (scenario == 12) linear = new[]{.04f,.09f,.16f}[channel];
                if (scenario >= 16)
                {
                    double lux = (scenario == 18 ? 2000 : 1000) / (4 * Math.PI * (scenario == 17 ? 4 : 1));
                    linear = (float)((diffuseWeight * (channel + 1) * .2 / Math.PI + normalSpecular) * lux);
                }
                radiance[variant,channel] = linear;
            }
            }
            // Forward scene lighting is unexposed radiance; the final pass owns display conversion.
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(actual[channel], Enumerable.Range(0,4).Min(variant=>radiance[variant,channel]) - .0001f,
                    Enumerable.Range(0,4).Max(variant=>radiance[variant,channel]) + .0001f);
            Assert.InRange(actual[3], .36999f, .37001f);
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }
    #endregion

    #region Driver compilation
    /// <summary>Expands guarded production imports without changing their lighting equations.</summary>
    private static string Expand(string path) => Regex.Replace(File.ReadAllText(path), "@import \"([^\"]+)\"",
        match => Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));
    /// <summary>Compiles production helper text at the engine runtime GLSL boundary.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        string log = GL.GetShaderInfoLog(shader);
        if (compiled == 0) GL.DeleteShader(shader);
        Assert.True(compiled != 0, log);
        return shader;
    }
    #endregion
}
