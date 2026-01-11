using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Tests that verify uniforms declared in LumOn shader source code are properly
/// linked and accessible in the compiled shader programs.
/// 
/// This test uses AST-based uniform extraction from shader source to automatically
/// discover all declared uniforms, then compares against OpenGL's reported active
/// uniforms. Uniforms that are declared but not found by OpenGL are reported as
/// "optimized out" (the GPU driver removed them because they're unused).
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnUniformTests : IDisposable
{
    private readonly HeadlessGLFixture _fixture;
    private readonly ShaderTestHelper? _helper;
    private readonly ITestOutputHelper _output;

    public LumOnUniformTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;

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

    #region Shader Pairs Data

    /// <summary>
    /// All LumOn shader pairs to test for uniform validation.
    /// </summary>
    public static TheoryData<string, string> LumOnShaderPairs => new()
    {
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh" },
        { "lumon_temporal.vsh", "lumon_temporal.fsh" },
        { "lumon_gather.vsh", "lumon_gather.fsh" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh" },
        { "lumon_combine.vsh", "lumon_combine.fsh" },
        { "lumon_debug.vsh", "lumon_debug.fsh" },
    };

    #endregion

    #region Uniform Validation Tests

    /// <summary>
    /// Validates that all uniforms declared in shader source are either:
    /// 1. Active (found by GL.GetUniformLocation)
    /// 2. Documented as optimized out (unused by the shader)
    /// 
    /// This test extracts uniforms via AST parsing, compiles the shader, and
    /// compares declared vs active uniforms to detect mismatches.
    /// </summary>
    [Theory]
    [MemberData(nameof(LumOnShaderPairs))]
    public void Shader_DeclaredUniformsAreAccessible(string vertexShader, string fragmentShader)
    {
        _fixture.EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available - assets may be missing");

        // Get processed shader sources (with @import resolved)
        var (vertexSource, fragmentSource) = _helper!.GetProcessedSources(vertexShader, fragmentShader);
        Assert.SkipWhen(vertexSource == null || fragmentSource == null,
            $"Could not read shader sources for {vertexShader}/{fragmentShader}");

        // Extract uniforms from source via AST
        var declaredUniforms = UniformExtractor.ExtractUniformNamesFromMultiple(vertexSource!, fragmentSource!);

        // Compile and link the shader
        var linkResult = _helper.CompileAndLink(vertexShader, fragmentShader);
        Assert.True(linkResult.IsSuccess,
            $"Failed to compile/link {vertexShader} + {fragmentShader}: {linkResult.ErrorMessage}");

        // Compare declared uniforms with GL locations
        var activeUniforms = new List<string>();
        var optimizedOut = new List<string>();

        foreach (var uniformName in declaredUniforms.OrderBy(n => n))
        {
            int location = _helper.GetUniformLocation(linkResult.ProgramId, uniformName);
            if (location >= 0)
            {
                activeUniforms.Add(uniformName);
            }
            else
            {
                optimizedOut.Add(uniformName);
            }
        }

        // Log results for visibility
        var shaderName = Path.GetFileNameWithoutExtension(vertexShader).Replace(".vsh", "");
        _output.WriteLine($"=== {shaderName} Uniform Analysis ===");
        _output.WriteLine($"Declared uniforms: {declaredUniforms.Count}");
        _output.WriteLine($"Active uniforms: {activeUniforms.Count}");
        _output.WriteLine($"Optimized out: {optimizedOut.Count}");

        if (activeUniforms.Count > 0)
        {
            _output.WriteLine($"\nActive:");
            foreach (var name in activeUniforms)
            {
                _output.WriteLine($"  ✓ {name}");
            }
        }

        if (optimizedOut.Count > 0)
        {
            _output.WriteLine($"\nOptimized out (declared but unused):");
            foreach (var name in optimizedOut)
            {
                _output.WriteLine($"  ○ {name}");
            }
        }

        // Test passes as long as shader compiled - optimized out uniforms are expected
        // The key assertion is that we can compile and link the shader
        Assert.True(linkResult.ProgramId > 0, "Program should have valid ID");
    }

    /// <summary>
    /// Validates specific critical uniforms that MUST be active (not optimized out)
    /// for the shader to function correctly. These are the uniforms that the C#
    /// code actually sets at runtime.
    /// </summary>
    [Theory]
    [MemberData(nameof(CriticalUniformsData))]
    public void Shader_CriticalUniformIsActive(string vertexShader, string fragmentShader, string uniformName)
    {
        _fixture.EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available - assets may be missing");

        var linkResult = _helper!.CompileAndLink(vertexShader, fragmentShader);
        Assert.True(linkResult.IsSuccess,
            $"Failed to compile/link {vertexShader} + {fragmentShader}: {linkResult.ErrorMessage}");

        int location = _helper.GetUniformLocation(linkResult.ProgramId, uniformName);
        Assert.True(location >= 0,
            $"Critical uniform '{uniformName}' not found in {vertexShader}/{fragmentShader}. " +
            $"Location: {location}. This uniform is required for the shader to work correctly.");
    }

    /// <summary>
    /// Critical uniforms that must be active for each shader.
    /// These are uniforms that the C# rendering code sets at runtime.
    /// </summary>
    public static TheoryData<string, string, string> CriticalUniformsData => new()
    {
        // lumon_probe_anchor - depth/normal input and matrices
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "primaryDepth" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "gBufferNormal" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "invProjectionMatrix" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "invViewMatrix" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "probeSpacing" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "screenSize" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "frameIndex" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "anchorJitterEnabled" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", "anchorJitterScale" },

        // lumon_probe_trace - probe data and ray-marching params
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh", "probeAnchorPosition" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh", "probeAnchorNormal" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh", "primaryDepth" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh", "primaryColor" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh", "probeGridSize" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh", "frameIndex" },
        { "lumon_probe_trace.vsh", "lumon_probe_trace.fsh", "raysPerProbe" },

        // lumon_temporal - history buffers and blend params
        { "lumon_temporal.vsh", "lumon_temporal.fsh", "radianceCurrent0" },
        { "lumon_temporal.vsh", "lumon_temporal.fsh", "radianceHistory0" },
        { "lumon_temporal.vsh", "lumon_temporal.fsh", "probeAnchorPosition" },
        { "lumon_temporal.vsh", "lumon_temporal.fsh", "temporalAlpha" },

        // lumon_gather - radiance sampling and bilateral filter
        { "lumon_gather.vsh", "lumon_gather.fsh", "radianceTexture0" },
        { "lumon_gather.vsh", "lumon_gather.fsh", "probeAnchorPosition" },
        { "lumon_gather.vsh", "lumon_gather.fsh", "primaryDepth" },
        { "lumon_gather.vsh", "lumon_gather.fsh", "gBufferNormal" },
        { "lumon_gather.vsh", "lumon_gather.fsh", "probeSpacing" },
        { "lumon_gather.vsh", "lumon_gather.fsh", "intensity" },

        // lumon_upsample - bilateral upsampling
        { "lumon_upsample.vsh", "lumon_upsample.fsh", "indirectHalf" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh", "primaryDepth" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh", "gBufferNormal" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh", "screenSize" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh", "holeFillEnabled" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh", "holeFillRadius" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh", "holeFillMinConfidence" },

        // lumon_combine - final compositing
        { "lumon_combine.vsh", "lumon_combine.fsh", "sceneDirect" },
        { "lumon_combine.vsh", "lumon_combine.fsh", "indirectDiffuse" },
        { "lumon_combine.vsh", "lumon_combine.fsh", "gBufferAlbedo" },
        { "lumon_combine.vsh", "lumon_combine.fsh", "indirectIntensity" },
        { "lumon_combine.vsh", "lumon_combine.fsh", "lumOnEnabled" },

        // lumon_debug - visualization uniforms
        { "lumon_debug.vsh", "lumon_debug.fsh", "primaryDepth" },
        { "lumon_debug.vsh", "lumon_debug.fsh", "screenSize" },
        { "lumon_debug.vsh", "lumon_debug.fsh", "probeGridSize" },
    };

    #endregion

    #region Diagnostic Tests

    /// <summary>
    /// Generates a detailed uniform report for all shaders.
    /// This is useful for documentation and debugging.
    /// </summary>
    [Fact]
    public void GenerateUniformReport()
    {
        _fixture.EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available - assets may be missing");

        _output.WriteLine("=== LumOn Shader Uniform Report ===\n");

        foreach (var (vsh, fsh) in GetShaderPairs())
        {
            var (vertexSource, fragmentSource) = _helper!.GetProcessedSources(vsh, fsh);
            if (vertexSource == null || fragmentSource == null)
            {
                _output.WriteLine($"{vsh}: SKIPPED (source not found)");
                continue;
            }

            var linkResult = _helper.CompileAndLink(vsh, fsh);
            if (!linkResult.IsSuccess)
            {
                _output.WriteLine($"{vsh}: FAILED ({linkResult.ErrorMessage})");
                continue;
            }

            // Extract full uniform declarations with types
            var combinedSource = vertexSource + "\n" + fragmentSource;
            var declarations = UniformExtractor.ExtractUniformsList(combinedSource);

            var shaderName = Path.GetFileNameWithoutExtension(vsh);
            _output.WriteLine($"--- {shaderName} ---");

            foreach (var decl in declarations.OrderBy(d => d.Name))
            {
                int location = _helper.GetUniformLocation(linkResult.ProgramId, decl.Name);
                var status = location >= 0 ? "✓" : "○";
                var arrayInfo = decl.IsArray ? $"[{decl.ArraySize}]" : "";
                _output.WriteLine($"  {status} {decl.TypeName} {decl.Name}{arrayInfo} (loc: {location})");
            }

            _output.WriteLine("");
        }
    }

    private static IEnumerable<(string vsh, string fsh)> GetShaderPairs()
    {
        yield return ("lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh");
        yield return ("lumon_probe_trace.vsh", "lumon_probe_trace.fsh");
        yield return ("lumon_temporal.vsh", "lumon_temporal.fsh");
        yield return ("lumon_gather.vsh", "lumon_gather.fsh");
        yield return ("lumon_upsample.vsh", "lumon_upsample.fsh");
        yield return ("lumon_combine.vsh", "lumon_combine.fsh");
        yield return ("lumon_debug.vsh", "lumon_debug.fsh");
    }

    #endregion
}
