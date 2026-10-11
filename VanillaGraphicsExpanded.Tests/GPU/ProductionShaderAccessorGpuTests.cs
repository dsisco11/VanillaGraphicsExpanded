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
    /// <summary>Synchronous asset callbacks cannot allocate a replacement under a changed context registration.</summary>
    [Fact]
    public void ContextChangeDuringAssetReadsPreservesInstalledExecutable()
    {
        EnsureContextValid();
        using var cache = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = Create(assets);
        Assert.True(program.EnsureReady());
        int installed = program.ProgramId;
        ulong revision = program.ExecutableRevision;
        program.InvalidateAssets();
        var original = VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Current();
        var registrations = (System.Collections.IDictionary)typeof(VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry)
            .GetField("registrations", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        object registration = registrations[original.Handle]!;
        var replacement = new object();
        bool changed = false;
        assets.BeforeRead = _ =>
        {
            if (changed) return;
            changed = true;
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
        };
        try
        {
            Assert.False(program.EnsureReady());
            Assert.Contains("originating native context generation", program.PreparationFailure!.Message);
            Assert.Equal(installed, program.ProgramId);
            Assert.Equal(revision, program.ExecutableRevision);
            Assert.True(GL.IsProgram(installed));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            // Restore the live originating registration before owner retirement.
            lock (registrations) registrations[original.Handle] = registration;
            assets.BeforeRead = null;
        }
    }

    /// <summary>Retirement during binary preparation cannot publish or leak a replacement after terminal cleanup.</summary>
    [Fact]
    public void RetirementDuringPreparationCannotInstallAnotherExecutable()
    {
        EnsureContextValid();
        using var cache = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = Create(assets);
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
        int installed = program.ProgramId;
        ulong revision = program.ExecutableRevision;
        program.InvalidateAssets();
        bool retired = false;
        assets.BeforeRead = _ =>
        {
            if (retired) return;
            retired = true;
            program.Dispose();
        };
        Assert.False(program.EnsureReady());
        Assert.True(program.IsRetired);
        Assert.Equal(0, program.ProgramId);
        Assert.Equal(revision, program.ExecutableRevision);
        Assert.False(GL.IsProgram(installed));
        program.Dispose();
        Assert.False(program.CompileAndLink());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Asset invalidation during preparation discards the obsolete candidate without suppressing the new asset epoch.</summary>
    [Fact]
    public void AssetEpochSupersessionRetainsInstalledGenerationAndPermitsRetry()
    {
        EnsureContextValid();
        using var cache = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = Create(assets);
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
        int installed = program.ProgramId;
        ulong revision = program.ExecutableRevision;
        ulong epoch = program.InstalledAssetGeneration;
        program.InvalidateAssets();
        bool changed = false;
        assets.BeforeRead = _ =>
        {
            if (changed) return;
            changed = true;
            program.InvalidateAssets();
        };
        Assert.False(program.EnsureReady());
        Assert.Equal(installed, program.ProgramId);
        Assert.Equal(revision, program.ExecutableRevision);
        Assert.Equal(epoch, program.InstalledAssetGeneration);
        Assert.True(program.RequiresPreparation);
        Assert.True(GL.IsProgram(installed));
        assets.BeforeRead = null;
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
        Assert.Equal(revision + 1, program.ExecutableRevision);
        Assert.Equal(epoch + 2, program.InstalledAssetGeneration);
        Assert.False(GL.IsProgram(installed));
        Assert.Null(program.PreparationFailure);
    }

    /// <summary>A typed structural update selects a real PBR variant and preserves the installed program if loading fails.</summary>
    [Fact]
    public void CompositeGeneratedAccessorSelectsBinaryAndPreservesFailedReplacement()
    {
        EnsureContextValid();
        using var cache = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var program = new PBRCompositeShaderProgram
        {
            PassName = PBRCompositeShaderProgram.Contract.Identity
        };
        program.Initialize(assets.Api);
        try
        {
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            int first = program.ProgramId;
            var firstLayout = program.ResourceBindings;
            program.EnableShortRangeAo = false;
            Assert.Empty(assets.ScheduledTasks);
            Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
            assets.ScheduledTasks.Clear();
            Assert.NotEqual(first, program.ProgramId);
            Assert.False(GL.IsProgram(first));
            Assert.Same(firstLayout, program.ResourceBindings);
            program.SetUnderwater(true);
            int installed = program.ProgramId;
            ulong installedRevision = program.ExecutableRevision;
            var installedInterface = program.GraphicsInterface;
            var installedSettings = program.InstalledSettings;
            var installedLayout = program.ResourceBindings;
            program.EnableShortRangeAo = true;
            assets.Overrides["shaders/pbr_composite.fsh.spv"] = new byte[20];
            Assert.Empty(assets.ScheduledTasks);
            Assert.False(program.EnsureReady());
            assets.ScheduledTasks.Clear();
            Assert.Equal(installed, program.ProgramId);
            Assert.Equal(installedRevision, program.ExecutableRevision);
            Assert.Same(installedInterface, program.GraphicsInterface);
            Assert.True(GL.IsProgram(installed));
            Assert.Same(installedSettings, program.InstalledSettings);
            Assert.Same(installedLayout, program.ResourceBindings);
            var pending = program.RequestedSettings;
            Assert.False(program.ConfigureOptions(() => program.EnableShortRangeAo = true));
            Assert.Same(pending,program.RequestedSettings);
            Assert.NotSame(installedSettings,pending);
            Assert.False(program.EnsureReady());
            // A genuinely different requested variant can prepare even while the
            // previously selected default binary is still unavailable.
            program.PreOverlayOnly = true;
            Assert.True(program.EnsureReady(),string.Join('\n',assets.Logs));
            Assert.NotEqual(installed,program.ProgramId);
            Assert.Same(program.RequestedSettings,program.InstalledSettings);
            program.PreOverlayOnly = false;
            Assert.False(program.EnsureReady());
            pending = program.RequestedSettings;
            assets.Overrides.Clear();
            // Asset reload clears the remembered failure; identical requested
            // options must not hide that pending readiness work.
            program.InvalidateAssets();
            Assert.False(program.ConfigureOptions(() => program.EnableShortRangeAo = true));
            Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
            Assert.Same(pending,program.InstalledSettings);
        }
        finally { program.Dispose(); }
    }
    /// <summary>Retained edits that revert before preparation do no work; explicit disposal permanently retires the owner.</summary>
    [Fact]
    public void RevertedSettingsAndRetiredOwnersDoNotLoad()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = Create(assets);
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        int installed = program.ProgramId;
        assets.Reads.Clear();
        program.EnableShortRangeAo = false;
        program.EnableShortRangeAo = true;
        Assert.Empty(assets.ScheduledTasks);
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
        assets.ScheduledTasks.Clear();
        Assert.Empty(assets.Reads);
        Assert.Equal(installed, program.ProgramId);
        Assert.False(program.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_ENABLE_SHORT_RANGE_AO"] = null }));
        Assert.Empty(assets.ScheduledTasks);
        program.EnableShortRangeAo = false;
        program.Dispose();
        Assert.Empty(assets.ScheduledTasks);
        Assert.False(program.EnsureReady());
        Assert.Empty(assets.Reads);
        Assert.Null(program.InstalledSettings);
        Assert.False(GL.IsProgram(installed));
        program.Dispose();
        Assert.False(program.EnsureReady());
    }

    /// <summary>A setting changed by an asset callback cannot mix settings between the two stages of one load.</summary>
    [Fact]
    public void AssetReadMutationRejectsSupersededSnapshotThenPreparesLatest()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = Create(assets);
        using var cache = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        bool changed = false;
        assets.BeforeRead = path =>
        {
            if (changed || !path.EndsWith(".spv", StringComparison.Ordinal)) return;
            changed = true;
            program.EnableShortRangeAo = false;
        };
        Assert.False(program.CompileAndLink());
        Assert.True(changed);
        Assert.Equal(0, program.ProgramId);
        Assert.Null(program.InstalledSettings);
        Assert.Equal("0", program.RequestedSettings.Values["VGE_LUMON_ENABLE_SHORT_RANGE_AO"].Canonical);
        Assert.Contains("shaders/pbr_composite.fsh.spv", assets.Reads);
        int installed = program.ProgramId;
        Assert.Empty(assets.ScheduledTasks);
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
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
            PassName = LumOnScreenProbeAtlasTraceShaderProgram.Contract.Identity
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
        Assert.Empty(assets.ScheduledTasks);
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
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
            PassName = LumOnScreenProbeAtlasTraceShaderProgram.Contract.Identity
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
    /// <summary>Bulk edits configure before initialization, coalesce retained changes and replace only the final generation.</summary>
    [Fact]
    public void BulkUpdatesPublishOnlyCompleteInstalledGenerations()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = new PBRCompositeShaderProgram
        {
            PassName = PBRCompositeShaderProgram.Contract.Identity
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
        Assert.Empty(assets.ScheduledTasks);
        program.ConfigureOptions(() => program.EnableShortRangeAo = false);
        Assert.Empty(assets.ScheduledTasks);
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
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
        var stable = program.RequestedSettings;
        var stableInterface = program.ProgramLayout.BinaryInterface;
        int reads = assets.Reads.Count;
        Action unchanged = () => { program.EnablePbrComposite = true; program.EnableShortRangeAo = false; };
        for (int frame = 0; frame < 128; frame++)
        {
            Assert.False(program.ConfigureOptions(unchanged));
            Assert.True(program.EnsureReady());
            Assert.Same(stable,program.RequestedSettings);
            Assert.Same(stable,program.InstalledSettings);
        }
        Assert.Equal(second,program.ProgramId);
        Assert.Same(stableInterface,program.ProgramLayout.BinaryInterface);
        Assert.Equal(reads,assets.Reads.Count);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Creates the real production owner with fixture API assets.</summary>
    private static PBRCompositeShaderProgram Create(BinaryShaderApiFixture assets)
    {
        var program = new PBRCompositeShaderProgram
        {
            PassName = PBRCompositeShaderProgram.Contract.Identity
        };
        program.Initialize(assets.Api);
        return program;
    }
    #endregion
}
