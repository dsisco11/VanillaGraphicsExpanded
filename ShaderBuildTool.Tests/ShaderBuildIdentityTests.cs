using System.Globalization;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Tests;

/// <summary>Checks identity boundaries against effective contracts, implementation files and compiler inputs.</summary>
public sealed class ShaderBuildIdentityTests
{
    #region Public API
    #region Assembly ownership
    /// <summary>The offline dependency closure has one model identity and excludes declaration data.</summary>
    [Fact]
    public void CatalogueIsSeparateFromProcessingClosure()
    {
        Assert.Equal("ShaderBuildModel", typeof(ShaderStageContract).Assembly.GetName().Name);
        Assert.Equal("ShaderBuildCatalog", typeof(GpuShaderContracts).Assembly.GetName().Name);
        string[] files = ShaderBuildIdentities.ImplementationFiles().ToArray();
        Assert.Contains(typeof(ShaderStageContract).Assembly.Location, files);
        Assert.DoesNotContain(typeof(GpuShaderContracts).Assembly.Location, files);
        Assert.DoesNotContain(typeof(ShaderStageContract).FullName!,
            typeof(Program).Assembly.GetTypes().Select(t => t.FullName));
        Assert.DoesNotContain(typeof(ShaderStageContract).FullName!,
            typeof(GpuShaderContracts).Assembly.GetTypes().Select(t => t.FullName));
    }

    #endregion

    #region Contract projection
    /// <summary>Every effective emitter input changes identity, independently of output association.</summary>
    [Theory]
    [InlineData("source")]
    [InlineData("stage")]
    [InlineData("entry")]
    [InlineData("fixed")]
    [InlineData("structural")]
    [InlineData("specialization-id")]
    [InlineData("specialization-name")]
    [InlineData("specialization-default")]
    [InlineData("specialization-type")]
    [InlineData("inactive")]
    public void EffectiveInputsInvalidateOnlyTheirContract(string change)
    {
        var before = Selection();
        var after = Selection(change);
        Assert.NotEqual(ShaderContractProjection.Effective(before), ShaderContractProjection.Effective(after));
        // A separate declaration retains the same projection; catalogue-wide changes are not inputs.
        Assert.Equal(ShaderContractProjection.Effective(before), ShaderContractProjection.Effective(Selection()));
    }

    /// <summary>All binding namespaces and descriptor fields are represented, including conservative policy inputs.</summary>
    [Theory]
    [InlineData("sampler")]
    [InlineData("image")]
    [InlineData("ubo")]
    [InlineData("ssbo")]
    [InlineData("atomic")]
    [InlineData("uniform-location")]
    [InlineData("varying-location")]
    [InlineData("output-location")]
    [InlineData("required")]
    [InlineData("array")]
    [InlineData("type")]
    [InlineData("target")]
    [InlineData("policy")]
    public void BindingInputsInvalidateEffectiveContract(string change)
    {
        var first = new GpuBindingContract(); first.Samplers.Add("texture", new(0, true));
        var second = new GpuBindingContract(); second.Samplers.Add("texture", new(0, true));
        switch (change)
        {
            case "sampler": second.Samplers["texture"] = new(1, true); break;
            case "image": second.Images.Add("image", new(1, true)); break;
            case "ubo": second.UniformBlocks.Add("Block", new(1, true)); break;
            case "ssbo": second.StorageBlocks.Add("Block", new(1, true)); break;
            case "atomic": second.AtomicCounters.Add("counter", new(1, true)); break;
            case "uniform-location": second.UniformLocations.Add("value", 1); break;
            case "varying-location": second.VaryingLocations.Add("value", 1); break;
            case "output-location": second.FragmentOutputLocations.Add("value", 1); break;
            case "required": second.Samplers["texture"] = new(0, false); break;
            case "array": second.Samplers["texture"] = new(0, true, 2); break;
            case "type": second.Samplers["texture"] = new(0, true, ShaderType: ShaderResourceType.Sampler2D); break;
            case "target": second.Samplers["texture"] = new(0, true, TextureTarget: 1); break;
            case "policy": second.Samplers["texture"] = new(0, true, Sampler: 1); break;
        }
        Assert.NotEqual(ShaderContractProjection.Effective(Selection(bindings: first)),
            ShaderContractProjection.Effective(Selection(bindings: second)));
    }

    /// <summary>Insertion order and culture do not change a canonical effective contract.</summary>
    [Fact]
    public void ProjectionIsOrdinalAndCultureIndependent()
    {
        var first = new GpuBindingContract(); first.UniformLocations.Add("z", 1); first.UniformLocations.Add("a", 2);
        var second = new GpuBindingContract(); second.UniformLocations.Add("a", 2); second.UniformLocations.Add("z", 1);
        byte[] expected = ShaderContractProjection.Effective(Selection(bindings: first));
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(expected, ShaderContractProjection.Effective(Selection(bindings: second)));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    /// <summary>Output relocation changes catalogue association without changing reusable source inputs.</summary>
    [Fact]
    public void MembershipAndScopeAreSeparateFromEffectiveSource()
    {
        var a = Selection(); var b = Selection("output");
        Assert.Equal(ShaderContractProjection.Effective(a), ShaderContractProjection.Effective(b));
        var first = new ShaderVariantResolver([new GpuShaderContract("program", [a.Stage], 2, a.Stage.Structural.Concat(a.Stage.Specializations.Select(s => s.Option)))]);
        var second = new ShaderVariantResolver([new GpuShaderContract("program", [b.Stage], 2, b.Stage.Structural.Concat(b.Stage.Specializations.Select(s => s.Option)))]);
        Assert.NotEqual(ShaderContractProjection.Membership(first, "production"), ShaderContractProjection.Membership(second, "production"));
        Assert.NotEqual(ShaderContractProjection.Membership(first, "production"), ShaderContractProjection.Membership(first, "tests"));
    }

    #endregion

    #region Processing and compiler identities
    /// <summary>Processing and reflection changes invalidate the receipt but not identical compiler inputs.</summary>
    [Fact]
    public void ProcessingAndInterfaceChangesPreserveCompilerEligibility()
    {
        var baseline = new ShaderBuildIdentities("pre", "emit", "compiler", "interface");
        var stage = Selection().Stage;
        var registry = new ShaderVariantResolver([new GpuShaderContract("program", [stage], 2,
            stage.Structural.Concat(stage.Specializations.Select(s => s.Option)))]);
        var cache = new ShaderVariantCache("unused", baseline.Compiler);
        foreach (var changed in new[] { baseline with { Preprocessing = "new" }, baseline with { Emission = "new" }, baseline with { Interface = "new" } })
        {
            Assert.NotEqual(baseline.Receipt(registry, "production"), changed.Receipt(registry, "production"));
            Assert.Equal(cache.Key("identical GLSL", "fragment", "main"),
                new ShaderVariantCache("unused", changed.Compiler).Key("identical GLSL", "fragment", "main"));
        }
    }

    /// <summary>Implementation identities use file content and schema rather than timestamps or input order.</summary>
    [Fact]
    public void ImplementationHashTracksContentsAndSchema()
    {
        using var fixture = new ShaderBuildFixture();
        string a = Path.Combine(fixture.Assets, "a.dll"), b = Path.Combine(fixture.Assets, "b.dll");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        string before = ShaderBuildIdentities.Files("schema", [a, b]);
        Assert.Equal(before, ShaderBuildIdentities.Files("schema", [b, a]));
        Assert.NotEqual(before, ShaderBuildIdentities.Files("new-schema", [a, b]));
        File.WriteAllText(b, "c");
        Assert.NotEqual(before, ShaderBuildIdentities.Files("schema", [a, b]));
    }

    /// <summary>The same policy builder supplies target, warning, optimization and debug arguments to execution.</summary>
    [Fact]
    public void CompilerPolicyAndPathContextAreExplicit()
    {
        string[] policy = ShaderCompilerProcess.PolicyArguments("fragment", "opengl4.5", false, "main");
        Assert.Contains("--shader-stage=fragment", policy); Assert.Contains("--entry-point=main", policy);
        Assert.Contains("--target-env=opengl4.5", policy);
        Assert.Throws<NotSupportedException>(() => ShaderCompilerProcess.PolicyArguments("fragment", "opengl4.5", true, "main"));
        Assert.Contains(ShaderCompilerProcess.OptimizationArgument, policy);
        Assert.Equal(ShaderCompilerProcess.GenerateDebugInfo, policy.Contains("-g"));
        Assert.DoesNotContain("-Werror", ShaderCompilerProcess.PolicyArguments("fragment", "opengl4.5", false, "main"));
        var cache = new ShaderVariantCache("unused", "compiler");
        string a = cache.Key("source", "fragment", "main", "a.glsl", "one");
        string b = cache.Key("source", "fragment", "main", "b.glsl", "two");
        Assert.Equal(!ShaderCompilerProcess.GenerateDebugInfo, a == b);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Constructs a resolved selection with independently mutable source, layout and specialization inputs.</summary>
    private static ShaderStageSelection Selection(string change = "", GpuBindingContract? bindings = null)
    {
        var structural = new ShaderOption<bool>("MODE", false);
        ShaderOption numeric = change == "specialization-type" ? new ShaderOption<int>("AMOUNT", 1) :
            new ShaderOption<float>(change == "specialization-name" ? "OTHER" : "AMOUNT", change == "specialization-default" ? 2f : 1f);
        var stage = new ShaderStageContract("fixture.csh", change == "source" ? "other.csh" : "fixture.csh",
            change == "stage" ? ShaderStageKind.Fragment : ShaderStageKind.Compute, bindings ?? new(),
            [structural], [new ShaderSpecialization(change == "specialization-id" ? 2 : 1, numeric,
                change == "inactive" ? ShaderCondition.Equal(structural, true) : null)],
            new Dictionary<string, ShaderScalar> { ["FIXED"] = ShaderScalar.From(change == "fixed" ? 2 : 1) },
            change == "entry" ? "alternate" : "main", change == "output" ? "relocated.csh" : null);
        return new(stage, new Dictionary<string, ShaderScalar> { ["MODE"] = ShaderScalar.From(change == "structural"), [numeric.Name] = numeric.Default });
    }
    #endregion
}
