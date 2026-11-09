using VanillaGraphicsExpanded.Rendering.Shaders;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Checks declaration, demand loading and replacement lifetimes for production debug programs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnDebugShaderDemandTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region World-view contracts
    /// <summary>Enumerates every view that accepts world-probe topology rather than only its disabled default variant.</summary>
    public static IEnumerable<object[]> WorldViews() => LumOnDebugShaderProgram.Contracts
        .Where(contract => contract.Groups.Contains(LumOnShaderGroups.World))
        .Select(contract => new object[] { contract.Identity });

    /// <summary>Enabled topology specializes only constants retained by each independently optimized view binary.</summary>
    [Theory]
    [MemberData(nameof(WorldViews))]
    public void EnabledWorldViewsSpecializeTheirCompiledInterfaces(string identity)
    {
        // Arrange one production declaration with enabled world topology for the requested debug view.
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
        // Act by compiling the selected variant, then assert it is a live linked program with no GL error.
        Assert.True(program.EnsureReady(), string.Join('\n', assets.Logs));
        // Assert the requested variant produced a valid executable and left the GL context clean.
        Assert.True(GL.IsProgram(program.ProgramId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Selection and reuse

    /// <summary>Only the selected per-mode member loads and unchanged selections reuse their executable.</summary>
    [Theory]
    [InlineData("lumon_debug_view_direct_diffuse")]
    [InlineData("lumon_debug_view_direct_specular")]
    public void SelectedProgramLoadsOnceAndReusesCurrentSettings(string name)
    {
        // Arrange the family and choose one view; the other registered declarations must remain unused.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet(name, out var selected));
        Assert.True(selected.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" }));
        // Act: load the selected view once and verify its requested settings reached the installed executable.
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected), string.Join('\n', assets.Logs));
        // Assert the first load selected only this program and installed the requested define.
        Assert.Same(selected, Assert.Single(assets.PreparedPrograms).Value);
        Assert.Equal("1", selected.InstalledSettings!.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.True(GL.IsProgram(selected.ProgramId));
        Assert.All(assets.Reads.Where(path => path.EndsWith(".spv", StringComparison.Ordinal)), path => Assert.True(path == "shaders/lumon_debug.vsh.spv" || path.StartsWith($"shaders/{name}.", StringComparison.Ordinal) || path.StartsWith($"shaders/variants/{name}.", StringComparison.Ordinal), path));
        int reads = assets.Reads.Count;
        int installed = selected.ProgramId;
        // Act again without changing settings; reuse must not read assets or replace the executable.
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        // Assert this later selection reuses the executable and performs no additional asset work.
        Assert.Equal(installed, selected.ProgramId);
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Empty(assets.ScheduledTasks);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Replacement and recovery

    /// <summary>Settings edits to a previously selected inactive program wait for selection before reading or compiling.</summary>
    [Fact]
    public void InactiveCompiledProgramDefersSettingsReplacement()
    {
        // Arrange two registered views and install the first before changing its requested settings while inactive.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        int reads = assets.Reads.Count;
        // Act: the inactive setting change is deferred; selecting the view later performs the replacement.
        selected.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" });
        // Assert the inactive edit is retained without triggering reads or replacing the executable.
        Assert.Empty(assets.ScheduledTasks);
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Equal(installed, selected.ProgramId);
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        // Assert selecting the changed view installs its retained define in a replacement executable.
        Assert.NotEqual(installed, selected.ProgramId);
        Assert.Equal("1", selected.InstalledSettings!.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Invalidated owners replace their executable on demand while unused declarations retain their settings.</summary>
    [Fact]
    public void AssetInvalidationReplacesSelectionWithoutLoadingUnusedPrograms()
    {
        // Arrange a selected executable and a configured-but-unused sibling declaration.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_specular", out var unused));
        unused.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" });
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        // Act: invalidate only the selected executable and request it again; the sibling must stay uncompiled.
        selected.InvalidateAssets();
        int reads = assets.Reads.Count;
        Assert.Equal(reads, assets.Reads.Count);
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected), string.Join('\n', assets.Logs));
        // Assert the selected executable is valid and the sibling remains configured but unloaded.
        Assert.False(selected.IsRetired);
        Assert.True(GL.IsProgram(selected.ProgramId));
        Assert.Single(assets.PreparedPrograms);
        Assert.Equal("1", unused.RequestedSettings.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.Equal(0, unused.ProgramId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Reload marks declarations stale without linking; selection retains requested settings and replaces the executable.</summary>
    [Fact]
    public void ReloadDefersReplacementUntilSelection()
    {
        // Arrange an installed view with retained settings before starting a shader-library reload generation.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        selected.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" });
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        int reads = assets.Reads.Count;
        // Act: reload marks declarations stale without reading binaries; selecting the view then installs a replacement.
        GpuShaderProgramLibraries.Require(assets.Api).BeginReload();
        GpuShaderProgramLibraries.Require(assets.Api).CompleteReload();
        Assert.Equal(reads, assets.Reads.Count);
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var replacement));
        Assert.Equal("1", replacement.RequestedSettings.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, replacement));
        // Assert on-demand selection reads the replacement and retires the previous executable.
        Assert.True(assets.Reads.Count > reads);
        Assert.NotEqual(installed, replacement.ProgramId);
        Assert.False(GL.IsProgram(installed));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A failed replacement retains its valid executable and retries only after a new reload generation.</summary>
    [Fact]
    public void FailedReplacementRetainsExecutableAndBoundsRetries()
    {
        // Arrange a valid executable, then force the replacement generation to fail with an invalid binary.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        assets.Overrides["shaders/lumon_debug_view_direct_diffuse.fsh.spv"] = new byte[20];
        GpuShaderProgramLibraries.Require(assets.Api).BeginReload();
        GpuShaderProgramLibraries.Require(assets.Api).CompleteReload();
        // Act: reject the malformed replacement while retaining the last valid executable.
        Assert.False(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        Assert.Equal(installed, selected.ProgramId);
        Assert.True(GL.IsProgram(installed));
        int reads = assets.Reads.Count;
        // Repeated observations in the same failed generation must not trigger additional reads or links.
        for (int index = 0; index < 3; index++) Assert.False(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        Assert.Equal(reads, assets.Reads.Count);
        // Advance the reload generation with valid input; only this transition may retry and replace the executable.
        assets.Overrides.Clear();
        GpuShaderProgramLibraries.Require(assets.Api).BeginReload();
        GpuShaderProgramLibraries.Require(assets.Api).CompleteReload();
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        // Assert a new executable replaces the retained one only after a valid reload generation.
        Assert.NotEqual(installed, selected.ProgramId);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Program disposal

    /// <summary>Disposal releases selected executables and unused declarations without creating GPU work.</summary>
    [Fact]
    public void DisposalReleasesSelectedAndUnusedPrograms()
    {
        // Arrange one selected executable alongside unused declarations, then dispose the production program family.
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_view_direct_diffuse", out var selected));
        Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, selected));
        int installed = selected.ProgramId;
        int reads = assets.Reads.Count;
        // Act: family disposal releases the executable without loading the other declarations.
        LumOnDebugShaderProgramFamily.Dispose(assets.Api);
        // Assert disposal releases selected ownership without loading the remaining declarations.
        Assert.Empty(LumOnDebugShaderProgramFamily.GetAll());
        Assert.False(GL.IsProgram(installed));
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }


    #endregion
    #endregion
}
