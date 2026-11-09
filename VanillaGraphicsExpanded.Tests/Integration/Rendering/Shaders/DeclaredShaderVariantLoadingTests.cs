using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.RHI;
using Xunit;



namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Verifies declared shader settings select valid built SPIR-V variants for native loading.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class DeclaredShaderVariantLoadingTests
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates variant-loading checks using the shared headless context.</summary>
    /// <param name="fixture">The headless graphics fixture.</param>
    public DeclaredShaderVariantLoadingTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Loads the declared LumOn combine binary selected by per-program settings.</summary>
    [Fact]
    public void LumOnCombineLoads_WithPerProgramSettings()
    {
        // Arrange: Select the declared combine identity and provide per-program settings without preprocessing source.
        _fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        // The settings select a declared built variant; this test does not preprocess shader source.
        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_ENABLED"] = "0",
            ["VGE_LUMON_PBR_COMPOSITE"] = "1",
        };

        // Act: Prepare the binary for the selected settings.
        using var shader = PrepareProgram("lumon_combine", defines);
        // Assert: The declared program owns a live native executable.
        Assert.True(shader.Program.ProgramId > 0, "Program ID should be valid");
    }

    /// <summary>Loads a declared binary using global defaults without per-program overrides.</summary>
    [Fact]
    public void DefaultDeclaredVariantLoads_WithoutPerProgramOverrides()
    {
        // Arrange: Use the built global defaults without supplying per-program settings.
        _fixture.EnsureContextValid();

        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        // Act: Prepare the declared smoke program from its packaged binary.
        using var shader = PrepareProgram("tests/vge_global_defines_smoke");
        // Assert: Preparation publishes a valid native program.
        Assert.True(shader.Program.ProgramId > 0, "Program ID should be valid");
    }

    /// <summary>Loads each declared world-probe debug variant.</summary>
    /// <param name="enabledValue">The world-probe define value.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void WorldProbeDebugVariantLoads_ForEachFeatureState(string enabledValue)
    {
        // Arrange: Select one enabled or disabled world-probe feature state and its corresponding authored bounds.
        _fixture.EnsureContextValid();

        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        // The world-probe feature is baked into its selected binary; exercise one enabled and one disabled declaration.
        var defines = new Dictionary<string, string?>
        {
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = enabledValue,
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = enabledValue == "1" ? "2" : "0",
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = enabledValue == "1" ? "16" : "0",
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = enabledValue == "1" ? "4.0" : "0.0",
        };

        // Act: Prepare the declared world-probe debug binary for the selected state.
        using var shader = PrepareProgram("lumon_debug_view_world_probe_irradiance_combined", defines);
        // Assert: The selected packaged variant has a live native executable.
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Loads each declared LumOn combine feature state.</summary>
    /// <param name="enabledValue">The LumOn define value.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LumOnCombineLoads_ForEachFeatureState(string enabledValue)
    {
        // Arrange: Select the enabled or disabled LumOn setting for the declared combine program.
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

        // Act: Prepare the binary matching that feature state.
        using var shader = PrepareProgram("lumon_combine", defines);
        // Assert: The selected declaration produces a valid native program.
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Loads LumOn combine binaries for the selected PBR composite settings.</summary>
    /// <param name="pbrComposite">The PBR composite variant value.</param>
    /// <param name="enableAO">The ambient occlusion variant value.</param>
    /// <param name="enableShortRangeAo">The short-range ambient occlusion variant value.</param>
    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "1", "0")]
    [InlineData("1", "1", "1")]
    public void LumOnCombineLoads_WithPbrCompositeSettings(
        string pbrComposite, string enableAO, string enableShortRangeAo)
    {
        // Arrange: Select PBR composite, AO, and short-range AO settings under enabled LumOn.
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

        // Act: Prepare the declared combine variant with those settings.
        using var shader = PrepareProgram("lumon_combine", defines);
        // Assert: The selected packaged variant has a live native executable.
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Loads PBR composite binaries selected by the supported toggle settings.</summary>
    /// <param name="lumOnEnabled">The LumOn variant value.</param>
    /// <param name="pbrComposite">The PBR composite variant value.</param>
    /// <param name="enableShortRangeAo">The short-range ambient occlusion variant value.</param>
    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "1", "0")]
    [InlineData("1", "1", "1")]
    public void PbrCompositeLoads_WithToggleSettings(
        string lumOnEnabled, string pbrComposite, string enableShortRangeAo)
    {
        // Arrange: Supply the supported LumOn, composite, and short-range AO toggle combination.
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

        // Act: Prepare the packaged composite binary for the selected settings.
        using var shader = PrepareProgram("pbr_composite", defines);
        // Assert: The declared variant produces a valid native program.
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Loads LumOn upsample binaries selected by their supported toggle settings.</summary>
    /// <param name="denoiseEnabled">The denoising variant value.</param>
    /// <param name="holeFillEnabled">The hole-fill variant value.</param>
    [Theory]
    [InlineData("0", "0")]
    [InlineData("0", "1")]
    [InlineData("1", "0")]
    [InlineData("1", "1")]
    public void LumOnUpsampleLoads_WithToggleSettings(
        string denoiseEnabled, string holeFillEnabled)
    {
        // Arrange: Select denoise and hole-fill settings for the declared upsample program.
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

        // Act: Prepare the packaged upsample binary for the selected settings.
        using var shader = PrepareProgram("lumon_upsample", defines);
        // Assert: The selected declaration has a valid native executable.
        Assert.True(shader.Program.ProgramId > 0);
    }

    // Note: Legacy SH temporal/trace shaders were removed.
    // The current atlas pipeline has its own temporal pass (`lumon_probe_atlas_temporal`), but does not yet
    // do velocity-based reprojection/disocclusion handling (tracked in project.todo).

    /// <summary>Loads the probe-anchor tracing binary selected by the supplied loop bounds.</summary>
    /// <param name="texelsPerFrame">The configured texel count per frame.</param>
    /// <param name="raySteps">The configured ray-step count.</param>
    [Theory]
    [InlineData("8", "8")]
    [InlineData("64", "16")]
    public void ProbeAnchorTraceLoads_WithLoopBoundSettings(string texelsPerFrame, string raySteps)
    {
        // Arrange: Select representative atlas texel and ray-step loop bounds.
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

        // Act: Prepare the declared probe-anchor trace binary for those bounds.
        using var shader = PrepareProgram("tests/trace_probe_anchor", defines);
        // Assert: Preparation yields a valid native program.
        Assert.True(shader.Program.ProgramId > 0);
    }

    /// <summary>Loads each probe-atlas temporal binary selected by its texel distribution.</summary>
    /// <param name="texelsPerFrame">The configured texel count per frame.</param>
    [Theory]
    [InlineData("8")]
    [InlineData("64")]
    public void ProbeAtlasTemporalLoads_WithTexelDistribution(string texelsPerFrame)
    {
        // Arrange: Select one packaged atlas texel distribution.
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

        // Act: Prepare the declared temporal binary for that distribution.
        using var shader = PrepareProgram("lumon_probe_atlas_temporal", defines);
        // Assert: The selected packaged variant has a valid native executable.
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
