using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks declaration, demand loading and replacement lifetimes for production debug programs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnDebugShaderDemandTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Declaration and selection
    /// <summary>Enumerates every view that accepts world-probe topology rather than only its disabled default variant.</summary>
    public static IEnumerable<object[]> WorldViews() => LumOnDebugShaderProgram.Contracts
        .Where(contract => contract.Groups.Contains(LumOnShaderGroups.World))
        .Select(contract => new object[] { contract.Identity });

    /// <summary>Enabled topology specializes only constants retained by each independently optimized view binary.</summary>
    [Theory]
    [MemberData(nameof(WorldViews))]
    public void EnabledWorldViewsSpecializeTheirCompiledInterfaces(string identity)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = new LumOnDebugShaderProgram { PassName = identity };
        program.Initialize(assets.Api);
        program.SetDefines(new Dictionary<string, string?>
        {
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = "true",
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = "4",
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = "1.5"
        });
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
        Assert.True(GL.IsProgram(program.ProgramId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Declaring and configuring unused programs does not read assets, link, register or queue work.</summary>
    [Fact]
    public void UnusedDeclarationsRetainSettingsWithoutGpuWork()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        var declarations = LumOnDebugShaderProgramFamily.GetAll();
        Assert.Equal(68, declarations.Count());
        foreach (var program in declarations)
        {
            Assert.Equal(0, program.ProgramId);
            Assert.True(program.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" }));
            Assert.Equal("1", program.RequestedSettings.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        }
        Assert.Empty(assets.Reads);
        Assert.Empty(assets.RegisteredPrograms);
        Assert.Empty(assets.ScheduledTasks);
    }

    /// <summary>Only the selected per-mode member loads and unchanged selections reuse their executable.</summary>
    [Theory]
    [InlineData("lumon_debug_view_direct_diffuse")]
    [InlineData("lumon_debug_view_direct_specular")]
    public void SelectedProgramLoadsOnceAndReusesCurrentSettings(string name)
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet(name, out var selected));
        Assert.True(selected.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" }));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected), string.Join('\n', assets.Logs));
        Assert.Same(selected, Assert.Single(assets.RegisteredPrograms).Value);
        Assert.Equal("1", selected.InstalledSettings!.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.True(GL.IsProgram(selected.ProgramId));
        Assert.All(assets.Reads.Where(path => path.EndsWith(".spv", StringComparison.Ordinal)), path => Assert.True(path == "shaders/lumon_debug.vsh.spv" || path.StartsWith($"shaders/{name}.", StringComparison.Ordinal) || path.StartsWith($"shaders/variants/{name}.", StringComparison.Ordinal), path));
        int reads = assets.Reads.Count;
        int installed = selected.ProgramId;
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        Assert.Equal(installed, selected.ProgramId);
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Empty(assets.ScheduledTasks);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Reload and failure ownership
    /// <summary>Settings edits to a previously selected inactive program wait for selection before reading or compiling.</summary>
    [Fact]
    public void InactiveCompiledProgramDefersSettingsReplacement()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        int reads = assets.Reads.Count;
        selected.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" });
        Assert.Empty(assets.ScheduledTasks);
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Equal(installed, selected.ProgramId);
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        Assert.NotEqual(installed, selected.ProgramId);
        Assert.Equal("1", selected.InstalledSettings!.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Engine-disposed registered owners are recreated on demand while unused declarations retain their settings.</summary>
    [Fact]
    public void EngineReloadRestoresDisposedSelectionWithoutLoadingUnusedPrograms()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_specular", out var unused));
        unused.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" });
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        ((Vintagestory.Client.NoObf.ShaderProgramBase)selected).Dispose();
        assets.RegisteredPrograms.Clear();
        int reads = assets.Reads.Count;
        Assert.Equal(reads, assets.Reads.Count);
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected), string.Join('\n', assets.Logs));
        Assert.False(selected.Disposed);
        Assert.True(GL.IsProgram(selected.ProgramId));
        Assert.Single(assets.RegisteredPrograms);
        Assert.Equal("1", unused.RequestedSettings.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.Equal(0, unused.ProgramId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Reload marks declarations stale without linking; selection retains requested settings and replaces the executable.</summary>
    [Fact]
    public void ReloadDefersReplacementUntilSelection()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        selected.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" });
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        int reads = assets.Reads.Count;
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.Equal(reads, assets.Reads.Count);
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var replacement));
        Assert.Equal("1", replacement.RequestedSettings.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, replacement));
        Assert.True(assets.Reads.Count > reads);
        Assert.NotEqual(installed, replacement.ProgramId);
        Assert.False(GL.IsProgram(installed));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A failed replacement retains its valid executable and retries only after a new reload generation.</summary>
    [Fact]
    public void FailedReplacementRetainsExecutableAndBoundsRetries()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        assets.Overrides["shaders/lumon_debug_view_direct_diffuse.fsh.spv"] = new byte[20];
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.False(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        Assert.Equal(installed, selected.ProgramId);
        Assert.True(GL.IsProgram(installed));
        int reads = assets.Reads.Count;
        for (int index = 0; index < 3; index++) Assert.False(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        Assert.Equal(reads, assets.Reads.Count);
        assets.Overrides.Clear();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        Assert.NotEqual(installed, selected.ProgramId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Disposal releases selected executables and unused declarations without creating GPU work.</summary>
    [Fact]
    public void DisposalReleasesSelectedAndUnusedPrograms()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        int reads = assets.Reads.Count;
        LumOnDebugShaderProgramFamily.Dispose(assets.Api);
        Assert.Empty(LumOnDebugShaderProgramFamily.GetAll());
        Assert.False(GL.IsProgram(installed));
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
