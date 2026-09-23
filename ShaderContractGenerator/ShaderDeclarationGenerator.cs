using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Generates contracts from semantic attribute declarations for both runtime and offline consumers.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ShaderDeclarationGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidDeclaration = new("VGEGEN001", "Invalid shader declaration", "{0}: {1}", "Shaders", DiagnosticSeverity.Error, true);

    #region Incremental entry point
    /// <summary>Tracks source and additional-file changes, including declaration removal.</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider.Where(f => f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select((f, token) => (f.Path, Text: f.GetText(token)?.ToString() ?? "")).Collect();
        var offline = context.AnalyzerConfigOptionsProvider.Select((p, _) =>
            p.GlobalOptions.TryGetValue("build_property.ShaderContractsOffline", out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
        context.RegisterSourceOutput(context.CompilationProvider.Combine(files).Combine(offline),
            (production, input) => Generate(production, input.Left.Left, input.Left.Right, input.Right));
    }
    /// <summary>Builds a semantic-only view of runtime source offline; game types never enter emitted build-tool code.</summary>
    private static void Generate(SourceProductionContext context, Compilation compilation, ImmutableArray<(string Path, string Text)> files, bool offline)
    {
        var originalCompilation = compilation;
        if (offline)
        {
            var existing = new HashSet<string>(compilation.SyntaxTrees.Where(t => !string.IsNullOrEmpty(t.FilePath)).Select(t => Path.GetFullPath(t.FilePath)), StringComparer.OrdinalIgnoreCase);
            // Additional trees belong to this compilation and must retain its language version and symbols.
            var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ?? new CSharpParseOptions();
            compilation = compilation.AddSyntaxTrees(files.Where(f => !existing.Contains(Path.GetFullPath(f.Path)))
                .Select(f => CSharpSyntaxTree.ParseText(f.Text, parseOptions, f.Path, Encoding.UTF8)));
        }
        var owners = new Dictionary<INamedTypeSymbol, OwnerDeclaration>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semantic = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot(context.CancellationToken).DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (declaration.AttributeLists.Count == 0 && !declaration.Members.Any(p => p.AttributeLists.Count != 0)) continue;
                if (semantic.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } symbol || owners.ContainsKey(symbol)) continue;
                if (!symbol.GetAttributes().Any(IsDeclarationAttribute) && !symbol.GetMembers().Any(m => m.GetAttributes().Any(IsDeclarationAttribute))) continue;
                owners.Add(symbol, new(symbol));
            }
        }
        bool failed = false;
        var options = new OptionReader(owners);
        foreach (var owner in owners.Values.OrderBy(o => o.Name, StringComparer.Ordinal))
            Try(owner, () =>
            {
                foreach (var attribute in owner.Symbol.GetAttributes().Concat(owner.Symbol.GetMembers().SelectMany(m => m.GetAttributes())).Where(IsDeclarationAttribute))
                {
                    if (attribute.ApplicationSyntaxReference?.GetSyntax() is not { } syntax) continue;
                    var errors = compilation.GetSemanticModel(syntax.SyntaxTree).GetDiagnostics(syntax.Span, context.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                    if (errors.Length != 0) throw new ArgumentException(string.Join("; ", errors.Select(d => d.GetMessage())));
                }
                if (owner.Symbol.GetMembers().Any(m => m is not IPropertySymbol && m.GetAttributes().Any(a => Attributes(m, "ShaderOption").Contains(a) || Attributes(m, "ShaderOptionReference").Contains(a))))
                    throw new ArgumentException("Shader options require partial properties; fields cannot intercept runtime assignments.");
                if (owner.Symbol.ContainingType != null || owner.Symbol.IsGenericType || owner.Symbol.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is ClassDeclarationSyntax c && !c.Modifiers.Any(SyntaxKind.PartialKeyword)))
                    throw new ArgumentException("Shader declaration owners must be top-level, nongeneric partial classes.");
                foreach (var property in owner.Symbol.GetMembers().OfType<IPropertySymbol>().Where(p => Attributes(p, "ShaderOption").Any() || Attributes(p, "ShaderOptionReference").Any())) options.Read(owner, property);
                foreach (var sameName in owner.Options.Values.GroupBy(o => o.Model.Name))
                    if (sameName.Any(o => o.TypeName != sameName.First().TypeName || !o.Model.Equivalent(sameName.First().Model)))
                        throw new ArgumentException($"Conflicting shared option '{sameName.Key}'.");
                ProgramReader.ReadGroups(owner);
            });
        var reader = new ProgramReader(owners);
        foreach (var owner in owners.Values.OrderBy(o => o.Name, StringComparer.Ordinal)) Try(owner, () => reader.Read(owner));
        if (!failed)
        {
            var programs = new Dictionary<string, GpuShaderContract>(StringComparer.Ordinal);
            var stages = new Dictionary<string, ShaderStageContract>(StringComparer.Ordinal);
            var layoutExpressions = new Dictionary<string, string>(StringComparer.Ordinal);
            var optionTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            var outputPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var owner in owners.Values.OrderBy(o => o.Name, StringComparer.Ordinal)) Try(owner, () =>
            {
                foreach (var program in owner.Programs)
                {
                    if (programs.ContainsKey(program.Scope + ":" + program.Model.Identity)) throw new ArgumentException($"Duplicate program identity '{program.Model.Identity}' in scope '{program.Scope}'.");
                    programs.Add(program.Scope + ":" + program.Model.Identity, program.Model);
                    foreach (var stage in program.Model.Stages)
                    {
                        string key = program.Scope + ":" + stage.Identity;
                        if (stages.TryGetValue(key, out var prior) && !prior.Equivalent(stage)) throw new ArgumentException($"Conflicting shared stage '{stage.Identity}'.");
                        stages[key] = stage;
                        string output = program.Scope + ":" + stage.BinaryAsset;
                        if (outputPaths.TryGetValue(output, out string? otherIdentity) && otherIdentity != stage.Identity)
                            throw new ArgumentException($"Output path '{stage.BinaryAsset}' collides with stage '{otherIdentity}'.");
                        outputPaths[output] = stage.Identity;
                        foreach (var option in stage.Structural.Concat(stage.Specializations.Select(s => s.Option)))
                        {
                            string optionKey = key + ":" + option.Name;
                            string type = owner.Options.Values.First(o => o.Model.Name == option.Name).TypeName;
                            if (optionTypes.TryGetValue(optionKey, out string? otherType) && type != otherType)
                                throw new ArgumentException($"Shared stage '{stage.Identity}' has incompatible option type '{option.Name}'.");
                            optionTypes[optionKey] = type;
                        }
                    }
                }
                foreach (var stage in Attributes(owner.Symbol, "ShaderStage"))
                {
                    string source = Text(stage, 2), identity = Text(stage, "Identity", source)!;
                    string layout = Text(stage, "Layout", source.Contains('.') ? source.Substring(0, source.LastIndexOf('.')) : source)!;
                    string scope = owner.Programs.Single(p => p.Member == Text(stage, 0)).Scope;
                    string key = scope + ":" + identity;
                    if (layoutExpressions.TryGetValue(key, out string? prior) && prior != layout) throw new ArgumentException($"Shared stage '{identity}' has conflicting binding layouts.");
                    layoutExpressions[key] = layout;
                }
            });
        }
        if (failed) return;
        foreach (var owner in owners.Values.OrderBy(o => o.Name, StringComparer.Ordinal))
            context.AddSource(owner.Name.Replace("global::", "").Replace('.', '_') + ".Shader.g.cs", SourceText.From(DeclarationEmitter.Owner(owner, offline), Encoding.UTF8));
        if (offline)
        {
            var enumTypes = owners.Values.SelectMany(o => o.Options.Values).Select(o => o.Shared
                ? ((INamedTypeSymbol)o.Property.Type).TypeArguments[0] : o.Property.Type)
                .OfType<INamedTypeSymbol>().Where(t => t.TypeKind == TypeKind.Enum)
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var type in enumTypes)
            {
                // Existing references/shared model sources already supply these types to the offline compiler.
                if (originalCompilation.GetTypeByMetadataName(type.ToDisplayString()) != null) continue;
                string source = DeclarationEmitter.Enum(type);
                context.AddSource(type.ToDisplayString().Replace('.', '_') + ".Enum.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }
        context.AddSource("GeneratedShaderCatalog.g.cs", SourceText.From(DeclarationEmitter.Catalog(owners.Values), Encoding.UTF8));

        // Report errors at the owning declaration and never publish a partially valid catalog.
        void Try(OwnerDeclaration owner, Action action)
        {
            try { action(); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or InvalidCastException)
            {
                failed = true;
                var location = error is ConditionDeclarationException conditionError ? conditionError.Location : owner.Symbol.Locations.FirstOrDefault() ?? Location.None;
                if (offline && location.SourceTree != null && !originalCompilation.SyntaxTrees.Contains(location.SourceTree))
                    location = Location.Create(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
                context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, location, owner.Name, error.Message));
            }
        }
    }
    /// <summary>Recognizes the explicit declaration schema and excludes legacy discovery-only metadata.</summary>
    private static bool IsDeclarationAttribute(AttributeData attribute) => attribute.AttributeClass?.ToDisplayString() is
        Prefix + "ShaderProgramAttribute" or Prefix + "ShaderStageAttribute" or Prefix + "ShaderOptionAttribute" or
        Prefix + "ShaderOptionReferenceAttribute" or Prefix + "ShaderUseAttribute" or Prefix + "ShaderFixedDefineAttribute" or
        Prefix + "ShaderGroupAttribute" or Prefix + "ShaderAcceptGroupAttribute" or Prefix + "ShaderAssignmentAttribute";
    #endregion
}
