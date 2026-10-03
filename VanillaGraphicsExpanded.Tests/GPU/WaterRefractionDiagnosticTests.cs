using System.Globalization;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Records exact production traversal decisions on controlled opaque receiver fields.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionDiagnosticTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Separates rejected receivers from accepted hits attenuated by confidence heuristics.</summary>
    [Theory]
    [InlineData(0, "flat")]
    [InlineData(1, "tilted")]
    [InlineData(2, "shallow-plane")]
    [InlineData(3, "adjacent-foreground")]
    [InlineData(4, "sky")]
    [InlineData(5, "range-exhaustion")]
    [InlineData(6, "edge-motion")]
    [InlineData(7, "range-fade")]
    [InlineData(8, "positive-view-z")]
    [InlineData(9, "metadata")]
    [InlineData(10, "depth-discontinuity")]
    [InlineData(11, "grazing")]
    public void BaselineReceiverDiagnostics(int scenario, string label)
    {
        EnsureContextValid();
        const int size = 128;
        float receiverDepth = scenario switch { 2 => 2.01f, 5 => 80f, 7 => 30f, 8 => 1.7f, _ => 10f };
        float deviceDepth = DeviceDepth(receiverDepth);
        var depths = Enumerable.Repeat(scenario == 4 ? 1f : deviceDepth, size * size).ToArray();
        if (scenario == 3) depths[64 * size + 65] = DeviceDepth(1);
        // An oblique submerged plane remains eligible while the bent ray moves toward the camera.
        if (scenario == 8)
            for (int y = 0; y < size; ++y)
                for (int x = 0; x < size; ++x)
                {
                    float rayX = (2f * (x + .5f) / size - 1) / .1f;
                    float distance = -6.572f / (-.7f * rayX + .714f);
                    depths[y * size + x] = distance > .1f ? DeviceDepth(distance) : 1f;
                }
        if (scenario == 10)
            for (int y = 0; y < size; ++y)
                for (int x = 0; x < size; ++x)
                    depths[y * size + x] = DeviceDepth(x < 69 ? 20 : 3);
        using var depth = DynamicTexture2D.Create(size, size, PixelInternalFormat.R32f);
        using var color = DynamicTexture2D.Create(size, size, PixelInternalFormat.Rgba32f);
        depth.UploadDataImmediate(depths);
        color.UploadDataImmediate(Enumerable.Range(0, size * size).SelectMany(_ => new[] { 4f, 2f, 1f, scenario == 9 ? 0f : 1f }).ToArray());
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/liquids/refraction.glsl"));
        string normal = scenario is 1 or 10 ? "normalize(vec3(-.4,0,1))" : scenario == 8 ? "normalize(vec3(-.7,0,-.714))"
            : scenario == 11 ? "normalize(vec3(1,0,.01))" : "vec3(0,0,1)";
        float focal = scenario == 8 ? .1f : MathF.Sqrt(3);
        string header = $$"""
            #version 450 core
            const vec2 frameSize = vec2(128);
            const float focal = {{focal.ToString("R", CultureInfo.InvariantCulture)}};
            mat4 projectionMatrix = mat4(focal,0,0,0, 0,focal,0,0, 0,0,-100.1/99.9,-1, 0,0,-20.0/99.9,0);
            int diagnosticReason = 0;
            int diagnosticCount = 0;
            vec3 diagnosticSample = vec3(0);
            #define VGE_REFRACTION_EVENT(reason) diagnosticReason = reason
            #define VGE_REFRACTION_SAMPLE(uv, depth) diagnosticCount++; diagnosticSample = vec3(uv, depth)
            /** Converts the independently supplied conventional hardware depth to metres. */
            float VgeLiquidViewDepth(float depth) { return 20.0 / (100.1 - (depth * 2.0 - 1.0) * 99.9); }
            layout(location=0) out vec4 decision;
            layout(location=1) out vec4 sampled;
            layout(location=2) out vec4 transport;
            """;
        string main = $$"""
            /** Exposes selected sample, rejection, depth-evaluation count, transport and fallback weight. */
            void main()
            {
                vec3 surface = {{(scenario == 8 ? "vec3(10,0,-2)" : "vec3((gl_FragCoord.xy / frameSize * 2.0 - 1.0) * 2.0 / focal, -2)")}};
                vec3 normal = {{normal}};
                vec3 background, receiver;
                float lengthMetres, confidence;
                bool hit = VgeWaterRefraction(surface, normal, false, background, lengthMetres, receiver, confidence);
                decision = vec4(hit ? 1 : 0, diagnosticReason, diagnosticCount, confidence);
                sampled = vec4(diagnosticSample, 1);
                transport = vec4(lengthMetres, refract(normalize(surface), normal, 1.0/1.333).z, hit ? 1.0-confidence : 1.0, receiver.z);
            }
            """;
        int vertex = Compile(ShaderType.VertexShader, "#version 450 core\nlayout(location=0) in vec2 position; void main(){gl_Position=vec4(position,0,1);}");
        int fragment = 0;
        int program = GL.CreateProgram();
        try
        {
            fragment = Compile(ShaderType.FragmentShader, header + "\n" + source + "\n" + main);
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var colorBinding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 9, color.TextureId);
            using var depthBinding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 10, depth.TextureId);
            using var colorSampler = GpuSamplers.NearestClamp.BindScope(9);
            using var depthSampler = GpuSamplers.NearestClamp.BindScope(10);
            using var target = CreateMRTRenderTarget(size, size, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
            using var framework = new ShaderTestFramework();
            framework.RenderQuadTo(program, target);
            int[] columns = scenario == 6 ? [1, 3, 6, 12, 32, 64] : [64];
            foreach (int x in columns)
            {
                float[] decision = target[0].ReadPixelsRegion(x, 64, 1, 1);
                float[] sampled = target[1].ReadPixelsRegion(x, 64, 1, 1);
                float[] transport = target[2].ReadPixelsRegion(x, 64, 1, 1);
                output.WriteLine($"{label} x={x}: hit/reason/evaluations/confidence=[{string.Join(",", decision)}]; uv/depth=[{string.Join(",", sampled)}]; length/directionZ/fallback/receiverZ=[{string.Join(",", transport)}]");
                Assert.All(decision.Concat(sampled).Concat(transport), value => Assert.True(float.IsFinite(value)));
                Assert.InRange(decision[2], 0, 38);
                if (scenario is 0 or 1 or 7) Assert.Equal(1, decision[0]);
                if (scenario == 2) Assert.Equal(4, decision[1]);
                if (scenario == 3) Assert.Equal(5, decision[1]);
                if (scenario is 4 or 9) Assert.Equal(2, decision[1]);
                if (scenario == 5) Assert.Equal(9, decision[1]);
                if (scenario == 6)
                {
                    Assert.Equal(x == 1 ? 0 : 1, decision[0]);
                    if (x is 3 or 6) Assert.InRange(decision[3], .01f, .5f);
                    if (x >= 12) Assert.Equal(1, decision[3]);
                }
                // Receiver depth 30 is 28 metres beyond the surface: the range fade is half strength.
                if (scenario == 7) Assert.InRange(decision[3], .49f, .51f);
                if (scenario == 8)
                {
                    Assert.True(transport[1] > 0);
                    Assert.Equal(1, decision[0]);
                }
                if (scenario == 10) Assert.Equal(7, decision[1]);
                if (scenario == 11) Assert.Equal(1, decision[1]);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.UseProgram(0);
            GL.DeleteProgram(program);
            GL.DeleteShader(vertex);
            if (fragment != 0) GL.DeleteShader(fragment);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Projects an independently specified axial receiver distance into OpenGL device depth.</summary>
    private static float DeviceDepth(float metres) => .5f * (1 + (100.1f - 20 / metres) / 99.9f);

    /// <summary>Compiles diagnostic source with the real traversal include and fails on driver errors.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string error = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            Assert.Fail(error);
        }
        return shader;
    }
    #endregion
}
