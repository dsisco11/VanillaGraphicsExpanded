using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.Helpers;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks contract-driven selection and bounded loading without runtime metadata assets.</summary>
public sealed class SpirvStageLoaderTests
{
    #region Selection and bounded loading
    /// <summary>Reads only the selected binary slice and leaves its backing storage unchanged.</summary>
    [Fact]
    public void SlicedBinaryRequiresNoManifestOrSourceAssets()
    {
        var requests = new List<string>();
        var assets = new SlicedShaderAssets(path =>
        {
            requests.Add(path);
            return new byte[20]; // Invalid header stops before any graphics-context access.
        });
        var error = Assert.Throws<InvalidOperationException>(() => SpirvStageLoader.Load(
            "tests/render_infrastructure.fsh", ShaderType.FragmentShader, null, assets.Read));
        Assert.Contains("Invalid SPIR-V binary header", error.Message);
        Assert.Equal(new[] { "tests/render_infrastructure.fsh.spv" }, requests);
        assets.AssertUnchanged();
    }

    /// <summary>Numeric tuning does not change structural variant selection, and equivalent values normalize alike.</summary>
    [Fact]
    public void NumericSpecializationsDoNotChangeBinarySelection()
    {
        var stage = GpuShaderContracts.CreateStage("lumon_probe_atlas_trace.fsh");
        var first = new Dictionary<string, string?> { ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1.0", ["VGE_LUMON_RAY_STEPS"] = "10" };
        var second = new Dictionary<string, string?> { ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1", ["VGE_LUMON_RAY_STEPS"] = "20" };
        Assert.Equal(stage.BinaryPath("lumon_probe_atlas_trace.fsh", first), stage.BinaryPath("lumon_probe_atlas_trace.fsh", second));
        Assert.Equal(32, stage.Variants().Count());
    }

    /// <summary>Unsupported structural values fail before reading an asset.</summary>
    [Fact]
    public void UnsupportedStructuralValueFailsBeforeRead()
    {
        Assert.Throws<ArgumentException>(() => SpirvStageLoader.Load("lumon_combine.fsh", ShaderType.FragmentShader,
            new Dictionary<string, string?> { ["VGE_LUMON_ENABLED"] = "2" }, _ => throw new Exception("Unexpected asset read")));
    }

    /// <summary>Stage identity is checked against the source extension before accessing assets.</summary>
    [Fact]
    public void MismatchedStageFailsBeforeRead()
    {
        Assert.Throws<ArgumentException>(() => SpirvStageLoader.Load("tests/render_infrastructure.fsh", ShaderType.VertexShader,
            null, _ => throw new Exception("Unexpected asset read")));
    }

    /// <summary>Conditional constants follow the same structural branch in emission and loading.</summary>
    [Fact]
    public void ImportanceSamplingConstantsFollowDeclaredConditions()
    {
        var stage = GpuShaderContracts.CreateStage("lumon_probe_atlas_pis_mask.fsh");
        Assert.Equal(2, stage.Constants(null).Count());
        var settings = new Dictionary<string, string?> { ["VGE_LUMON_PROBE_PIS_ENABLED"] = "1" };
        Assert.Equal(5, stage.Constants(settings).Count());
        settings["VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK"] = "1";
        Assert.Equal(2, stage.Constants(settings).Count());
    }
    #endregion
}
