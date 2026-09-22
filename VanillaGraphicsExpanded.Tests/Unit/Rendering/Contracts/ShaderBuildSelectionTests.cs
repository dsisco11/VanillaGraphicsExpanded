using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks the typed stage selections consumed by the builder, including finite program subsets and shared sources.</summary>
public sealed class ShaderBuildSelectionTests
{
    #region Build projections
    /// <summary>Restricted program rows project onto stages without multiplying shared vertex binaries.</summary>
    [Fact]
    public void SupportedProgramRowsDeduplicateProjectedStageConfigurations()
    {
        var vertexOption = new ShaderOption<bool>("VERTEX_FEATURE", false);
        var fragmentOption = new ShaderOption<bool>("FRAGMENT_FEATURE", false);
        var vertex = new ShaderStageContract("shared.vsh", "shared.vsh", ShaderStageKind.Vertex, new(), [vertexOption]);
        var fragment = new ShaderStageContract("surface.fsh", "surface.fsh", ShaderStageKind.Fragment, new(), [fragmentOption]);
        IReadOnlyDictionary<string, string>[] supported = [
            new Dictionary<string, string> { [vertexOption.Name] = "0", [fragmentOption.Name] = "0" },
            new Dictionary<string, string> { [vertexOption.Name] = "1", [fragmentOption.Name] = "0" },
            new Dictionary<string, string> { [vertexOption.Name] = "1", [fragmentOption.Name] = "1" }
        ];
        var first = new GpuShaderContract("first", [vertex, fragment], 3, [vertexOption, fragmentOption], supportedAssignments: supported);
        var alternate = new GpuShaderContract("alternate", [vertex, fragment], 3, [vertexOption, fragmentOption], supportedAssignments: supported);
        var plan = new ShaderVariantResolver([first, alternate]);
        Assert.Equal(6, plan.Programs.Values.Sum(p => p.Assignments.Count));
        Assert.Equal(4, plan.Binaries.Count);
        Assert.Equal(2, plan.Binaries.Count(b => b.Stage.Kind == ShaderStageKind.Vertex));
        Assert.Equal(plan.Binaries.Select(b => b.BinaryPath), new ShaderVariantResolver([alternate, first]).Binaries.Select(b => b.BinaryPath));
        Assert.Throws<ArgumentException>(() => plan.Resolve(new ShaderSettings(first).With(fragmentOption, true)));
    }

    /// <summary>Stage identity, source, kind and entry point remain separate contract fields for compilation.</summary>
    [Fact]
    public void SharedSourceCanHaveDistinctFixedConfigurationsAndBinaryIdentities()
    {
        var first = new ShaderStageContract("one.csh", "common.source", ShaderStageKind.Compute, new(),
            fixedDefines: new Dictionary<string, ShaderScalar> { ["FIXED_MODE"] = ShaderScalar.From(1) }, entryPoint: "computeEntry");
        var second = new ShaderStageContract("two.csh", "common.source", ShaderStageKind.Compute, new(),
            fixedDefines: new Dictionary<string, ShaderScalar> { ["FIXED_MODE"] = ShaderScalar.From(2) }, entryPoint: "computeEntry");
        var plan = new ShaderVariantResolver([new GpuShaderContract("one", [first], 1), new GpuShaderContract("two", [second], 1)]);
        Assert.Equal(2, plan.Binaries.Count);
        Assert.Equal(new[] { "one.csh.spv", "two.csh.spv" }, plan.Binaries.Select(b => b.BinaryPath));
        Assert.All(plan.Binaries, b => { Assert.Equal("common.source", b.Stage.Source); Assert.Equal("computeEntry", b.Stage.EntryPoint); });
    }
    /// <summary>A stage with two structural options compiles only the program's three supported rows, never its four-row Cartesian product.</summary>
    [Fact]
    public void SameStageSubsetExcludesUnsupportedBinary()
    {
        var first = new ShaderOption<bool>("FIRST", false);
        var second = new ShaderOption<bool>("SECOND", false);
        var vertex = new ShaderStageContract("shared.vsh", "shared.vsh", ShaderStageKind.Vertex, new());
        var fragment = new ShaderStageContract("subset.fsh", "subset.fsh", ShaderStageKind.Fragment, new(), [first, second]);
        IReadOnlyDictionary<string, string>[] supported = [
            new Dictionary<string, string> { [first.Name] = "0", [second.Name] = "0" },
            new Dictionary<string, string> { [first.Name] = "1", [second.Name] = "0" },
            new Dictionary<string, string> { [first.Name] = "1", [second.Name] = "1" }
        ];
        var program = new GpuShaderContract("subset", [vertex, fragment], 3, [first, second], supportedAssignments: supported);
        var plan = new ShaderVariantResolver([program]);
        Assert.Equal(4, plan.Binaries.Count);
        Assert.Single(plan.Binaries, b => b.Stage.Kind == ShaderStageKind.Vertex);
        Assert.Equal(3, plan.Binaries.Count(b => b.Stage.Kind == ShaderStageKind.Fragment));
        Assert.DoesNotContain(plan.Binaries, b => b.Key == "FIRST=0;SECOND=1");
    }
    #endregion
}
