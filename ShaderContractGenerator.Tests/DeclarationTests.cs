namespace ShaderContractGenerator.Tests;

/// <summary>Checks semantic declarations, generated C# compilation, shared metadata and deterministic discovery.</summary>
public sealed class DeclarationTests
{
    internal const string Basic = """
        [ShaderProgram("Contract", "example", 2)]
        [ShaderStage("Contract", ShaderStageKind.Vertex, "shared.vsh")]
        [ShaderStage("Contract", ShaderStageKind.Fragment, "example.fsh")]
        [ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Enabled))]
        internal static partial class Shader
        {
            [ShaderOption("ENABLED", false, Aliases = new[] { "OLD_ENABLED" })]
            internal static partial ShaderOption<bool> Enabled { get; }
        }
        """;

    #region Generated output
    /// <summary>Additional shader declarations retain non-pinned consumer parse options without mixed-version trees.</summary>
    [Fact]
    public void OfflineDeclarationsUseConsumerLanguageVersion()
    {
        var result = GeneratorFixture.Generate(Basic, offline: true,
            languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview);
        result.Compile();
        Assert.NotEmpty(result.Generated);
    }

    /// <summary>Offline and runtime declarations produce identical metadata without a mod assembly reference.</summary>
    [Fact]
    public void OfflineAndRuntimeGenerateIdenticalContracts()
    {
        var runtime = GeneratorFixture.Generate(Basic);
        var offline = GeneratorFixture.Generate(Basic, offline: true);
        runtime.Compile();
        offline.Compile();
        Assert.Equal(runtime.Generated.Select(s => s.Replace("static partial ShaderOption", "static ShaderOption")), offline.Generated);
    }
    /// <summary>Offline source parsing honors the compilation's build configuration symbols.</summary>
    [Fact]
    public void OfflineAndRuntimeHonorBuildSymbols()
    {
        string source = "#if DEBUG\n" + Basic + "\n#endif\n";
        var runtime = GeneratorFixture.Generate(source, symbols: ["DEBUG"]);
        var offline = GeneratorFixture.Generate(source, true, symbols: ["DEBUG"]);
        runtime.Compile(); offline.Compile();
        Assert.Equal(runtime.Generated.Select(s => s.Replace("static partial ShaderOption", "static ShaderOption")), offline.Generated);
        Assert.Contains(offline.Generated, s => s.Contains("global::Example.Shader.Contract"));
    }
    /// <summary>Stable ordering is independent of source declaration order.</summary>
    [Fact]
    public void DeclarationOrderDoesNotChangeGeneratedCatalog()
    {
        string other = Basic.Replace("class Shader", "class Other").Replace("\"example\"", "\"other\"").Replace("example.fsh", "other.fsh");
        var first = GeneratorFixture.Generate(Basic + other);
        var second = GeneratorFixture.Generate(other + Basic);
        first.Compile(); second.Compile();
        Assert.Equal(first.Generated, second.Generated);
    }
    /// <summary>Changed additional files add and remove owners without a registration edit or stale output.</summary>
    [Fact]
    public void IncrementalOfflineInputsIncludeAndRemoveOwners()
    {
        var first = GeneratorFixture.Generate(Basic, true);
        string other = Basic.Replace("class Shader", "class Other").Replace("\"example\"", "\"other\"").Replace("example.fsh", "other.fsh");
        var added = GeneratorFixture.Generate(Basic + other, true, first.Driver);
        var removed = GeneratorFixture.Generate(other, true, added.Driver);
        added.Compile(); removed.Compile();
        Assert.Contains(added.Generated, s => s.Contains("global::Example.Shader.Contract") && s.Contains("global::Example.Other.Contract"));
        Assert.DoesNotContain(removed.Generated, s => s.Contains("global::Example.Shader.Contract"));
    }
    /// <summary>Aliases and fully qualified attribute names bind semantically rather than matching short text.</summary>
    [Fact]
    public void AttributeAliasesAreSupported()
    {
        string source = Basic.Replace("[ShaderProgram(", "[global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderProgramAttribute(");
        GeneratorFixture.Generate(source).Compile();
    }
    /// <summary>The offline view tolerates unavailable runtime base types and members without emitting them.</summary>
    [Fact]
    public void OfflineDoesNotRequireGameTypes()
    {
        string source = Basic.Replace("static partial class Shader", "partial class Shader : MissingGameBase").Replace("internal static partial ShaderOption<bool> Enabled", "internal static partial ShaderOption<bool> Enabled")
            .Replace("internal static partial ShaderOption<bool> Enabled { get; }", "internal static partial ShaderOption<bool> Enabled { get; } public MissingGpuType Render(MissingEngineType frame) => MissingRenderer.Draw(frame);");
        var output = GeneratorFixture.Generate(source, true);
        output.Compile();
        Assert.DoesNotContain(output.Generated, s => s.Contains("MissingGpuType") || s.Contains("MissingGameBase"));
    }
    /// <summary>Families and isolated scopes share stages but expose separate program identities.</summary>
    [Fact]
    public void FamilyScopesAndExplicitSubsetArePreserved()
    {
        string source = Basic.Replace("internal static partial class Shader", """
            [ShaderProgram("OtherContract", "alternate", 1, Scope = "isolated")]
            [ShaderStage("OtherContract", ShaderStageKind.Compute, "alternate.csh")]
            [ShaderAssignment("Contract", "ENABLED=0")]
            internal static partial class Shader
            """) + """
            public static class Proof { public static string Run() =>
                Shader.Contract.Assignments.Count + ":" + GeneratedShaderCatalog.Programs().Count + ":" + GeneratedShaderCatalog.Programs("isolated").Count; }
            """;
        Assert.Equal("1:1:1", GeneratorFixture.Generate(source).Run());
    }
    #endregion
}

