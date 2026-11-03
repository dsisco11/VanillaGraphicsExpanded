using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Helpers;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.RHI;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies shader variants compile with injected and default preprocessor definitions.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class ShaderDefineInjectionTests
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates define-injection tests using the shared headless context.</summary>
    /// <param name="fixture">The headless graphics fixture.</param>
    public ShaderDefineInjectionTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Verifies shader assets compile and link with the configured injected defines.</summary>
    [Fact]
    public void Shaders_CompileAndLink_WithInjectedDefines()
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        // Note: these defines may not be referenced yet by the shaders under test.
        // This test primarily validates the injection mechanism and that shaders still compile.
        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = "0",
            ["VGE_LUMON_PBR_COMPOSITE"] = "1",
        };

        using var shader = PrepareProgram("lumon_combine", defines);
        Assert.True(shader.Program.ProgramId > 0, "Program ID should be valid");
    }

    /// <summary>Verifies global defaults compile when no per-program define overrides are supplied.</summary>
    [Fact]
    public void GlobalDefinesInclude_Defaults_CompileWithoutInjection()
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        using var shader = PrepareProgram("tests/vge_global_defines_smoke");
        Assert.True(shader.Program.ProgramId > 0, "Program ID should be valid");
    }

    /// <summary>Verifies debug world-probe variants compile for each enabled value.</summary>
    /// <param name="enabledValue">The world-probe define value.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LumOnDebugWorldProbe_Compiles_WithWorldProbeEnabledVariant(string enabledValue)
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        // World-probe program uses compile-time gating; compile representative enabled/disabled variants.
        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = enabledValue,
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = enabledValue == "1" ? "2" : "0",
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = enabledValue == "1" ? "16" : "0",
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = enabledValue == "1" ? "4.0" : "0.0",
        };

        using var shader = PrepareProgram("lumon_debug_view_world_probe_irradiance_combined", defines);
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Verifies LumOn combine variants compile for each feature define value.</summary>
    /// <param name="enabledValue">The LumOn define value.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LumOnCombine_Compiles_WithVGE_LUMON_ENABLED_Variant(string enabledValue)
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = enabledValue,
        };

        using var shader = PrepareProgram("lumon_combine", defines);
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Verifies LumOn combine variants compile against the selected PBR composite variants.</summary>
    /// <param name="pbrComposite">The PBR composite variant value.</param>
    /// <param name="enableAO">The ambient occlusion variant value.</param>
    /// <param name="enableShortRangeAo">The short-range ambient occlusion variant value.</param>
    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "1", "0")]
    [InlineData("1", "1", "1")]
    public void LumOnCombine_Compiles_WithPbrCompositeVariants(
        string pbrComposite, string enableAO, string enableShortRangeAo)
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = "1",
            ["VGE_LUMON_PBR_COMPOSITE"] = pbrComposite,
            ["VGE_LUMON_ENABLE_AO"] = enableAO,
            ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = enableShortRangeAo,
        };

        using var shader = PrepareProgram("lumon_combine", defines);
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Verifies PBR composite shaders compile across the supported toggle combinations.</summary>
    /// <param name="lumOnEnabled">The LumOn variant value.</param>
    /// <param name="pbrComposite">The PBR composite variant value.</param>
    /// <param name="enableShortRangeAo">The short-range ambient occlusion variant value.</param>
    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "1", "0")]
    [InlineData("1", "1", "1")]
    public void PbrComposite_Compiles_WithToggleVariants(
        string lumOnEnabled, string pbrComposite, string enableShortRangeAo)
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = lumOnEnabled,
            ["VGE_LUMON_PBR_COMPOSITE"] = pbrComposite,
            ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = enableShortRangeAo,
        };

        using var shader = PrepareProgram("pbr_composite", defines);
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Verifies the LumOn upsample shader compiles across its supported toggle combinations.</summary>
    /// <param name="denoiseEnabled">The denoising variant value.</param>
    /// <param name="holeFillEnabled">The hole-fill variant value.</param>
    [Theory]
    [InlineData("0", "0")]
    [InlineData("0", "1")]
    [InlineData("1", "0")]
    [InlineData("1", "1")]
    public void LumOnUpsample_Compiles_WithToggleVariants(
        string denoiseEnabled, string holeFillEnabled)
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_UPSAMPLE_DENOISE"] = denoiseEnabled,
            ["VGE_LUMON_UPSAMPLE_HOLEFILL"] = holeFillEnabled,
        };

        using var shader = PrepareProgram("lumon_upsample", defines);
        Assert.True(shader.Program.ProgramId > 0);
    }

    // Note: Legacy SH temporal/trace shaders were removed.
    // The current atlas pipeline has its own temporal pass (`lumon_probe_atlas_temporal`), but does not yet
    // do velocity-based reprojection/disocclusion handling (tracked in project.todo).

    /// <summary>Verifies probe-atlas tracing compiles with the supplied loop bounds.</summary>
    /// <param name="texelsPerFrame">The configured texel count per frame.</param>
    /// <param name="raySteps">The configured ray-step count.</param>
    [Theory]
    [InlineData("8", "8")]
    [InlineData("64", "16")]
    public void LumOnProbeAtlasTrace_Compiles_WithLoopBoundVariants(string texelsPerFrame, string raySteps)
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame,
            ["VGE_LUMON_RAY_STEPS"] = raySteps,
        };

        using var shader = PrepareProgram("tests/trace_probe_anchor", defines);
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Verifies probe-atlas temporal variants compile with each texel distribution.</summary>
    /// <param name="texelsPerFrame">The configured texel count per frame.</param>
    [Theory]
    [InlineData("8")]
    [InlineData("64")]
    public void LumOnProbeAtlasTemporal_Compiles_WithTexelDistributionVariants(string texelsPerFrame)
    {
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame,
        };

        using var shader = PrepareProgram("lumon_probe_atlas_temporal", defines);
        Assert.True(shader.Program.ProgramId > 0);
    }

    #endregion

    #region Private

    /// <summary>Creates a declared shader variant and retains its ready production owner.</summary>
    private static RHIShader PrepareProgram(string identity, IReadOnlyDictionary<string, string?>? defines = null)
    {
        var contract = GpuShaderContracts.Registry.FindProgram(identity);
        using var candidate = new ShaderAssetCandidate(new(new ShaderSettings(contract, defines)),
            path => TestAssetSnapshot.Built.Read("shaders/" + path),
            () => ShaderBinaryDigest.Parse(TestAssetSnapshot.Built.Copy("shaders/" + ShaderBinaryDigest.FileName)));
        candidate.Complete();
        return candidate.TakeShader();
    }
    #endregion
}
