using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ShaderContractGenerator.Tests;

/// <summary>Compiles real shared models and generated declarations without loading the game or shader compiler.</summary>
internal static class GeneratorFixture
{
    internal const string Prelude = "using VanillaGraphicsExpanded.Rendering.Contracts;\nnamespace Example;\n";
    private const string Bindings = "namespace VanillaGraphicsExpanded.Rendering.Contracts { internal static class GpuShaderContracts { internal static GpuBindingContract DeclareBindings(string identity) => new(); } }";
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp13);
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray();
    private static readonly SyntaxTree[] Models = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ContractSources"), "*.cs")
        .Order(StringComparer.Ordinal).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), ParseOptions, path)).Append(CSharpSyntaxTree.ParseText(Bindings, ParseOptions)).ToArray();

    #region Compilation
    /// <summary>Runs the same generator on normal source or semantic-only additional files.</summary>
    internal static Result Generate(string source, bool offline = false, GeneratorDriver? previous = null, string[]? symbols = null)
    {
        var parseOptions = ParseOptions.WithPreprocessorSymbols(symbols ?? []);
        var syntax = CSharpSyntaxTree.ParseText(Prelude + source, parseOptions, "Owner.cs");
        var compilation = CSharpCompilation.Create("GeneratedTest" + Guid.NewGuid().ToString("N"),
            (offline ? Models : Models.Append(syntax)).Select(tree => tree.WithRootAndOptions(tree.GetRoot(), parseOptions)), References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = previous ?? CSharpGeneratorDriver.Create([new ShaderDeclarationGenerator().AsSourceGenerator()],
            offline ? [new SourceFile("Owner.cs", Prelude + source)] : [], parseOptions, new Configuration(offline));
        if (previous != null && offline) driver = driver.ReplaceAdditionalTexts([new SourceFile("Owner.cs", Prelude + source)]);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
        return new(driver, updated, diagnostics, driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).OrderBy(s => s.HintName, StringComparer.Ordinal).Select(s => s.SourceText.ToString()).ToArray());
    }
    /// <summary>Contains compile output and provides explicit diagnostic/emit assertions.</summary>
    internal sealed record Result(GeneratorDriver Driver, Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics, string[] Generated)
    {
        /// <summary>Requires both generator validation and the resulting C# compilation to succeed.</summary>
        internal byte[] Compile()
        {
            Assert.Empty(Diagnostics);
            using var output = new MemoryStream();
            var result = Compilation.Emit(output);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return output.ToArray();
        }
        /// <summary>Executes a test-owned observation method in the emitted assembly.</summary>
        internal string Run(string type = "Example.Proof") => (string)Assembly.Load(Compile()).GetType(type)!.GetMethod("Run")!.Invoke(null, null)!;
    }
    #endregion

    #region Compiler inputs
    /// <summary>Provides deterministic additional source text to the offline generator.</summary>
    private sealed class SourceFile(string path, string text) : AdditionalText
    {
        public override string Path => path;
        /// <summary>Returns source content without filesystem access.</summary>
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
    /// <summary>Exposes the sole build-mode property required by the generator.</summary>
    private sealed class Options(bool offline) : AnalyzerConfigOptions
    {
        /// <summary>Reads explicit offline mode and leaves all other options absent.</summary>
        public override bool TryGetValue(string key, out string value)
        {
            value = offline ? "true" : "false";
            return key == "build_property.ShaderContractsOffline";
        }
    }
    /// <summary>Supplies identical mode options to all fixture syntax/additional files.</summary>
    private sealed class Configuration(bool offline) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(offline);
        /// <summary>Returns the fixture's explicit generation mode.</summary>
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        /// <summary>Returns the fixture's explicit generation mode.</summary>
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }
    #endregion
}


