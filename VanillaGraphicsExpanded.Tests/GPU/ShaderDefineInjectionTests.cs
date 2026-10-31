using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class ShaderDefineInjectionTests
{
    private readonly HeadlessGLFixture _fixture;

    public ShaderDefineInjectionTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Shaders_CompileAndLink_WithInjectedDefines()
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

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

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

        var result = helper.CompileAndLink(
            "tests/vge_global_defines_smoke.vsh",
            "tests/vge_global_defines_smoke.fsh");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.ProgramId > 0, "Program ID should be valid");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LumOnDebugWorldProbe_Compiles_WithWorldProbeEnabledVariant(string enabledValue)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

        // World-probe program uses compile-time gating; compile representative enabled/disabled variants.
        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = enabledValue,
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = enabledValue == "1" ? "2" : "0",
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = enabledValue == "1" ? "16" : "0",
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = enabledValue == "1" ? "4.0" : "0.0",
        };

        var result = helper.CompileProgram("lumon_debug_view_world_probe_irradiance_combined", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LumOnCombine_Compiles_WithVGE_LUMON_ENABLED_Variant(string enabledValue)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

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
        string pbrComposite, string enableAO, string enableShortRangeAo)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = "1",
            ["VGE_LUMON_PBR_COMPOSITE"] = pbrComposite,
            ["VGE_LUMON_ENABLE_AO"] = enableAO,
            ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = enableShortRangeAo,
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
        string lumOnEnabled, string pbrComposite, string enableShortRangeAo)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = lumOnEnabled,
            ["VGE_LUMON_PBR_COMPOSITE"] = pbrComposite,
            ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = enableShortRangeAo,
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

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_UPSAMPLE_DENOISE"] = denoiseEnabled,
            ["VGE_LUMON_UPSAMPLE_HOLEFILL"] = holeFillEnabled,
        };

        var result = helper.CompileAndLink("lumon_upsample.vsh", "lumon_upsample.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    // Note: Legacy SH temporal/trace shaders were removed.
    // The current atlas pipeline has its own temporal pass (`lumon_probe_atlas_temporal`), but does not yet
    // do velocity-based reprojection/disocclusion handling (tracked in project.todo).

    [Theory]
    [InlineData("8", "8")]
    [InlineData("64", "16")]
    public void LumOnProbeAtlasTrace_Compiles_WithLoopBoundVariants(string texelsPerFrame, string raySteps)
    {
        _fixture.EnsureContextValid();

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame,
            ["VGE_LUMON_RAY_STEPS"] = raySteps,
        };

        var result = helper.CompileProgram("tests/trace_probe_anchor", defines);
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

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper();

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame,
        };

        var result = helper.CompileAndLink("lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh", defines);
        Assert.True(result.IsSuccess, result.ErrorMessage);
    }
}
