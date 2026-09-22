using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.Helpers;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks typed selection and bounded loading without runtime metadata assets.</summary>
public sealed class SpirvStageLoaderTests
{
    #region Selection and bounded loading
    /// <summary>Reads only the selected binary slice and leaves its backing storage unchanged.</summary>
    [Fact]
    public void SlicedBinaryRequiresNoManifestOrSourceAssets()
    {
        var requests = new List<string>();
        var assets = new SlicedShaderAssets(path => { requests.Add(path); return new byte[20]; });
        var selected = Select("tests/render_infrastructure", ShaderStageKind.Fragment);
        var error = Assert.Throws<InvalidOperationException>(() => SpirvStageLoader.Load(selected, assets.Read));
        Assert.Contains("Invalid SPIR-V binary header", error.Message);
        Assert.Equal(new[] { "tests/render_infrastructure.fsh.spv" }, requests);
        assets.AssertUnchanged();
    }

    /// <summary>Numeric tuning changes typed arguments without affecting the selected structural binary.</summary>
    [Fact]
    public void NumericSpecializationsDoNotChangeBinarySelection()
    {
        var first = Select("lumon_probe_atlas_trace", ShaderStageKind.Fragment, new() { ["VGE_LUMON_WORLDPROBE_ENABLED"] = "true", ["VGE_LUMON_RAY_STEPS"] = "10" });
        var second = Select("lumon_probe_atlas_trace", ShaderStageKind.Fragment, new() { ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1", ["VGE_LUMON_RAY_STEPS"] = "20" });
        Assert.Equal(first.BinaryPath, second.BinaryPath);
        Assert.NotEqual(first.Specializations.Single(s => s.Id == 4).Value, second.Specializations.Single(s => s.Id == 4).Value);
        Assert.Equal(32, GpuShaderContracts.Registry.Binaries.Count(s => s.Stage.Identity == first.Stage.Identity));
    }

    /// <summary>Unsupported structural values fail while preparing the snapshot, before any asset read.</summary>
    [Fact]
    public void UnsupportedStructuralValueFailsBeforeRead()
    {
        Assert.Throws<ArgumentException>(() => SpirvStageLoader.Load(Select("lumon_combine", ShaderStageKind.Fragment,
            new() { ["VGE_LUMON_ENABLED"] = "2" }), _ => throw new Exception("Unexpected asset read")));
    }

    /// <summary>Explicit unknown program identities fail before asset access; callers do not infer identities from source names.</summary>
    [Fact]
    public void UnknownProgramFailsBeforeRead()
    {
        Assert.Throws<ArgumentException>(() => SpirvStageLoader.Load(Select("tests/render_infrastructure.fsh", ShaderStageKind.Fragment),
            _ => throw new Exception("Unexpected asset read")));
    }

    /// <summary>Conditional constants follow the same structural branch in emission and loading.</summary>
    [Fact]
    public void ImportanceSamplingConstantsFollowDeclaredConditions()
    {
        Assert.Equal(2, Select("lumon_probe_atlas_pis_mask", ShaderStageKind.Fragment).Specializations.Count);
        var values = new Dictionary<string, string?> { ["VGE_LUMON_PROBE_PIS_ENABLED"] = "1" };
        Assert.Equal(5, Select("lumon_probe_atlas_pis_mask", ShaderStageKind.Fragment, values).Specializations.Count);
        values["VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK"] = "1";
        Assert.Equal(2, Select("lumon_probe_atlas_pis_mask", ShaderStageKind.Fragment, values).Specializations.Count);
    }

    /// <summary>Resolves one explicit program snapshot through the production registry.</summary>
    private static ShaderStageSelection Select(string program, ShaderStageKind kind, Dictionary<string, string?>? values = null) =>
        GpuShaderContracts.Registry.Resolve(new ShaderSettings(GpuShaderContracts.Registry.FindProgram(program), values)).Single(s => s.Stage.Kind == kind);
    #endregion
}
