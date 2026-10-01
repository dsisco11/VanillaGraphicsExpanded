using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Resolves interface property ownership and implements missing bindings on concrete shader owners.</summary>
internal static class InterfaceBindingReader
{
    #region Public API
    /// <summary>Recognizes shader owners whose binding API is declared entirely through interfaces.</summary>
    public static bool HasBindings(INamedTypeSymbol owner) => owner.AllInterfaces.Any(i =>
        i.GetMembers().Any(p => Attributes(p, "ShaderBinding").Any()));

    /// <summary>Allows only a derived interface or matching concrete property to override inherited metadata.</summary>
    public static bool Overrides(IPropertySymbol property, IPropertySymbol prior)
    {
        if (prior.ContainingType.TypeKind != TypeKind.Interface || property.Name != prior.Name ||
            !SymbolEqualityComparer.Default.Equals(property.Type, prior.Type)) return false;
        return property.ContainingType.AllInterfaces.Contains(prior.ContainingType, SymbolEqualityComparer.Default);
    }

    /// <summary>Checks inherited API ambiguity and concrete implementation compatibility before any output is published.</summary>
    public static void ValidateImplementations(INamedTypeSymbol owner)
    {
        foreach (var property in Properties(owner))
        {
            var implementation = Implementation(owner, property);
            if (implementation == null && owner.GetMembers(property.Name).Length != 0)
                throw new ArgumentException($"Interface binding '{property.Name}' conflicts with an incompatible concrete member.");
            if (implementation != null)
            {
                if (implementation.IsAbstract && implementation.ContainingType.TypeKind == TypeKind.Class)
                    throw new ArgumentException($"Interface binding '{property.Name}' has an abstract concrete declaration; use a defining partial property or an authored body.");
                if (NeedsImplementation(implementation) &&
                    ((implementation.GetMethod != null) != (property.GetMethod != null) ||
                     (implementation.SetMethod != null) != (property.SetMethod != null) || implementation.SetMethod?.IsInitOnly == true))
                    throw new ArgumentException($"Interface binding '{property.Name}' has incompatible defining partial accessors.");
                // Concrete code owns behavior, while interface metadata owns the contract. An
                // attributed implementation may override indices/policy, but cannot change identity.
                var concrete = Attributes(implementation, "ShaderBinding").FirstOrDefault();
                var inherited = Attributes(property, "ShaderBinding").Single();
                if (concrete != null && (Text(concrete, 0) != Text(inherited, 0) ||
                    !Equals(Argument(concrete, 1).Value, Argument(inherited, 1).Value)))
                    throw new ArgumentException($"Interface binding '{property.Name}' changes its GLSL name or resource kind in the implementation.");
            }
            if (!property.Type.ToDisplayString().StartsWith(Prefix, StringComparison.Ordinal) &&
                NeedsImplementation(implementation) && !BindingReader.HasRuntimeTarget(owner))
                throw new ArgumentException($"Interface binding '{property.Name}' requires a GpuProgram owner or an owning GpuComputePipeline field named 'pipeline'.");
        }
    }

    /// <summary>Validates interface API ownership even when the interface is imported only as metadata.</summary>
    public static void ValidateHierarchy(INamedTypeSymbol contract)
    {
        _ = Properties(contract).ToArray();
    }

    /// <summary>Emits ordinary public members or completes authored partial properties without duplicating concrete/default bodies.</summary>
    public static string EmitProperties(INamedTypeSymbol owner)
    {
        var text = new StringBuilder();
        foreach (var property in Properties(owner))
        {
            var implementation = Implementation(owner, property);
            if (!NeedsImplementation(implementation) ||
                (implementation != null && Attributes(implementation, "ShaderBinding").Any()) ||
                (owner.GetMembers(property.Name).Length == 0 && owner.BaseType != null &&
                    owner.BaseType.AllInterfaces.Contains(property.ContainingType, SymbolEqualityComparer.Default))) continue;
            var attribute = Attributes(property, "ShaderBinding").Single();
            string type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string modifiers = "public";
            if (implementation != null)
            {
                var syntax = (PropertyDeclarationSyntax)implementation.DeclaringSyntaxReferences[0].GetSyntax();
                modifiers = string.Join(" ", syntax.Modifiers.Select(m => m.Text));
            }
            text.Append("/// <summary>Implements the declared interface GPU binding.</summary>\n")
                .Append(modifiers).Append(' ').Append(type).Append(' ').Append(property.Name).Append(" { ");
            if (property.GetMethod != null)
                text.Append("get => new ").Append(type).Append('(').Append(Quote(Text(attribute, 0))).Append(", ")
                    .Append(Argument(attribute, 2).Value).Append(", ").Append(Named(attribute, "Required").Value is false ? "false" : "true").Append("); ");
            else
            {
                string target = owner.GetMembers("pipeline").OfType<IFieldSymbol>().Any(f =>
                    f.Type.ToDisplayString() == "VanillaGraphicsExpanded.Rendering.GpuComputePipeline")
                    ? "pipeline.ProgramLayout, pipeline.ProgramId" : "ProgramLayout, ProgramId";
                string[] kinds = { "UniformLocation", "Sampler", "Image", "UniformBlock", "StorageBlock", "VaryingLocation", "FragmentOutputLocation" };
                text.Append("set => global::VanillaGraphicsExpanded.Rendering.ShaderBindingAccess.")
                    .Append(kinds[(int)Argument(attribute, 1).Value!]).Append('(').Append(target).Append(", ")
                    .Append(Quote(Text(attribute, 0))).Append(", value); ");
            }
            text.Append("}\n");
        }
        return text.ToString();
    }
    #endregion

    #region Private
    /// <summary>Collapses diamonds and explicit derived redeclarations while rejecting unrelated ownership of one API name.</summary>
    private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol owner)
    {
        var contracts = owner.TypeKind == TypeKind.Interface ? owner.AllInterfaces.Concat(new[] { owner }) : owner.AllInterfaces;
        var selected = new List<IPropertySymbol>();
        foreach (var group in contracts.SelectMany(i => i.GetMembers().OfType<IPropertySymbol>())
            .Where(p => Attributes(p, "ShaderBinding").Any()).GroupBy(p => p.Name).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // Resolve the complete inheritance graph before diagnosing sibling ambiguity. A
            // composite interface may explicitly redeclare the property to resolve both parents.
            var candidates = group.Where(p => !group.Any(other => Overrides(other, p))).ToArray();
            if (candidates.Length != 1)
                throw new ArgumentException($"Ambiguous interface binding property '{group.Key}' from " +
                    string.Join(", ", candidates.Select(p => p.ContainingType.ToDisplayString()).OrderBy(n => n, StringComparer.Ordinal)) + ".");
            foreach (var inherited in group.Where(p => !SymbolEqualityComparer.Default.Equals(p, candidates[0])))
                RequireSameIdentity(candidates[0], inherited);
            selected.Add(candidates[0]);
        }
        return selected;
    }

    /// <summary>Restricts inherited overrides to one GLSL resource identity while allowing layout/policy changes.</summary>
    private static void RequireSameIdentity(IPropertySymbol property, IPropertySymbol prior)
    {
        var current = Attributes(property, "ShaderBinding").Single();
        var inherited = Attributes(prior, "ShaderBinding").Single();
        if (Text(current, 0) != Text(inherited, 0) || !Equals(Argument(current, 1).Value, Argument(inherited, 1).Value))
            throw new ArgumentException($"Interface binding '{property.Name}' changes its inherited GLSL name or resource kind.");
    }

    /// <summary>Finds authored implementations; unresolved defining partial properties are handled by emission.</summary>
    private static IPropertySymbol? Implementation(INamedTypeSymbol owner, IPropertySymbol property)
        => owner.FindImplementationForInterfaceMember(property) as IPropertySymbol;

    /// <summary>Preserves concrete/default bodies and generates only missing or defining partial implementations.</summary>
    private static bool NeedsImplementation(IPropertySymbol? property)
    {
        if (property == null || property.IsAbstract) return true;
        return property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<PropertyDeclarationSyntax>()
            .Any(s => s.Modifiers.Any(SyntaxKind.PartialKeyword) && s.AccessorList != null &&
                s.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null));
    }
    #endregion
}
