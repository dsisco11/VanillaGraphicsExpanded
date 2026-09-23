using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks one real generated production accessor through binary loading and replacement ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ProductionShaderAccessorGpuTests : RenderTestBase
{
    /// <summary>Uses the existing shared headless context and production asset fixture.</summary>
    public ProductionShaderAccessorGpuTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Production loading
    /// <summary>A typed structural update selects a real PBR variant and preserves the installed program if loading fails.</summary>
    [Fact]
    public void CompositeGeneratedAccessorSelectsBinaryAndPreservesFailedReplacement()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var program = new PBRCompositeShaderProgram
        {
            PassName = PBRCompositeShaderProgram.Contract.Identity,
            VertexShader = new Vintagestory.Client.NoObf.Shader(),
            FragmentShader = new Vintagestory.Client.NoObf.Shader()
        };
        program.Initialize(assets.Api);
        try
        {
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            int first = program.ProgramId;
            var firstLayout = program.ResourceBindings;
            program.EnableShortRangeAo = false;
            Assert.Single(assets.ScheduledTasks)();
            assets.ScheduledTasks.Clear();
            Assert.NotEqual(first, program.ProgramId);
            Assert.False(GL.IsProgram(first));
            Assert.Same(firstLayout, program.ResourceBindings);
            program.FogDensityIn = 0.125f;
            int installed = program.ProgramId;
            var installedSettings = program.InstalledSettings;
            var installedLayout = program.ResourceBindings;
            program.EnableShortRangeAo = true;
            assets.Overrides["shaders/pbr_composite.fsh.spv"] = new byte[20];
            Assert.Single(assets.ScheduledTasks)();
            assets.ScheduledTasks.Clear();
            Assert.Equal(installed, program.ProgramId);
            Assert.True(GL.IsProgram(installed));
            Assert.Same(installedSettings, program.InstalledSettings);
            Assert.Same(installedLayout, program.ResourceBindings);
            assets.Overrides.Clear();
            Assert.True(program.Compile(), string.Join('\n', assets.Logs));
        }
        finally { program.Dispose(); }
    }
    /// <summary>Queued writes which return to installed inputs do no asset or GPU work; disposal cancels pending work.</summary>
    [Fact]
    public void CoalescedRevertedSettingsAndDisposedCallbacksDoNotLoad()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = Create(assets);
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        int installed = program.ProgramId;
        assets.Reads.Clear();
        program.EnableShortRangeAo = false;
        program.EnableShortRangeAo = true;
        Assert.Single(assets.ScheduledTasks)();
        assets.ScheduledTasks.Clear();
        Assert.Empty(assets.Reads);
        Assert.Equal(installed, program.ProgramId);
        Assert.False(program.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = null }));
        Assert.Empty(assets.ScheduledTasks);
        program.EnableShortRangeAo = false;
        program.Dispose();
        Assert.Single(assets.ScheduledTasks)();
        Assert.Empty(assets.Reads);
        Assert.Null(program.InstalledSettings);
        Assert.False(GL.IsProgram(installed));
        program.Dispose();
        for (int generation = 0; generation < 2; generation++)
        {
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            Assert.False(program.Disposed);
            Assert.NotNull(program.InstalledSettings);
            int reloaded = program.ProgramId;
            program.Dispose();
            program.Dispose();
            Assert.False(GL.IsProgram(reloaded));
        }
    }

    /// <summary>A setting changed by an asset callback cannot mix settings between the two stages of one load.</summary>
    [Fact]
    public void AssetReadMutationInstallsOneSnapshotThenQueuesNewSnapshot()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = Create(assets);
        bool changed = false;
        assets.BeforeRead = path =>
        {
            if (changed || !path.EndsWith(".spv", StringComparison.Ordinal)) return;
            changed = true;
            program.EnableShortRangeAo = false;
        };
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.True(changed);
        Assert.Equal("1", program.InstalledSettings!.Values["VGE_LUMON_ENABLE_SHORT_RANGE_AO"].Canonical);
        Assert.Equal("0", program.RequestedSettings.Values["VGE_LUMON_ENABLE_SHORT_RANGE_AO"].Canonical);
        Assert.Contains("shaders/pbr_composite.fsh.spv", assets.Reads);
        int installed = program.ProgramId;
        Assert.Single(assets.ScheduledTasks)();
        Assert.NotEqual(installed, program.ProgramId);
        Assert.Equal("0", program.InstalledSettings!.Values["VGE_LUMON_ENABLE_SHORT_RANGE_AO"].Canonical);
    }

    /// <summary>Inactive settings retain values without reads, while an active numeric change reloads the same binary path.</summary>
    [Fact]
    public void TraceNumericChangesReloadOnlyWhenEffective()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = new LumOnScreenProbeAtlasTraceShaderProgram
        {
            PassName = LumOnScreenProbeAtlasTraceShaderProgram.Contract.Identity,
            VertexShader = new Vintagestory.Client.NoObf.Shader(),
            FragmentShader = new Vintagestory.Client.NoObf.Shader()
        };
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        string[] firstReads = assets.Reads.Where(path => path.EndsWith(".spv", StringComparison.Ordinal)).ToArray();
        assets.Reads.Clear();
        program.WorldProbeResolution = 27;
        Assert.Empty(assets.ScheduledTasks);
        Assert.Empty(assets.Reads);
        Assert.Equal(27, program.WorldProbeResolution);
        int installed = program.ProgramId;
        program.RaySteps = 24;
        Assert.Single(assets.ScheduledTasks)();
        Assert.NotEqual(installed, program.ProgramId);
        Assert.Equal(firstReads, assets.Reads.Where(path => path.EndsWith(".spv", StringComparison.Ordinal)).ToArray());
        Assert.Equal("24", program.InstalledSettings!.Values["VGE_LUMON_RAY_STEPS"].Canonical);
        Assert.Equal("27", program.RequestedSettings.Values["VGE_LUMON_WORLDPROBE_RESOLUTION"].Canonical);
    }
    /// <summary>Representative application boundaries specialize and link without changing structural binary identity.</summary>
    [Theory]
    [InlineData(1, .25f, .01f, 0f, 0, 0f, 1)]
    [InlineData(512, 256f, 16f, 1f, 12, 64f, 64)]
    public void TraceApplicationNumericBoundariesLink(int steps, float distance, float thickness, float skyWeight, int mip, float emission, int texels)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = new LumOnScreenProbeAtlasTraceShaderProgram
        {
            PassName = LumOnScreenProbeAtlasTraceShaderProgram.Contract.Identity,
            VertexShader = new Vintagestory.Client.NoObf.Shader(),
            FragmentShader = new Vintagestory.Client.NoObf.Shader()
        };
        program.Initialize(assets.Api);
        program.ConfigureOptions(() =>
        {
            program.RaySteps = steps;
            program.RayMaxDistance = distance;
            program.RayThickness = thickness;
            program.SkyMissWeight = skyWeight;
            program.HzbCoarseMip = mip;
            program.EmissiveBoost = emission;
            program.TexelsPerFrame = texels;
        });
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        Assert.Contains("shaders/lumon_probe_atlas_trace.fsh.spv", assets.Reads);
        Assert.Equal(steps.ToString(System.Globalization.CultureInfo.InvariantCulture), program.InstalledSettings!.Values["VGE_LUMON_RAY_STEPS"].Canonical);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Bulk edits configure before initialization, coalesce queued changes and replace only the final generation.</summary>
    [Fact]
    public void BulkUpdatesPublishOnlyCompleteInstalledGenerations()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = new PBRCompositeShaderProgram
        {
            PassName = PBRCompositeShaderProgram.Contract.Identity,
            VertexShader = new Vintagestory.Client.NoObf.Shader(),
            FragmentShader = new Vintagestory.Client.NoObf.Shader()
        };
        program.ConfigureOptions(() => { program.EnableShortRangeAo = false; program.EnablePbrComposite = false; });
        Assert.Empty(assets.ScheduledTasks);
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        int first = program.ProgramId;
        var requested = program.RequestedSettings;
        var installed = program.InstalledSettings;
        Assert.Throws<ArgumentException>(() => program.ConfigureOptions(() =>
        {
            program.EnableShortRangeAo = true;
            program.SetDefines(new Dictionary<string, string?> { ["UNKNOWN"] = "1" });
        }));
        Assert.Same(requested, program.RequestedSettings);
        Assert.Same(installed, program.InstalledSettings);
        Assert.Empty(assets.ScheduledTasks);
        program.ConfigureOptions(() =>
        {
            program.EnableShortRangeAo = true;
            Assert.Empty(assets.ScheduledTasks);
            program.EnablePbrComposite = true;
            Assert.Equal(first, program.ProgramId);
        });
        Assert.Single(assets.ScheduledTasks);
        program.ConfigureOptions(() => program.EnableShortRangeAo = false);
        Assert.Single(assets.ScheduledTasks)();
        assets.ScheduledTasks.Clear();
        Assert.NotEqual(first, program.ProgramId);
        Assert.False(GL.IsProgram(first));
        Assert.Same(program.RequestedSettings, program.InstalledSettings);
        int second = program.ProgramId;
        Assert.False(program.ConfigureOptions(() =>
        {
            program.EnablePbrComposite = false;
            program.EnablePbrComposite = true;
        }));
        Assert.Empty(assets.ScheduledTasks);
        Assert.Equal(second, program.ProgramId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Creates the real production owner with fixture API assets.</summary>
    private static PBRCompositeShaderProgram Create(BinaryShaderApiFixture assets)
    {
        var program = new PBRCompositeShaderProgram
        {
            PassName = PBRCompositeShaderProgram.Contract.Identity,
            VertexShader = new Vintagestory.Client.NoObf.Shader(),
            FragmentShader = new Vintagestory.Client.NoObf.Shader()
        };
        program.Initialize(assets.Api);
        return program;
    }
    #endregion
}
