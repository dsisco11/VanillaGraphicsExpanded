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
            if (parent.ToDisplayString() is "VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram" or "VanillaGraphicsExpanded.Rendering.GpuComputeProgram") return true;
        return false;
    }

    /// <summary>Generates contract submission unless a specialized owner supplies its own override.</summary>
    public static bool GeneratesSubmission(INamedTypeSymbol symbol)
    {
        if (!IsRuntimeOwner(symbol)) return false;
        // The nearest authored implementation owns publication even through an abstract family.
        // A reabstracted hook restores the obligation to generate the concrete owner's body.
        for (var owner = symbol; owner != null; owner = owner.BaseType)
        {
            var method = owner.GetMembers("Submit").OfType<IMethodSymbol>()
                .FirstOrDefault(candidate => !candidate.IsStatic && candidate.Parameters.Length == 0);
            if (method != null) return method.IsAbstract || !method.IsOverride;
        }
        return true;
    }

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
            if (BindingReader.ReadKind(attribute) == ShaderBindingKind.Sampler && property.Type.SpecialType == SpecialType.System_Int32 && target == null)
                throw new ArgumentException($"Texture-ID binding '{property.Name}' requires TextureTarget in its binding contract.");
            _ = NamedEnum(attribute, "Sampler");
        }
    }

    #endregion

    #region Emission
    /// <summary>Emits the shader-owned retained state and generated resource accessors.</summary>
    public static string EmitProperties(INamedTypeSymbol owner)
    {
        var text = new StringBuilder();
        foreach (var property in Resources(owner))
            text.Append("private global::VanillaGraphicsExpanded.Rendering.")
                .Append("ShaderInputValidation")
                .Append(" __validation_").Append(property.Name).Append(";\n");
        var stateful = Resources(owner).Where(IsStateful).ToArray();
        if (IsRuntimeOwner(owner))
        {
            text.Append("/// <summary>Retains non-UBO binding inputs for this shader instance.</summary>\n")
                .Append("private struct ").Append(StateName(owner)).Append("\n{\n");
            foreach (var property in stateful)
                text.Append("internal ").Append(TypeName(property)).Append(' ').Append(property.Name).Append(" = default!;\n");
            text.Append("/// <summary>Initializes retained inputs.</summary>\npublic ").Append(StateName(owner)).Append("() { }\n}\nprivate ")
                .Append(StateName(owner)).Append(" __activeState = new();\n");
            if (stateful.Length != 0) text.Append("private ulong __inputRevision;\n");
        }
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
            bool statefulProperty = IsStateful(property);
            if (!statefulProperty)
                text.Append("private ").Append(type).Append(' ').Append(Field(property)).Append(" = default!;\n");
            text.Append("/// <summary>Retains the declared input until the next shader use.</summary>\n")
                .Append(modifiers).Append(' ').Append(type).Append(' ').Append(member).Append(" { ");
            string storage = statefulProperty ? "__activeState." + property.Name : Field(property);
            if (property.GetMethod != null) text.Append("get => ").Append(property.Type is IArrayTypeSymbol ? "global::VanillaGraphicsExpanded.Rendering.ShaderInputSnapshot.Copy(" + storage + ")" : storage).Append("; ");
            if (property.SetMethod != null)
            {
                text.Append("set { RequireInputMutation(); if (");
                if (property.Type.IsReferenceType) text.Append("global::System.Object.ReferenceEquals(");
                else text.Append("global::System.Collections.Generic.EqualityComparer<").Append(type).Append(">.Default.Equals(");
                text.Append(storage).Append(", value)) return; ").Append(storage).Append(" = ")
                    .Append(property.Type is IArrayTypeSymbol ? "global::VanillaGraphicsExpanded.Rendering.ShaderInputSnapshot.Copy(value)" : "value").Append("; ");
                if (statefulProperty) text.Append("__inputRevision++; ");
                text.Append("} ");
            }
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
        if (!resources.Any(IsStateful)) text.Append("_ = __activeState;\n");
        for (int i = 0; i < resources.Length; i++)
        {
            var property = resources[i];
            string source = InterfaceBindingReader.NeedsImplementation(InterfaceBindingReader.Implementation(owner, property))
                ? (IsStateful(property) ? "__activeState." + property.Name : Field(property))
                : "((" + property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")this)." + property.Name;
            text.Append("var input").Append(i).Append(" = ").Append(source).Append(";\n");
            if (IsStateful(property) && !InterfaceBindingReader.NeedsImplementation(InterfaceBindingReader.Implementation(owner, property)))
            {
                text.Append("if (!");
                if (property.Type.IsReferenceType) text.Append("global::System.Object.ReferenceEquals(");
                else text.Append("global::System.Collections.Generic.EqualityComparer<").Append(TypeName(property)).Append(">.Default.Equals(");
                text.Append("__activeState.").Append(property.Name).Append(", input").Append(i).Append(")) { __activeState.").Append(property.Name).Append(" = ");
                if (property.Type is IArrayTypeSymbol) text.Append("global::VanillaGraphicsExpanded.Rendering.ShaderInputSnapshot.Copy(input").Append(i).Append(')');
                else text.Append("input").Append(i);
                // Keep publication/lifetime checks independent of desired-input change tracking.
                // The conditional revision records only an actual authored input change.
                text.Append("; __inputRevision++; }\ninput").Append(i).Append(" = __activeState.").Append(property.Name).Append(";\n");
            }
            var declaration = Attributes(property, "ShaderBinding").Single();
            ulong identity = GpuBindingEntry.Identity(BindingReader.ReadKind(declaration), Text(declaration, 0));
            text.Append("var binding").Append(i).Append(" = __validation_").Append(property.Name).Append(".Resolve(this, ").Append(identity).Append("UL);\n");
        }
        foreach (bool validate in new[] { true, false })
            for (int i = 0; i < resources.Length; i++)
            {
                var property = resources[i];
                var attr = Attributes(property, "ShaderBinding").Single();
                var kind = BindingReader.ReadKind(attr);
                if (validate && kind == ShaderBindingKind.Sampler && property.Type.SpecialType == SpecialType.System_Int32)
                    text.Append("input").Append(i).Append(" = ");
                text.Append("global::VanillaGraphicsExpanded.Rendering.ShaderPreparedSubmission.").Append(validate ? "Validate" : "").Append(kind)
                    .Append("(binding").Append(i).Append(", ");
                // Select the managed-resource overload even when a concrete texture also converts to an engine ID.
                if (kind == ShaderBindingKind.Sampler && property.Type.SpecialType != SpecialType.System_Int32)
                    text.Append("(global::VanillaGraphicsExpanded.Rendering.GpuTexture?)");
                text.Append("input").Append(i);
                if (validate && (kind == ShaderBindingKind.Image ||
                    (kind == ShaderBindingKind.Sampler && property.Type.SpecialType != SpecialType.System_Int32) ||
                    (kind == ShaderBindingKind.StorageBlock && property.Type.IsValueType)))
                    text.Append(", ref __validation_").Append(property.Name);
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
    /// <summary>Excludes UBO sources because their owners already retain mutable upload state.</summary>
    private static bool IsStateful(IPropertySymbol property) => !IsDescriptor(property) &&
        BindingReader.ReadKind(Attributes(property, "ShaderBinding").Single()) != ShaderBindingKind.UniformBlock;
    /// <summary>Builds a stable nested state type name from the concrete shader owner.</summary>
    private static string StateName(INamedTypeSymbol owner) => owner.Name + "State";
    /// <summary>Formats nullable resource types consistently for generated state fields.</summary>
    private static string TypeName(IPropertySymbol property) => property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
    /// <summary>Uses a reserved generated prefix to keep storage independent of public member names.</summary>
    private static string Field(IPropertySymbol property) => "__submitted_" + property.Name;
    #endregion
}
