using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Tests that verify all LumOn shaders compile and link successfully.
/// These tests catch GLSL syntax errors and linking issues early.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnShaderCompilationTests : IDisposable
{
    private readonly HeadlessGLFixture _fixture;
    private readonly ShaderTestHelper? _helper;

    public LumOnShaderCompilationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;

        if (_fixture.IsContextValid)
        {
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
            var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaderincludes");

            if (Directory.Exists(shaderPath) && Directory.Exists(includePath))
            {
                _helper = new ShaderTestHelper(shaderPath, includePath);
            }
        }
    }

    public void Dispose()
    {
        _helper?.Dispose();
    }

    /// <summary>
    /// Provides test data for all LumOn shader pairs.
    /// </summary>
    public static TheoryData<string, string> LumOnShaderPairs => new()
    {
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh" },
        { "lumon_probe_trace_octahedral.vsh", "lumon_probe_trace_octahedral.fsh" },
        { "lumon_temporal.vsh", "lumon_temporal.fsh" },
        { "lumon_temporal_octahedral.vsh", "lumon_temporal_octahedral.fsh" },
        { "lumon_gather.vsh", "lumon_gather.fsh" },
        { "lumon_gather_octahedral.vsh", "lumon_gather_octahedral.fsh" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh" },
        { "lumon_combine.vsh", "lumon_combine.fsh" },
        { "lumon_debug.vsh", "lumon_debug.fsh" },
    };

    [Theory]
    [MemberData(nameof(LumOnShaderPairs))]
    public void Shader_CompilesAndLinks(string vertexShader, string fragmentShader)
    {
        _fixture.EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available - assets may be missing");

        var result = _helper!.CompileAndLink(vertexShader, fragmentShader);

        Assert.True(result.IsSuccess, 
            $"Shader compilation/link failed for {vertexShader} + {fragmentShader}:\n{result.ErrorMessage}");
        Assert.True(result.ProgramId > 0, "Program ID should be valid");
    }

    [Theory]
    [MemberData(nameof(LumOnShaderPairs))]
    public void VertexShader_CompilesSuccessfully(string vertexShader, string _)
    {
        _fixture.EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available - assets may be missing");

        var result = _helper!.CompileShader(vertexShader, ShaderType.VertexShader);

        Assert.True(result.IsSuccess, 
            $"Vertex shader compilation failed for {vertexShader}:\n{result.ErrorMessage}");
        Assert.True(result.ShaderId > 0, "Shader ID should be valid");
    }

    [Theory]
    [MemberData(nameof(LumOnShaderPairs))]
    public void FragmentShader_CompilesSuccessfully(string _, string fragmentShader)
    {
        _fixture.EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available - assets may be missing");

        var result = _helper!.CompileShader(fragmentShader, ShaderType.FragmentShader);

        Assert.True(result.IsSuccess, 
            $"Fragment shader compilation failed for {fragmentShader}:\n{result.ErrorMessage}");
        Assert.True(result.ShaderId > 0, "Shader ID should be valid");
    }
}
