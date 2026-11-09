using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderOptions;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks production registry declarations, settings projection, and selection rules.</summary>
public sealed class ShaderRegistryTests
{
    #region Public API
    #region Coverage
    /// <summary>Explicit empty contracts remain distinct from unknown identities and validator-only assets.</summary>
    [Fact]
    public void EmptyAndIsolatedProgramsAreExplicit()
    {
        // Arrange: Select the production registry and an isolated build-validation scope.
        var isolated = BuildValidationShaderPrograms.Create();
        // Act: Query empty/unknown production declarations and resolve isolated identities.
        Assert.Empty(Registry.FindProgram("lumon_velocity").Options);
        Assert.Empty(Registry.FindProgram("lumonscene_capture_voxel").Options);
        Assert.Throws<ArgumentException>(() => Registry.FindProgram("not_registered"));
        Assert.Throws<ArgumentException>(() => Registry.FindStage("not_registered.fsh"));
        Assert.Throws<ArgumentException>(() => Registry.FindStage("fixture.fsh"));
        // Assert: Empty, isolated, and unknown identities retain their distinct outcomes.
        Assert.Equal(2, isolated.Programs.Count);
        Assert.Equal(3, isolated.Stages.Count);
        Assert.Equal(3, isolated.Binaries.Count);
        Assert.All(isolated.Programs.Values, p => Assert.Empty(p.Options));
        Assert.Equal(2, BuildValidationShaderPrograms.Create(false).Stages.Count);
        Assert.Throws<ArgumentException>(() => isolated.FindStage("lumon_combine.fsh"));
    }

    /// <summary>Shared vertices and alternate fixture pairs reuse the exact registered stage instances.</summary>
    [Theory]
    [InlineData("lumon_probe_atlas_pis_mask", "lumon_probe_atlas_pis_mask.vsh", "lumon_probe_atlas_pis_mask.fsh", 8)]
    [InlineData("pbr_heightbake_copy", "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_copy.fsh", 1)]
    [InlineData("tests/framebuffer_blend", "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh", "tests/framebuffer_blend.fsh", 1)]
    [InlineData("tests/pbr_direct_fullscreen", "tests/fullscreen_uv.vsh", "pbr_direct_lighting.fsh", 1)]
    [InlineData("tests/trace_probe_anchor", "tests/trace_probe_anchor.vsh", "lumon_probe_atlas_trace.fsh", 32)]
    [InlineData("tests/worldprobe_debug", "lumon_debug.vsh", "lumon_debug_view_world_probe_irradiance_combined.fsh", 4)]
    [InlineData("cloud_simple", "cloud_simple.vsh", "cloud_simple.fsh", 1)]
    [InlineData("cloud_volume/view", "cloud_volume/view.vsh", "cloud_volume/view.fsh", 24)]
    [InlineData("cloud_volume/temporal", "cloud_volume/view.vsh", "cloud_volume/temporal.fsh", 2)]
    public void PairingsAreDeclared(string program, string vertex, string fragment, int budget)
    {
        // Arrange: xUnit supplies one declared program/stage pairing and its variant budget.
        // Act: Resolve the program and each shared stage from the production registry.
        var contract = Registry.FindProgram(program);
        // Assert: Pairings share registry-owned instances and keep the declared budgets.
        Assert.Same(Registry.FindStage(vertex), contract.Stages[0]);
        Assert.Same(Registry.FindStage(fragment), contract.Stages[1]);
        Assert.Equal(budget, contract.VariantBudget);
        Assert.Equal(budget, contract.Assignments.Count);
    }

    /// <summary>Every registered configuration retains default paths, fixed build profile and compiled selection agreement.</summary>
    [Fact]
    public void AllSelectionsRemainDeclaredAndDeterministic()
    {
        // Arrange: Enumerate each registered program and all declared assignments.
        // Act: Resolve stage paths, fixed defines, and compiled binary selections.
        foreach (var program in Registry.Programs.Values)
        {
            // Assert: Every selected result remains declared and deterministic.
            Assert.InRange(program.Assignments.Count, 1, program.VariantBudget);
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
                    var compiled = Assert.Single(Registry.Binaries, b => b.Stage.Identity == selected.Stage.Identity && b.Key == selected.Key);
                    Assert.Equal(compiled.BinaryPath, selected.BinaryPath);
                    Assert.Equal(compiled.Specializations.Select(s => s.Id), selected.Specializations.Select(s => s.Id));
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
        // Arrange: Supply known global settings shared by programs with different option groups.
        var values = new Dictionary<string, string?>
        {
            [AmbientOcclusion.Name] = "0", [PbrComposite.Name] = "0",
            ["VGE_LUMON_ENABLE_BENT_NORMAL"] = "0", [WorldProbeResolution.Name] = "4"
        };
        // Act: Project the globals into two program-specific immutable settings snapshots.
        var composite = ShaderGlobalSettings.Project(Registry, "pbr_composite", values);
        // Assert: Irrelevant globals are filtered, aliases normalize, and direct input stays strict.
        Assert.False(composite.Values.ContainsKey(AmbientOcclusion.Name));
        Assert.Equal(ShaderScalar.From(false), composite.Values[ShortRangeAo.Name]);
        var world = ShaderGlobalSettings.Project(Registry, "lumon_debug_view_world_probe_irradiance_combined", values);
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
        // Arrange: Select the trace and PIS mask contracts and their complete assignment sets.
        var trace = Registry.FindProgram("lumon_probe_atlas_trace");
        // Act: Resolve each trace branch against the condition declarations.
        // Assert: Specialization IDs match the declared conditional availability.
        foreach (var assignment in trace.Assignments)
        {
            var settings = new ShaderSettings(trace, assignment.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical));
            var ids = Registry.Resolve(settings)[1].Specializations.Select(s => s.Id).ToArray();
            Assert.Equal(settings.Values[NearField.Name].Bits == 0, ids.Contains(6u));
            Assert.Equal(settings.Values[WorldProbes.Name].Bits == 1, ids.Contains(11u));
            Assert.DoesNotContain(15u, ids);
        }
        // Act: Resolve PIS specialization availability for each assignment.
        // Assert: Only the fully enabled exploration branch exposes all five specializations.
        var mask = Registry.FindProgram("lumon_probe_atlas_pis_mask");
        foreach (var assignment in mask.Assignments)
        {
            var settings = new ShaderSettings(mask, assignment.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical));
            bool exploration = settings.Values[ImportanceSampling.Name].Bits == 1 &&
                settings.Values[BatchSlicing.Name].Bits == 0 && settings.Values[UniformMask.Name].Bits == 0;
            Assert.Equal(exploration ? 5 : 2, Registry.Resolve(settings)[1].Specializations.Count);
        }
        // Assert: Fixed IDs and defaults remain stable on the separate points shader.
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
    [InlineData("lumon_debug_view_world_probe_irradiance_combined", "VGE_LUMON_BIND_WORLDPROBE_RADIANCE_ATLAS")]
    public void IneffectiveOptionsAreNotRegistered(string program, string option)
    {
        // Arrange: xUnit supplies a program/option pair that cannot affect a compiled shader.
        // Act: Ask the program contract to resolve the ineffective option.
        // Assert: Unregistered options fail instead of becoming nominal settings.
        Assert.Throws<ArgumentException>(() => Registry.FindProgram(program).FindOption(option));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Uses the generated production registry for the owned shader declaration checks.</summary>
    private static ShaderVariantResolver Registry => GpuShaderContracts.Registry;
    #endregion
}
