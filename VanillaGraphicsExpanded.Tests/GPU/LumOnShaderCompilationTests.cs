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
                var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

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
        { "lumon_hzb_copy.vsh", "lumon_hzb_copy.fsh" },
        { "lumon_hzb_downsample.vsh", "lumon_hzb_downsample.fsh" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh" },
        { "lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh" },
        { "lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh" },
        { "lumon_probe_atlas_filter.vsh", "lumon_probe_atlas_filter.fsh" },
        { "lumon_probe_atlas_gather.vsh", "lumon_probe_atlas_gather.fsh" },
        { "lumon_probe_atlas_project_sh.vsh", "lumon_probe_atlas_project_sh.fsh" },
        { "lumon_probe_atlas_project_sh9.vsh", "lumon_probe_atlas_project_sh9.fsh" },
        { "lumon_probe_sh9_gather.vsh", "lumon_probe_sh9_gather.fsh" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh" },
        { "lumon_velocity.vsh", "lumon_velocity.fsh" },
        { "lumon_combine.vsh", "lumon_combine.fsh" },
        { "lumon_debug.vsh", "lumon_debug.fsh" },
        { "lumon_worldprobe_clipmap_resolve.vsh", "lumon_worldprobe_clipmap_resolve.fsh" },

        // LumOn debug multi-entrypoint split: one program per debug category.
        { "lumon_debug_probe_anchors.vsh", "lumon_debug_probe_anchors.fsh" },
        { "lumon_debug_gbuffer.vsh", "lumon_debug_gbuffer.fsh" },
        { "lumon_debug_temporal.vsh", "lumon_debug_temporal.fsh" },
        { "lumon_debug_sh.vsh", "lumon_debug_sh.fsh" },
        { "lumon_debug_indirect.vsh", "lumon_debug_indirect.fsh" },
        { "lumon_debug_probe_atlas.vsh", "lumon_debug_probe_atlas.fsh" },
        { "lumon_debug_composite.vsh", "lumon_debug_composite.fsh" },
        { "lumon_debug_direct.vsh", "lumon_debug_direct.fsh" },
        { "lumon_debug_velocity.vsh", "lumon_debug_velocity.fsh" },
        { "lumon_debug_worldprobe.vsh", "lumon_debug_worldprobe.fsh" },
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
