using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public class ShaderDefineInjectionTests
{
    private readonly HeadlessGLFixture _fixture;

    public ShaderDefineInjectionTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ProcessedSource_InsertsDefinesImmediatelyAfterVersion()
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_TEST_DEFINE"] = "1",
            ["VGE_TEST_FLOAT"] = "2.0",
        };

        var source = helper.GetProcessedSource("lumon_gather.fsh", defines);
        Assert.NotNull(source);

        int versionIndex = source!.IndexOf("#version", StringComparison.Ordinal);
        Assert.True(versionIndex >= 0, "Expected processed source to contain #version");

        int versionLineEnd = source.IndexOf('\n', versionIndex);
        Assert.True(versionLineEnd >= 0, "Expected #version to be terminated by a newline");

        // Defines must be injected immediately after the #version line.
        int injectedStart = versionLineEnd + 1;
        Assert.True(source.AsSpan(injectedStart).StartsWith("#define VGE_TEST_DEFINE 1\n", StringComparison.Ordinal),
            "Expected injected defines to start immediately after #version line");
    }

    [Fact]
    public void Shaders_CompileAndLink_WithInjectedDefines()
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        // Note: these defines may not be referenced yet by the shaders under test.
        // This test primarily validates the injection mechanism and that shaders still compile.
        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = "0",
            ["VGE_LUMON_PBR_COMPOSITE"] = "1",
        };

        var result = helper.CompileAndLink("lumon_combine.vsh", "lumon_combine.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.ProgramId > 0, "Program ID should be valid");
    }

    [Fact]
    public void GlobalDefinesInclude_Defaults_CompileWithoutInjection()
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var result = helper.CompileAndLink(
            "tests/vge_global_defines_smoke.vsh",
            "tests/vge_global_defines_smoke.fsh");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.ProgramId > 0, "Program ID should be valid");
    }
}
