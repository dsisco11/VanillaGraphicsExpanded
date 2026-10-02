using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Generates retained resource properties and publication from the existing binding interfaces.</summary>
internal static class RuntimeSubmissionEmitter
{
    #region Public API
    #region Discovery and validation
    /// <summary>Identifies concrete graphics and compute owners independently of declaration attributes.</summary>
    public static bool IsRuntimeOwner(INamedTypeSymbol symbol)
    {
        if (symbol.IsStatic || symbol.IsAbstract) return false;
        for (var parent = symbol.BaseType; parent != null; parent = parent.BaseType)
            if (parent.ToDisplayString() is "VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram" or "VanillaGraphicsExpanded.Rendering.GpuComputeShader") return true;
        return false;
    }

    /// <summary>Generates contract submission unless a specialized owner supplies its own override.</summary>
    public static bool GeneratesSubmission(INamedTypeSymbol symbol) => IsRuntimeOwner(symbol) &&
        !symbol.GetMembers("Submit").OfType<IMethodSymbol>().Any(m => m.IsOverride);

    /// <summary>Recognizes packed CPU block sources without introducing a second binding declaration.</summary>
    public static bool IsCpuBuffer(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            if (current.ToDisplayString() == "VanillaGraphicsExpanded.Rendering.CpuUniformBuffer") return true;
        return false;
    }

    /// <summary>Rejects resource contracts that cannot supply a complete retained submission.</summary>
    public static void Validate(INamedTypeSymbol owner)
    {
        if (!GeneratesSubmission(owner)) return;
        foreach (var descriptor in InterfaceBindingReader.Properties(owner).Where(IsDescriptor))
        {
            var kind = BindingReader.ReadKind(Attributes(descriptor, "ShaderBinding").Single());
            if (kind is ShaderBindingKind.Sampler or ShaderBindingKind.Image or ShaderBindingKind.UniformBlock or ShaderBindingKind.StorageBlock or ShaderBindingKind.AtomicCounter)
                throw new ArgumentException($"Binding '{descriptor.Name}' supplies only a slot descriptor; automatic Submit requires a runtime resource property.");
        }
        foreach (var property in Resources(owner))
        {
            var implementation = InterfaceBindingReader.Implementation(owner, property);
            bool generated = InterfaceBindingReader.NeedsImplementation(implementation);
            if (!generated && property.GetMethod == null)
                throw new ArgumentException($"Binding '{property.Name}' needs a retained getter or a generated property for automatic Submit; an authored set-only binding cannot be submitted.");
            if (property.GetMethod != null && generated && property.SetMethod == null)
                throw new ArgumentException($"Binding '{property.Name}' requires an authored getter supplying its runtime resource.");
            var attribute = Attributes(property, "ShaderBinding").Single();
            var target = NamedEnum(attribute, "TextureTarget");
            if (property.Type.SpecialType == SpecialType.System_Int32 && target == null)
                throw new ArgumentException($"Texture-ID binding '{property.Name}' requires TextureTarget in its binding contract.");
            _ = NamedEnum(attribute, "Sampler");
        }
    }

    #endregion

    #region Emission
    /// <summary>Emits storage only for generated resource accessors, preserving authored source getters.</summary>
    public static string EmitProperties(INamedTypeSymbol owner)
    {
        var text = new StringBuilder();
        foreach (var property in InterfaceBindingReader.Properties(owner))
        {
            var implementation = InterfaceBindingReader.Implementation(owner, property);
            if (!InterfaceBindingReader.NeedsImplementation(implementation)) continue;
            string type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
            string modifiers = "public";
            string member = property.Name;
            if (owner.DeclaredAccessibility == Accessibility.Public && property.Type.DeclaredAccessibility == Accessibility.Internal)
            {
                modifiers = "";
                member = property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + property.Name;
            }
            if (implementation != null)
                modifiers = string.Join(" ", ((PropertyDeclarationSyntax)implementation.DeclaringSyntaxReferences[0].GetSyntax()).Modifiers.Select(m => m.Text));
            if (IsDescriptor(property))
            {
                var attr = Attributes(property, "ShaderBinding").Single();
                text.Append("/// <summary>Exposes the declared binding descriptor.</summary>\n").Append(modifiers).Append(' ').Append(type).Append(' ').Append(member)
                    .Append(" => new ").Append(type).Append('(').Append(Quote(Text(attr, 0))).Append(", ").Append(Argument(attr, 2).Value)
                    .Append(", ").Append(Named(attr, "Required").Value is false ? "false" : "true").Append(");\n");
                continue;
            }
            text.Append("private ").Append(type).Append(' ').Append(Field(property)).Append(" = default!;\n")
                .Append("/// <summary>Retains the declared input until the next shader use.</summary>\n")
                .Append(modifiers).Append(' ').Append(type).Append(' ').Append(member).Append(" { ");
            if (property.GetMethod != null) text.Append("get => ").Append(Field(property)).Append("; ");
            if (property.SetMethod != null) text.Append("set { RequireInputMutation(); ").Append(Field(property)).Append(" = value; } ");
            text.Append("}\n");
        }
        return text.ToString();
    }

    /// <summary>Snapshots each source once, validates the full resource set, then publishes each declared binding once.</summary>
    public static string Emit(INamedTypeSymbol owner)
    {
        if (!GeneratesSubmission(owner)) return string.Empty;
        var resources = Resources(owner).ToArray();
        var text = new StringBuilder("#region Submission\n/// <summary>Publishes retained inputs through the installed binding contract.</summary>\nprotected override void Submit()\n{\n");
        for (int i = 0; i < resources.Length; i++)
        {
            var property = resources[i];
            string source = InterfaceBindingReader.NeedsImplementation(InterfaceBindingReader.Implementation(owner, property))
                ? Field(property) : "((" + property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")this)." + property.Name;
            text.Append("var input").Append(i).Append(" = ").Append(source).Append(";\n");
        }
        foreach (bool validate in new[] { true, false })
            for (int i = 0; i < resources.Length; i++)
            {
                var property = resources[i];
                var attr = Attributes(property, "ShaderBinding").Single();
                var kind = BindingReader.ReadKind(attr);
                text.Append("global::VanillaGraphicsExpanded.Rendering.ShaderBindingSubmission.").Append(validate ? "Validate" : "").Append(kind)
                    .Append("(this, ").Append(Quote(Text(attr, 0))).Append(", ");
                if (validate) text.Append(Named(attr, "Required").Value is false ? "false, " : "true, ");
                // Select the managed-resource overload even when a concrete texture also converts to an engine ID.
                if (kind == ShaderBindingKind.Sampler && property.Type.SpecialType != SpecialType.System_Int32)
                    text.Append("(global::VanillaGraphicsExpanded.Rendering.GpuTexture?)");
                text.Append("input").Append(i);
                if (!validate && kind == ShaderBindingKind.Sampler)
                {
                    if (NamedEnum(attr, "TextureTarget") is { } target)
                        text.Append(", target: ").Append(Literal(target));
                    if (NamedEnum(attr, "Sampler") is { } sampler)
                        text.Append(", sampler: ").Append(Literal(sampler));
                }
                if (kind == ShaderBindingKind.AtomicCounter) text.Append(", ").Append(Argument(attr, 2).Value);
                text.Append(");\n");
            }
        return text.Append("}\n#endregion\n").ToString();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Filters layout-only descriptors out of runtime resource publication.</summary>
    private static IEnumerable<IPropertySymbol> Resources(INamedTypeSymbol owner) => InterfaceBindingReader.Properties(owner).Where(p => !IsDescriptor(p));
    /// <summary>Recognizes descriptor properties by their contract namespace.</summary>
    private static bool IsDescriptor(IPropertySymbol property) => property.Type.ToDisplayString().StartsWith(Prefix, StringComparison.Ordinal);
    /// <summary>Uses a reserved generated prefix to keep storage independent of public member names.</summary>
    private static string Field(IPropertySymbol property) => "__submitted_" + property.Name;
    #endregion
}
