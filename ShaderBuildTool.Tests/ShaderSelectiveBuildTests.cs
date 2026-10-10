using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Qualifies selective processing through the real compiler, record owners and generation publisher.</summary>
public sealed class ShaderSelectiveBuildTests
{
    private const string Domain = "vanillagraphicsexpanded";
    private static readonly ShaderBuildIdentities Identities = new("preprocessor", "emitter", "compiler", "extractor");

    #region Public API
    #region Source selection
    /// <summary>A warm build performs no syntax or reflection work, and dependency edits select only their consumers.</summary>
    [Theory]
    [InlineData("root", 1)]
    [InlineData("direct", 2)]
    [InlineData("nested", 2)]
    [InlineData("cross-domain", 2)]
    public async Task DependencyEditsSelectOnlyConsumers(string edit, int affected)
    {
        using var fixture = Create();
        var registry = Registry();
        var cold = await Build(fixture, registry);
        Assert.Equal(3, cold.RootsExpanded);
        Assert.Equal(3, cold.VariantsEmitted);
        var initial = Snapshot(fixture);
        var warm = await Build(fixture, registry);
        Assert.Equal(3, warm.RootsReused);
        Assert.Equal(3, warm.OutputsRetained);
        Assert.Equal(3, warm.VariantsReused);
        Assert.Equal(0, warm.RootsExpanded);
        Assert.Equal(0, warm.VariantsEmitted);
        Assert.Equal(0, warm.CompilerInvocations);
        Assert.Equal(0, warm.InterfacesExtracted);
        foreach (string path in Directory.GetFiles(Published(fixture), "*.spv")) File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch);
        string pathToEdit = edit switch
        {
            "root" => Path.Combine(fixture.Shaders, "a.csh"),
            "direct" => Path.Combine(fixture.Shaders, "direct.inc"),
            "nested" => Path.Combine(fixture.Shaders, "nested.inc"),
            _ => Path.Combine(fixture.Assets, "other", "shaders", "value.inc")
        };
        string originalSource = File.ReadAllText(pathToEdit);
        File.AppendAllText(pathToEdit, "\n#define EXTRA_UNUSED 1\n");
        var changed = await Build(fixture, registry);
        Assert.Equal(affected, changed.RootsExpanded);
        Assert.Equal(3 - affected, changed.RootsReused);
        Assert.Equal(3 - affected, changed.VariantsReused);
        Assert.Equal(affected, changed.VariantsEmitted);
        Assert.Equal(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(Path.Combine(Published(fixture), "c.csh.spv")));
        var incremental = Snapshot(fixture);
        await Build(fixture, registry, incremental: false);
        Assert.Equal(incremental.ToArray(), Snapshot(fixture).ToArray());
        Assert.Equal(initial.Keys, incremental.Keys);
        File.WriteAllText(pathToEdit, originalSource);
        var reverted = await Build(fixture, registry);
        Assert.Equal(affected, reverted.RootsExpanded);
        Assert.Equal(3, reverted.VariantsReused);
        Assert.Equal(0, reverted.CompilerInvocations);
        Assert.Equal(initial.ToArray(), Snapshot(fixture).ToArray());
    }

    /// <summary>Replacing a root snapshot removes its old dependencies without dropping another root's ownership.</summary>
    [Fact]
    public async Task RemovedImportStopsInvalidatingItsFormerRoot()
    {
        using var fixture = Create();
        var registry = Registry();
        await Build(fixture, registry);
        File.WriteAllText(Path.Combine(fixture.Shaders, "a.csh"), Source("#define VALUE 7u"));
        var removed = await Build(fixture, registry);
        Assert.Equal(1, removed.RootsExpanded);
        File.WriteAllText(Path.Combine(fixture.Assets, "other", "shaders", "value.inc"), "#define VALUE 8u\n");
        var changed = await Build(fixture, registry);
        Assert.Equal(1, changed.RootsExpanded);
        Assert.Equal(2, changed.VariantsReused);
    }

    /// <summary>Strict validation selects changed reused dependencies even when their length and timestamps are preserved.</summary>
    [Fact]
    public async Task StrictSourceSelectionDetectsPreservedMetadataChanges()
    {
        using var fixture = Create();
        var registry = Registry();
        await Build(fixture, registry);
        string path = Path.Combine(fixture.Assets, "other", "shaders", "value.inc");
        DateTime write = File.GetLastWriteTimeUtc(path), creation = File.GetCreationTimeUtc(path);
        long length = new FileInfo(path).Length;
        File.WriteAllText(path, File.ReadAllText(path).Replace("7u", "8u"));
        File.SetLastWriteTimeUtc(path, write);
        File.SetCreationTimeUtc(path, creation);
        Assert.Equal(length, new FileInfo(path).Length);
        var changed = await Build(fixture, registry);
        Assert.Equal(2, changed.RootsExpanded);
        Assert.Equal(2, changed.VariantsEmitted);
        Assert.Equal(1, changed.RootsReused);
        Assert.Equal(1, changed.VariantsReused);
    }

    /// <summary>A newly introduced import joins later dependency invalidation without touching unrelated roots.</summary>
    [Fact]
    public async Task AddedImportUpdatesFutureDependencySelection()
    {
        using var fixture = Create();
        var registry = Registry();
        await Build(fixture, registry);
        File.WriteAllText(Path.Combine(fixture.Shaders, "c.csh"), Source("@import \"direct.inc\""));
        var added = await Build(fixture, registry);
        Assert.Equal(1, added.RootsExpanded);
        Assert.Equal(2, added.VariantsReused);
        File.WriteAllText(Path.Combine(fixture.Assets, "other", "shaders", "value.inc"), "#define VALUE 8u\n");
        var consumers = await Build(fixture, registry);
        Assert.Equal(3, consumers.RootsExpanded);
        Assert.Equal(3, consumers.VariantsEmitted);
    }

    /// <summary>A missing import aborts safely and preserves the complete prior generation.</summary>
    [Fact]
    public async Task MissingDependencyPreservesPriorGenerationWithoutReceipt()
    {
        using var fixture = Create();
        var registry = Registry();
        await Build(fixture, registry);
        var prior = Snapshot(fixture);
        File.WriteAllText(Path.Combine(fixture.Output, "build-receipt.json"), "old success");
        File.Delete(Path.Combine(fixture.Assets, "other", "shaders", "value.inc"));
        await Assert.ThrowsAnyAsync<Exception>(() => Build(fixture, registry));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
        Assert.Equal(prior.ToArray(), Snapshot(fixture).ToArray());
    }
    #endregion

    #region Record and identity selection
    /// <summary>Extractor changes reuse emission and verified binaries; emitter changes still reuse equivalent compiler input.</summary>
    [Fact]
    public async Task ProcessingIdentitiesSelectTheirOwningOperations()
    {
        using var fixture = Create();
        var registry = Registry();
        await Build(fixture, registry);
        var extraction = await Build(fixture, registry, Identities with { Interface = "new-extractor" });
        Assert.Equal(3, extraction.RootsReused);
        Assert.Equal(3, extraction.VariantsReused);
        Assert.Equal(0, extraction.VariantsEmitted);
        Assert.Equal(0, extraction.CompilerInvocations);
        Assert.Equal(3, extraction.InterfacesExtracted);
        var emission = await Build(fixture, registry, Identities with { Emission = "new-emitter" });
        Assert.Equal(3, emission.RootsReused);
        Assert.Equal(3, emission.VariantsEmitted);
        Assert.Equal(0, emission.CompilerInvocations);
        var preprocessing = await Build(fixture, registry, Identities with { Preprocessing = "new-preprocessor" });
        Assert.Equal(3, preprocessing.RootsExpanded);
        Assert.Equal(0, preprocessing.CompilerInvocations);
        var policy = await Build(fixture, registry, Identities with { Compiler = "new-compiler-policy" });
        Assert.Equal(3, policy.RootsReused);
        Assert.Equal(ShaderCompilerProcess.GenerateDebugInfo ? 3 : 2, policy.CompilerInvocations);
        Assert.Equal(3, policy.CompilerInvocations + policy.CompilerReused);
    }

    /// <summary>Changing the supported target policy invalidates compiler artifacts while source records remain reusable.</summary>
    [Fact]
    public async Task ActualCompilerPolicyChangeSelectsCompilerWork()
    {
        using var fixture = Create();
        var registry = Registry();
        var initial = Identities with { Compiler = ShaderBuildReceipt.CompilerFingerprint(fixture.Repository, "opengl4.5", false) };
        await Build(fixture, registry, initial);
        var changed = initial with { Compiler = ShaderBuildReceipt.CompilerFingerprint(fixture.Repository, "opengl", false) };
        Assert.NotEqual(initial.Compiler, changed.Compiler);
        var result = await ShaderVariantBuild.RunAsync(fixture.Assets, fixture.Output, Domain, fixture.Repository,
            "opengl", false, registry, 3, TestContext.Current.CancellationToken, incremental: true,
            execution: new ShaderBuildExecution(changed, new ShaderFileHashIndex(fixture.Output, true)));
        Assert.Equal(3, result.RootsReused);
        Assert.Equal(3, result.VariantsEmitted);
        Assert.Equal(ShaderCompilerProcess.GenerateDebugInfo ? 3 : 2, result.CompilerInvocations);
        Assert.Equal(3, result.CompilerInvocations + result.CompilerReused);
    }

    /// <summary>An unsupported warnings-as-errors policy fails before cache reuse and invalidates prior success.</summary>
    [Fact]
    public async Task UnsupportedWarningsPolicyPreservesGenerationWithoutSuccess()
    {
        using var fixture = Create();
        var registry = Registry();
        await ShaderBuildInvocation.RunAsync(fixture.Assets, fixture.Output, Domain, fixture.Repository,
            "opengl4.5", false, () => registry, "fixture", 3, true, false, true, TestContext.Current.CancellationToken);
        var prior = Snapshot(fixture);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => ShaderBuildInvocation.RunAsync(
            fixture.Assets, fixture.Output, Domain, fixture.Repository, "opengl4.5", true, () => registry,
            "fixture", 3, true, false, true, TestContext.Current.CancellationToken));
        Assert.Contains("warningsAsErrors", error.Message);
        Assert.Equal(prior.ToArray(), Snapshot(fixture).ToArray());
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Effective contract edits select one stage while unrelated source and variant records remain reusable.</summary>
    [Theory]
    [InlineData("fixed")]
    [InlineData("binding")]
    [InlineData("specialization")]
    [InlineData("structural")]
    public async Task ContractChangesSkipUnrelatedVariants(string change)
    {
        using var fixture = Create();
        await Build(fixture, Registry());
        var changed = await Build(fixture, Registry(change));
        Assert.Equal(3, changed.RootsReused);
        Assert.Equal(0, changed.RootsExpanded);
        Assert.Equal(2, changed.VariantsReused);
        Assert.Equal(change == "structural" ? 2 : 1, changed.VariantsEmitted);
        var reverted = await Build(fixture, Registry());
        Assert.Equal(3, reverted.VariantsReused);
        Assert.Equal(0, reverted.CompilerInvocations);
    }

    /// <summary>An unsupported entry-point edit cannot reuse old metadata or replace the coherent prior generation.</summary>
    [Fact]
    public async Task EntryPointChangeRejectsIncompatibleCompilerInterface()
    {
        using var fixture = Create();
        await Build(fixture, Registry());
        var prior = Snapshot(fixture);
        await Assert.ThrowsAnyAsync<Exception>(() => Build(fixture, Registry("entry")));
        Assert.Equal(prior.ToArray(), Snapshot(fixture).ToArray());
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>A changed stage kind must compile under the new stage rules instead of accepting cached compute output.</summary>
    [Fact]
    public async Task StageKindChangeRejectsIncompatibleSourceWithoutPublishing()
    {
        using var fixture = Create();
        var baseline = Registry();
        await Build(fixture, baseline);
        var prior = Snapshot(fixture);
        var vertex = new ShaderStageContract("a.csh", "a.csh", ShaderStageKind.Vertex, new());
        var fragment = new ShaderStageContract("a-fragment.fsh", "a.csh", ShaderStageKind.Fragment, new());
        var changed = new ShaderVariantResolver(baseline.Programs.Values.Where(program => program.Identity != "a")
            .Append(new GpuShaderContract("a", [vertex, fragment], 1)));
        await Assert.ThrowsAnyAsync<Exception>(() => Build(fixture, changed));
        Assert.Equal(prior.ToArray(), Snapshot(fixture).ToArray());
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Registry resolution and missing source roots invalidate old success under the invocation failure boundary.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvocationSetupFailureInvalidatesSuccess(bool missingDirectory)
    {
        using var fixture = Create();
        var registry = Registry();
        await ShaderBuildInvocation.RunAsync(fixture.Assets, fixture.Output, Domain, fixture.Repository,
            "opengl4.5", false, () => registry, "fixture", 3, true, false, true, TestContext.Current.CancellationToken);
        var prior = Snapshot(fixture);
        Assert.True(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
        if (missingDirectory) Directory.Move(fixture.Shaders, fixture.Shaders + "-removed");
        await Assert.ThrowsAnyAsync<Exception>(() => ShaderBuildInvocation.RunAsync(fixture.Assets, fixture.Output,
            Domain, fixture.Repository, "opengl4.5", false,
            () => throw new ArgumentException("Injected invalid registry."), "fixture", 3, true, false, true,
            TestContext.Current.CancellationToken));
        Assert.Equal(prior.ToArray(), Snapshot(fixture).ToArray());
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Scope changes publish exact membership, retain historical records, and reject unregistered roots.</summary>
    [Fact]
    public async Task MembershipChangesReuseRetainedStagesAndPruneRemovedOutputs()
    {
        using var fixture = Create();
        await Build(fixture, Registry());
        File.Delete(Path.Combine(fixture.Shaders, "c.csh"));
        var removed = await Build(fixture, Registry(omitC: true));
        Assert.Equal(2, removed.VariantsReused);
        Assert.Equal(1, removed.OutputsRemoved);
        Assert.Equal(0, removed.CompilerInvocations);
        Assert.False(File.Exists(Path.Combine(Published(fixture), "c.csh.spv")));
        File.WriteAllText(Path.Combine(fixture.Shaders, "c.csh"), Source("#define VALUE 9u"));
        await Assert.ThrowsAnyAsync<Exception>(() => Build(fixture, Registry(omitC: true)));
        var added = await Build(fixture, Registry());
        Assert.Equal(3, added.VariantsReused);
        Assert.Equal(0, added.CompilerInvocations);
    }

    /// <summary>Missing interfaces and outputs repair locally without syntax work or compilation.</summary>
    [Fact]
    public async Task MissingArtifactsRepairAtTheirOwningLayer()
    {
        using var fixture = Create();
        var registry = Registry();
        await Build(fixture, registry);
        foreach (var file in Directory.GetFiles(Path.Combine(fixture.Output, "_cache", "interfaces"))) File.Delete(file);
        File.Delete(Path.Combine(Published(fixture), "c.csh.spv"));
        var repaired = await Build(fixture, registry);
        Assert.Equal(3, repaired.VariantsReused);
        Assert.Equal(1, repaired.OutputsRepaired);
        Assert.Equal(0, repaired.VariantsEmitted);
        Assert.Equal(0, repaired.CompilerInvocations);
        Assert.Equal(3, repaired.InterfacesExtracted);
        Assert.True(File.Exists(Path.Combine(Published(fixture), "c.csh.spv")));
    }
    /// <summary>Aliases share one compiler job when compiler-visible path policy permits equivalent input.</summary>
    [Fact]
    public async Task EquivalentInputAliasesCountUniqueCompilerJobs()
    {
        using var fixture = Create();
        var baseline = Registry();
        var alias = new ShaderStageContract("alias.csh", "a.csh", ShaderStageKind.Compute, new());
        var registry = new ShaderVariantResolver(baseline.Programs.Values.Append(new GpuShaderContract("alias", [alias], 1)));
        var cold = await Build(fixture, registry);
        Assert.Equal(3, cold.RootsExpanded);
        Assert.Equal(4, cold.VariantsEmitted);
        Assert.Equal(4, cold.CompilerInvocations + cold.CompilerReused);
        if (ShaderCompilerProcess.GenerateDebugInfo) Assert.Equal(4, cold.CompilerInvocations);
        else
        {
            Assert.InRange(cold.CompilerInvocations, 1, 3);
            Assert.Equal(File.ReadAllBytes(Path.Combine(Published(fixture), "a.csh.spv")),
                File.ReadAllBytes(Path.Combine(Published(fixture), "alias.csh.spv")));
        }
        var warm = await Build(fixture, registry);
        Assert.Equal(4, warm.VariantsReused);
        Assert.Equal(0, warm.CompilerInvocations);
    }

    /// <summary>A consumed dependency changed before commit cannot receive a successful generation or receipt.</summary>
    [Fact]
    public async Task ConsumedInputMutationRejectsCommit()
    {
        using var fixture = Create();
        var registry = Registry();
        await Build(fixture, registry);
        var prior = Snapshot(fixture);
        await Assert.ThrowsAsync<IOException>(() => ShaderVariantBuild.RunAsync(fixture.Assets, fixture.Output, Domain,
            fixture.Repository, "opengl4.5", false, registry, 3, TestContext.Current.CancellationToken, incremental: true,
            publish: generation =>
            {
                File.WriteAllText(Path.Combine(fixture.Assets, "other", "shaders", "value.inc"), "#define VALUE 8u\n");
                generation.ValidateInputs(new ShaderFileHashIndex(fixture.Output, true));
            }, execution: new ShaderBuildExecution(Identities, new ShaderFileHashIndex(fixture.Output, true))));
        Assert.Equal(prior.ToArray(), Snapshot(fixture).ToArray());
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>Compiler and record policy use the declared kind when asset extensions differ.</summary>
    [Fact]
    public async Task DeclaredStageKindControlsRecordReuse()
    {
        using var fixture = Create();
        File.Move(Path.Combine(fixture.Shaders, "a.csh"), Path.Combine(fixture.Shaders, "a.fsh"));
        var baseline = Registry();
        var a = new ShaderStageContract("a.csh", "a.fsh", ShaderStageKind.Compute, new());
        var registry = new ShaderVariantResolver(baseline.Programs.Values.Where(p => p.Identity != "a")
            .Append(new GpuShaderContract("a", [a], 1)));
        await Build(fixture, registry);
        var warm = await Build(fixture, registry);
        Assert.Equal(3, warm.VariantsReused);
        Assert.Equal(0, warm.CompilerInvocations);
    }
    #endregion
    #endregion

    #region Private
    #region Fixture inputs
    /// <summary>Creates nested, shared, cross-domain and isolated compute roots in an owned temporary fixture.</summary>
    private static ShaderBuildFixture Create()
    {
        var fixture = new ShaderBuildFixture();
        foreach (var file in Directory.GetFiles(fixture.Shaders)) File.Delete(file);
        File.WriteAllText(Path.Combine(fixture.Shaders, "a.csh"), Source("@import \"direct.inc\""));
        File.WriteAllText(Path.Combine(fixture.Shaders, "b.csh"), Source("@import \"direct.inc\""));
        File.WriteAllText(Path.Combine(fixture.Shaders, "c.csh"), Source("#define VALUE 9u"));
        File.WriteAllText(Path.Combine(fixture.Shaders, "direct.inc"), "@import \"nested.inc\"\n");
        File.WriteAllText(Path.Combine(fixture.Shaders, "nested.inc"), "@import \"other:shaders/value.inc\"\n");
        string other = Path.Combine(fixture.Assets, "other", "shaders");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "value.inc"), "#define VALUE 7u\n");
        return fixture;
    }

    /// <summary>Uses an observable storage write so compiler optimizations retain changed constants.</summary>
    private static string Source(string header) => "#version 450 core\n" + header +
        "\nlayout(local_size_x=1) in; layout(std430,binding=0) buffer Data{uint value;}; void main(){value=VALUE;}\n";

    /// <summary>Builds independently owned compute programs with one optionally changed effective contract.</summary>
    private static ShaderVariantResolver Registry(string change = "", bool omitC = false)
    {
        var programs = new List<GpuShaderContract>();
        foreach (string name in omitC ? new[] { "a", "b" } : new[] { "a", "b", "c" })
        {
            var bindings = new GpuBindingContract();
            if (name == "a" && change == "binding") bindings.StorageBlocks.Add("Data", new(1, true));
            ShaderOption[] structural = name == "a" && change == "structural" ? [new ShaderOption<bool>("MODE", false)] : [];
            ShaderSpecialization[] specializations = name == "a" && change == "specialization" ? [new(3, new ShaderOption<int>("AMOUNT", 2))] : [];
            var fixedDefines = new Dictionary<string, ShaderScalar>();
            if (name == "a" && change == "fixed") fixedDefines.Add("FIXED", ShaderScalar.From(1));
            var stage = new ShaderStageContract(name + ".csh", name + ".csh", ShaderStageKind.Compute, bindings,
                structural, specializations, fixedDefines, entryPoint: name == "a" && change == "entry" ? "alternate" : "main");
            programs.Add(new GpuShaderContract(name, [stage], 4, structural.Concat(specializations.Select(item => item.Option))));
        }
        return new(programs);
    }

    #endregion

    #region Execution and observations
    /// <summary>Invokes the production selective pipeline with stable test identities and strict source checks.</summary>
    private static Task<ShaderBuildStatistics> Build(ShaderBuildFixture fixture, ShaderVariantResolver registry,
        ShaderBuildIdentities? identities = null, bool incremental = true) =>
        ShaderVariantBuild.RunAsync(fixture.Assets, fixture.Output, Domain, fixture.Repository, "opengl4.5", false,
            registry, 3, TestContext.Current.CancellationToken, incremental: incremental,
            execution: new ShaderBuildExecution(identities ?? Identities, new ShaderFileHashIndex(fixture.Output, true)));

    /// <summary>Locates the complete active generation owned by this fixture.</summary>
    private static string Published(ShaderBuildFixture fixture) => Path.Combine(fixture.Output, Domain, "shaders");

    /// <summary>Captures runtime bytes including interface manifests, independent of private work and cache records.</summary>
    private static SortedDictionary<string, string> Snapshot(ShaderBuildFixture fixture) =>
        new(Directory.GetFiles(Published(fixture), "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(Published(fixture), path), path => Convert.ToBase64String(File.ReadAllBytes(path)))!, StringComparer.Ordinal);
    #endregion
    #endregion
}
