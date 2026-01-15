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

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LumOnCombine_Compiles_WithVGE_LUMON_ENABLED_Variant(string enabledValue)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = enabledValue,
        };

        var result = helper.CompileAndLink("lumon_combine.vsh", "lumon_combine.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "1", "0")]
    [InlineData("1", "1", "1")]
    public void LumOnCombine_Compiles_WithPbrCompositeVariants(
        string pbrComposite, string enableAO, string enableBentNormal)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = "1",
            ["VGE_LUMON_PBR_COMPOSITE"] = pbrComposite,
            ["VGE_LUMON_ENABLE_AO"] = enableAO,
            ["VGE_LUMON_ENABLE_BENT_NORMAL"] = enableBentNormal,
        };

        var result = helper.CompileAndLink("lumon_combine.vsh", "lumon_combine.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "1", "0")]
    [InlineData("1", "1", "1")]
    public void PbrComposite_Compiles_WithToggleVariants(
        string lumOnEnabled, string pbrComposite, string enableBentNormal)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = lumOnEnabled,
            ["VGE_LUMON_PBR_COMPOSITE"] = pbrComposite,
            ["VGE_LUMON_ENABLE_BENT_NORMAL"] = enableBentNormal,
        };

        var result = helper.CompileAndLink("pbr_composite.vsh", "pbr_composite.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("0", "1")]
    [InlineData("1", "0")]
    [InlineData("1", "1")]
    public void LumOnUpsample_Compiles_WithToggleVariants(
        string denoiseEnabled, string holeFillEnabled)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_UPSAMPLE_DENOISE"] = denoiseEnabled,
            ["VGE_LUMON_UPSAMPLE_HOLEFILL"] = holeFillEnabled,
        };

        var result = helper.CompileAndLink("lumon_upsample.vsh", "lumon_upsample.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LumOnTemporal_Compiles_WithVelocityReprojectionVariants(string velocityReprojection)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_TEMPORAL_USE_VELOCITY_REPROJECTION"] = velocityReprojection,
        };

        var result = helper.CompileAndLink("lumon_temporal.vsh", "lumon_temporal.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("1", "4")]
    [InlineData("16", "32")]
    public void LumOnProbeTrace_Compiles_WithLoopBoundVariants(string raysPerProbe, string raySteps)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_RAYS_PER_PROBE"] = raysPerProbe,
            ["VGE_LUMON_RAY_STEPS"] = raySteps,
        };

        var result = helper.CompileAndLink("lumon_probe_anchor.vsh", "lumon_probe_trace.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("8", "8")]
    [InlineData("64", "16")]
    public void LumOnProbeAtlasTrace_Compiles_WithLoopBoundVariants(string texelsPerFrame, string raySteps)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame,
            ["VGE_LUMON_RAY_STEPS"] = raySteps,
        };

        var result = helper.CompileAndLink("lumon_probe_anchor.vsh", "lumon_probe_atlas_trace.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("8")]
    [InlineData("64")]
    public void LumOnProbeAtlasTemporal_Compiles_WithTexelDistributionVariants(string texelsPerFrame)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame,
        };

        var result = helper.CompileAndLink("lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }
}
