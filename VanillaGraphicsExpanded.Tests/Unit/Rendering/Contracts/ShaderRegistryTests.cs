using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderOptions;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks the owned registry against source coverage and the reviewed migration baseline.</summary>
public sealed class ShaderRegistryTests
{
    private static ShaderVariantResolver Registry => GpuShaderContracts.Registry;

    #region Coverage
    /// <summary>Every packaged entry point is explicitly registered and every declaration has source.</summary>
    [Fact]
    public void SourcesAndRegistryCoverEachOther()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ShaderBuildTool", "Program.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string root = Path.Combine(directory.FullName, "VanillaGraphicsExpanded", "assets", "vanillagraphicsexpanded", "shaders");
        string[] extensions = [".vsh", ".fsh", ".csh", ".gsh", ".tcsh", ".tesh"];
        var sources = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => extensions.Contains(Path.GetExtension(p)))
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith("includes/", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        Assert.Equal(sources, Registry.Stages.Values.Select(s => s.Source).Order(StringComparer.Ordinal));
        Assert.Equal(63, Registry.Programs.Count);
        Assert.Equal(98, Registry.Stages.Count);
        Assert.Equal(243, Registry.Binaries.Count);
        Assert.Equal(242, Registry.Programs.Values.Sum(p => p.Assignments.Count));
        Assert.Equal(243, Registry.Binaries.Select(b => b.BinaryPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>Explicit empty contracts remain distinct from unknown identities and validator-only assets.</summary>
    [Fact]
    public void EmptyAndIsolatedProgramsAreExplicit()
    {
        Assert.Empty(Registry.FindProgram("lumon_velocity").Options);
        Assert.Empty(Registry.FindProgram("lumonscene_capture_voxel").Options);
        Assert.Throws<ArgumentException>(() => Registry.FindProgram("not_registered"));
        Assert.Throws<ArgumentException>(() => GpuShaderContracts.CreateStage("not_registered.fsh"));
        Assert.Throws<ArgumentException>(() => GpuShaderContracts.CreateStage("fixture.fsh"));
        var isolated = BuildValidationShaderPrograms.Create();
        Assert.Equal(2, isolated.Programs.Count);
        Assert.Equal(3, isolated.Stages.Count);
        Assert.Equal(3, isolated.Binaries.Count);
        Assert.All(isolated.Programs.Values, p => Assert.Empty(p.Options));
        Assert.Equal(2, BuildValidationShaderPrograms.Create(false).Stages.Count);
        Assert.Throws<ArgumentException>(() => isolated.FindStage("lumon_combine.fsh"));
    }

    /// <summary>Shared vertices and alternate fixture pairs reuse the exact registered stage instances.</summary>
    [Theory]
    [InlineData("lumon_probe_atlas_pis_mask", "lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_pis_mask.fsh", 8)]
    [InlineData("pbr_heightbake_copy", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_copy.fsh", 1)]
    [InlineData("tests/framebuffer_blend", "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh", "tests/framebuffer_blend.fsh", 1)]
    [InlineData("tests/pbr_direct_fullscreen", "tests/fullscreen_uv.vsh", "pbr_direct_lighting.fsh", 1)]
    [InlineData("tests/trace_probe_anchor", "lumon_probe_anchor.vsh", "lumon_probe_atlas_trace.fsh", 32)]
    [InlineData("tests/worldprobe_debug", "lumon_debug.vsh", "lumon_debug_worldprobe.fsh", 4)]
    public void PairingsAreDeclared(string program, string vertex, string fragment, int budget)
    {
        var contract = Registry.FindProgram(program);
        Assert.Same(Registry.FindStage(vertex), contract.Stages[0]);
        Assert.Same(Registry.FindStage(fragment), contract.Stages[1]);
        Assert.Equal(budget, contract.VariantBudget);
        Assert.Equal(budget, contract.Assignments.Count);
    }

    /// <summary>Every registered configuration retains default paths, fixed build profile and adapter agreement.</summary>
    [Fact]
    public void AllSelectionsAgreeWithTransitionalConsumers()
    {
        foreach (var program in Registry.Programs.Values)
        {
            Assert.Equal(program.VariantBudget, program.Assignments.Count);
            Assert.All(Registry.Resolve(new(program)), s => Assert.Equal(s.Stage.Identity + ".spv", s.BinaryPath));
            foreach (var stage in program.Stages)
            {
                Assert.Same(Registry.FindStage(stage.Identity), stage);
                Assert.Equal("main", stage.EntryPoint);
                Assert.Equal(ShaderScalar.From(1), stage.FixedDefines["VGE_SPIRV_BUILD"]);
            }
            foreach (var assignment in program.Assignments)
            {
                var values = assignment.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical);
                var settings = new ShaderSettings(program, values);
                foreach (var selected in Registry.Resolve(settings))
                {
                    var adapter = GpuShaderContracts.CreateStage(selected.Stage.Identity);
                    Assert.Equal(selected.BinaryPath, adapter.BinaryPath(selected.Stage.Identity, values));
                    Assert.Equal(selected.Specializations.Select(s => s.Id), adapter.Constants(values).Select(s => (uint)s.Id));
                }
            }
        }
    }
    #endregion

    #region Settings and availability
    /// <summary>Global membership filters irrelevant known settings while direct program input stays strict.</summary>
    [Fact]
    public void GlobalProjectionUsesDeclaredGroups()
    {
        var values = new Dictionary<string, string?>
        {
            [AmbientOcclusion.Name] = "0", [PbrComposite.Name] = "0",
            ["VGE_LUMON_ENABLE_BENT_NORMAL"] = "0", [WorldProbeResolution.Name] = "4"
        };
        var composite = ShaderGlobalSettings.Project(Registry, "pbr_composite", values);
        Assert.False(composite.Values.ContainsKey(AmbientOcclusion.Name));
        Assert.Equal(ShaderScalar.From(false), composite.Values[ShortRangeAo.Name]);
        var world = ShaderGlobalSettings.Project(Registry, "lumon_debug_worldprobe", values);
        Assert.Equal(ShaderScalar.From(4), world.Values[WorldProbeResolution.Name]);
        Assert.Empty(Registry.Resolve(world)[0].Specializations);
        Assert.Empty(Registry.Resolve(world)[1].Specializations); // inactive values remain selected
        Assert.Throws<ArgumentException>(() => new ShaderSettings(world.Contract, new Dictionary<string, string?> { ["UNKNOWN"] = "1" }));
        Assert.Throws<ArgumentException>(() => new ShaderSettings(world.Contract, new Dictionary<string, string?> { [PbrComposite.Name] = "1" }));
        values["UNKNOWN"] = "1";
        Assert.Throws<ArgumentException>(() => ShaderGlobalSettings.Project(Registry, "lumon_velocity", values));
        values.Remove("UNKNOWN");
        values[ShortRangeAo.Name] = "1";
        Assert.Throws<ArgumentException>(() => ShaderGlobalSettings.Project(Registry, "lumon_velocity", values));
    }

    /// <summary>All trace and PIS branches keep their reviewed specialization availability.</summary>
    [Fact]
    public void ConditionsMatchTheLightingBranches()
    {
        var trace = Registry.FindProgram("lumon_probe_atlas_trace");
        foreach (var assignment in trace.Assignments)
        {
            var settings = new ShaderSettings(trace, assignment.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical));
            var ids = Registry.Resolve(settings)[1].Specializations.Select(s => s.Id).ToArray();
            Assert.Equal(settings.Values[NearField.Name].Bits == 0, ids.Contains(6u));
            Assert.Equal(settings.Values[WorldProbes.Name].Bits == 1, ids.Contains(11u));
            Assert.DoesNotContain(15u, ids);
        }
        var mask = Registry.FindProgram("lumon_probe_atlas_pis_mask");
        foreach (var assignment in mask.Assignments)
        {
            var settings = new ShaderSettings(mask, assignment.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical));
            bool exploration = settings.Values[ImportanceSampling.Name].Bits == 1 &&
                settings.Values[BatchSlicing.Name].Bits == 0 && settings.Values[UniformMask.Name].Bits == 0;
            Assert.Equal(exploration ? 5 : 2, Registry.Resolve(settings)[1].Specializations.Count);
        }
        Assert.Equal(new uint[] { 13, 14 }, Registry.Resolve(new(Registry.FindProgram("vge_worldprobe_orbs_points")))[1].Specializations.Select(s => s.Id));
        Assert.Equal(-1, unchecked((int)ExploreCount.Default.Bits));
        Assert.Equal(ShaderScalar.From(0), WorldProbeResolution.Default);
        Assert.Equal(ShaderScalar.From(2), WorldProbeDiffuseStride.Default);
    }

    /// <summary>Locally ineffective writes are rejected rather than declared as functional settings.</summary>
    [Theory]
    [InlineData("lumon_velocity", "LUMON_EMISSIVE_BOOST")]
    [InlineData("pbr_composite", "VGE_LUMON_ENABLE_AO")]
    [InlineData("pbr_composite", "VGE_PBR_DEBUG_VIEW_MODE")]
    [InlineData("lumon_probe_atlas_trace", "VGE_LUMON_PROBE_PIS_EXPLORE_COUNT")]
    [InlineData("lumon_probe_atlas_temporal", "VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK")]
    [InlineData("lumon_probe_atlas_trace", "VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE")]
    [InlineData("vge_worldprobe_orbs_points", "VGE_LUMON_WORLDPROBE_ENABLED")]
    [InlineData("lumon_probe_atlas_gather", "VGE_LUMON_WORLDPROBE_ATLAS_TEXELS_PER_UPDATE")]
    [InlineData("lumon_debug", "VGE_LUMON_BIND_WORLDPROBE_RADIANCE_ATLAS")]
    public void IneffectiveOptionsAreNotRegistered(string program, string option) =>
        Assert.Throws<ArgumentException>(() => Registry.FindProgram(program).FindOption(option));
    #endregion
}
