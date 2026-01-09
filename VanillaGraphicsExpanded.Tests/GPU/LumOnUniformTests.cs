using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Tests that verify expected uniforms exist in linked LumOn shader programs.
/// Validates that C# code and GLSL shaders agree on uniform names.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnUniformTests : IDisposable
{
    private readonly HeadlessGLFixture _fixture;
    private readonly ShaderTestHelper? _helper;

    public LumOnUniformTests(HeadlessGLFixture fixture)
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

    #region Expected Uniforms Data

    /// <summary>
    /// Expected uniforms for lumon_probe_anchor shader.
    /// Note: probeGridSize is declared but not used in shader code (optimized out).
    /// </summary>
    public static TheoryData<string> ProbeAnchorUniforms => new()
    {
        "primaryDepth",
        "gBufferNormal",
        "invProjectionMatrix",
        "invViewMatrix",
        "probeSpacing",
        // "probeGridSize",  // Declared but optimized out
        "screenSize",
        "zNear",
        "zFar",
        "depthDiscontinuityThreshold",
    };

    /// <summary>
    /// Expected uniforms for lumon_probe_trace shader.
    /// Note: Some uniforms are declared but optimized out because they're passed through includes.
    /// </summary>
    public static TheoryData<string> ProbeTraceUniforms => new()
    {
        "probeAnchorPosition",
        "probeAnchorNormal",
        "primaryDepth",
        "primaryColor",
        "invProjectionMatrix",
        "projectionMatrix",
        "viewMatrix",
        // "probeSpacing",   // Declared but optimized out
        "probeGridSize",
        // "screenSize",     // Declared but optimized out
        "frameIndex",
        "raysPerProbe",
        "raySteps",
        "rayMaxDistance",
        "rayThickness",
        // "zNear",          // Passed through lumon_common.fsh but not used directly
        // "zFar",           // Passed through lumon_common.fsh but not used directly
        "skyMissWeight",
        "sunPosition",
        "sunColor",
        "ambientColor",
    };

    /// <summary>
    /// Expected uniforms for lumon_temporal shader.
    /// Note: zNear/zFar use local LinearizeDepth, invViewMatrix not used.
    /// </summary>
    public static TheoryData<string> TemporalUniforms => new()
    {
        "radianceCurrent0",
        "radianceCurrent1",
        "radianceHistory0",
        "radianceHistory1",
        "probeAnchorPosition",
        "probeAnchorNormal",
        "historyMeta",
        "viewMatrix",
        // "invViewMatrix",     // Declared but optimized out
        "prevViewProjMatrix",
        "probeGridSize",
        // "zNear",             // Used in local LinearizeDepth but may be optimized
        // "zFar",              // Used in local LinearizeDepth but may be optimized
        "temporalAlpha",
        "depthRejectThreshold",
        "normalRejectThreshold",
    };

    /// <summary>
    /// Expected uniforms for lumon_gather shader.
    /// Note: zNear/zFar passed via include, depthDiscontinuityThreshold not used.
    /// </summary>
    public static TheoryData<string> GatherUniforms => new()
    {
        "radianceTexture0",
        "radianceTexture1",
        "probeAnchorPosition",
        "probeAnchorNormal",
        "primaryDepth",
        "gBufferNormal",
        "invProjectionMatrix",
        "viewMatrix",
        "probeSpacing",
        "probeGridSize",
        "screenSize",
        "halfResSize",
        // "zNear",                      // Passed through include
        // "zFar",                       // Passed through include
        // "depthDiscontinuityThreshold", // Declared but not used
        "intensity",
        "indirectTint",
        "depthSigma",
        "normalSigma",
    };

    /// <summary>
    /// Expected uniforms for lumon_upsample shader.
    /// Note: upsampleSpatialSigma declared but not used in current implementation.
    /// </summary>
    public static TheoryData<string> UpsampleUniforms => new()
    {
        "indirectHalf",
        "primaryDepth",
        "gBufferNormal",
        "screenSize",
        "halfResSize",
        "zNear",
        "zFar",
        "denoiseEnabled",
        "upsampleDepthSigma",
        "upsampleNormalSigma",
        // "upsampleSpatialSigma",  // Declared but optimized out
    };

    /// <summary>
    /// Expected uniforms for lumon_combine shader.
    /// </summary>
    public static TheoryData<string> CombineUniforms => new()
    {
        "sceneDirect",
        "indirectDiffuse",
        "gBufferAlbedo",
        "gBufferMaterial",
        "primaryDepth",
        "indirectIntensity",
        "indirectTint",
        "lumOnEnabled",
    };

    /// <summary>
    /// Expected uniforms for lumon_debug shader.
    /// Note: indirectHalf, invProjectionMatrix declared but not used in all code paths.
    /// </summary>
    public static TheoryData<string> DebugUniforms => new()
    {
        "primaryDepth",
        "gBufferNormal",
        "probeAnchorPosition",
        "probeAnchorNormal",
        "radianceTexture0",
        "radianceTexture1",
        // "indirectHalf",        // Declared but optimized out
        "historyMeta",
        // "invProjectionMatrix", // Declared but optimized out
        "screenSize",
        "probeGridSize",
        "probeSpacing",
        "zNear",
        "zFar",
        "temporalAlpha",
        "depthRejectThreshold",
        "normalRejectThreshold",
    };

    #endregion

    #region Uniform Validation Tests

    [Theory]
    [MemberData(nameof(ProbeAnchorUniforms))]
    public void ProbeAnchor_HasExpectedUniform(string uniformName)
    {
        AssertUniformExists("lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", uniformName);
    }

    [Theory]
    [MemberData(nameof(ProbeTraceUniforms))]
    public void ProbeTrace_HasExpectedUniform(string uniformName)
    {
        AssertUniformExists("lumon_probe_trace.vsh", "lumon_probe_trace.fsh", uniformName);
    }

    [Theory]
    [MemberData(nameof(TemporalUniforms))]
    public void Temporal_HasExpectedUniform(string uniformName)
    {
        AssertUniformExists("lumon_temporal.vsh", "lumon_temporal.fsh", uniformName);
    }

    [Theory]
    [MemberData(nameof(GatherUniforms))]
    public void Gather_HasExpectedUniform(string uniformName)
    {
        AssertUniformExists("lumon_gather.vsh", "lumon_gather.fsh", uniformName);
    }

    [Theory]
    [MemberData(nameof(UpsampleUniforms))]
    public void Upsample_HasExpectedUniform(string uniformName)
    {
        AssertUniformExists("lumon_upsample.vsh", "lumon_upsample.fsh", uniformName);
    }

    [Theory]
    [MemberData(nameof(CombineUniforms))]
    public void Combine_HasExpectedUniform(string uniformName)
    {
        AssertUniformExists("lumon_combine.vsh", "lumon_combine.fsh", uniformName);
    }

    [Theory]
    [MemberData(nameof(DebugUniforms))]
    public void Debug_HasExpectedUniform(string uniformName)
    {
        AssertUniformExists("lumon_debug.vsh", "lumon_debug.fsh", uniformName);
    }

    #endregion

    #region Helper Methods

    private void AssertUniformExists(string vertexShader, string fragmentShader, string uniformName)
    {
        _fixture.EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available - assets may be missing");

        var linkResult = _helper!.CompileAndLink(vertexShader, fragmentShader);
        Assert.True(linkResult.IsSuccess, 
            $"Failed to compile/link {vertexShader} + {fragmentShader}: {linkResult.ErrorMessage}");

        int location = _helper.GetUniformLocation(linkResult.ProgramId, uniformName);
        Assert.True(location >= 0, 
            $"Uniform '{uniformName}' not found in {vertexShader}/{fragmentShader}. " +
            $"Got location {location}. Check if the uniform is used (not optimized out) or if the name matches.");
    }

    #endregion
}
