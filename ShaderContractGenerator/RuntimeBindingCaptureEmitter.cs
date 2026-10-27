using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Generates retained resource properties and immutable CPU capture from declared binding interfaces.</summary>
internal static class RuntimeBindingCaptureEmitter
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

    /// <summary>Generates contract capture unless a specialized owner supplies its own override.</summary>
    public static bool GeneratesCapture(INamedTypeSymbol symbol)
    {
        if (!IsRuntimeOwner(symbol)) return false;
        // The nearest authored implementation owns capture even through an abstract family.
        // A reabstracted hook restores the obligation to generate the concrete owner's body.
        for (var owner = symbol; owner != null; owner = owner.BaseType)
        {
            var method = owner.GetMembers("CaptureBindingsCore").OfType<IMethodSymbol>()
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

    /// <summary>Rejects resource contracts that cannot supply complete retained input capture.</summary>
    public static void Validate(INamedTypeSymbol owner)
    {
        if (!GeneratesCapture(owner)) return;
        foreach (var descriptor in InterfaceBindingReader.Properties(owner).Where(IsDescriptor))
        {
            var kind = BindingReader.ReadKind(Attributes(descriptor, "ShaderBinding").Single());
            if (kind is ShaderBindingKind.Sampler or ShaderBindingKind.Image or ShaderBindingKind.UniformBlock or ShaderBindingKind.StorageBlock or ShaderBindingKind.AtomicCounter)
                throw new ArgumentException($"Binding '{descriptor.Name}' supplies only a slot descriptor; automatic binding capture requires a runtime resource property.");
        }
        foreach (var property in Resources(owner))
        {
            var implementation = InterfaceBindingReader.Implementation(owner, property);
            bool generated = InterfaceBindingReader.NeedsImplementation(implementation);
            if (!generated && property.GetMethod == null)
                throw new ArgumentException($"Binding '{property.Name}' needs a retained getter or a generated property for automatic capture; an authored set-only binding cannot be captured.");
            if (property.GetMethod != null && generated && property.SetMethod == null)
                throw new ArgumentException($"Binding '{property.Name}' requires an authored getter supplying its runtime resource.");
            var attribute = Attributes(property, "ShaderBinding").Single();
            _ = NamedEnum(attribute, "TextureTarget");
            _ = NamedEnum(attribute, "Sampler");
        }
    }

    #endregion

    #region Emission
    /// <summary>Emits the shader-owned retained state and generated resource accessors.</summary>
    public static string EmitProperties(INamedTypeSymbol owner)
    {
        var text = new StringBuilder();
        var stateful = Resources(owner).Where(IsStateful).ToArray();
        if (IsRuntimeOwner(owner))
        {
            text.Append("/// <summary>Retains non-UBO binding inputs for this shader instance.</summary>\n")
                .Append("private struct ").Append(StateName(owner)).Append("\n{\n");
            foreach (var property in stateful)
                text.Append("internal ").Append(TypeName(property)).Append(' ').Append(property.Name).Append(" = default!;\n");
            text.Append("/// <summary>Initializes retained inputs.</summary>\npublic ").Append(StateName(owner)).Append("() { }\n}\nprivate ")
                .Append(StateName(owner)).Append(" __activeState = new();\n");
            text.Append("private ulong __inputRevision;\nprivate ulong __capturedInputRevision;\nprivate global::VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings? __capturedBindings;\n");
            foreach (var property in Resources(owner).Where(p => !IsStateful(p)))
                text.Append("private ").Append(IsCpuBuffer(property.Type) ? "global::VanillaGraphicsExpanded.Rendering.RHI.RHIUniformSnapshot?" : TypeName(property))
                    .Append(" __captured_").Append(property.Name).Append(" = default!;\n");
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
                if (property.Type is IArrayTypeSymbol) text.Append("global::VanillaGraphicsExpanded.Rendering.ShaderInputSnapshot.Equal(");
                else if (property.Type.IsReferenceType) text.Append("global::System.Object.ReferenceEquals(");
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

    /// <summary>Captures desired inputs once and reuses immutable tables without advancing execution or upload history.</summary>
    public static string Emit(INamedTypeSymbol owner)
    {
        if (!GeneratesCapture(owner)) return string.Empty;
        const string rhi = "global::VanillaGraphicsExpanded.Rendering.RHI.";
        var resources = Resources(owner).ToArray();
        var text = new StringBuilder("#region Binding capture\n/// <summary>Freezes retained resource inputs without native state, allocation or publication.</summary>\nprotected override " + rhi + "RHIResourceBindings CaptureBindingsCore()\n{\n");
        if (!resources.Any(IsStateful)) text.Append("_ = __activeState;\n");
        for (int i = 0; i < resources.Length; i++)
        {
            var property = resources[i];
            bool generated = InterfaceBindingReader.NeedsImplementation(InterfaceBindingReader.Implementation(owner, property));
            string source = generated ? IsStateful(property) ? "__activeState." + property.Name : Field(property)
                : "((" + property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")this)." + property.Name;
            text.Append("var input").Append(i).Append(" = ").Append(source).Append(";\n");
            if (IsStateful(property) && !generated)
            {
                text.Append("if (!");
                if (property.Type is IArrayTypeSymbol) text.Append("global::VanillaGraphicsExpanded.Rendering.ShaderInputSnapshot.Equal(");
                else if (property.Type.IsReferenceType) text.Append("global::System.Object.ReferenceEquals(");
                else text.Append("global::System.Collections.Generic.EqualityComparer<").Append(TypeName(property)).Append(">.Default.Equals(");
                text.Append("__activeState.").Append(property.Name).Append(", input").Append(i).Append(")) { __activeState.").Append(property.Name).Append(" = ");
                if (property.Type is IArrayTypeSymbol) text.Append("global::VanillaGraphicsExpanded.Rendering.ShaderInputSnapshot.Copy(input").Append(i).Append(')');
                else text.Append("input").Append(i);
                text.Append("; __inputRevision++; }\ninput").Append(i).Append(" = __activeState.").Append(property.Name).Append(";\n");
            }
            if (IsCpuBuffer(property.Type)) text.Append("var snapshot").Append(i).Append(" = input").Append(i).Append("?.Capture();\n");
        }
        text.Append("if (__capturedBindings is not null && __capturedBindings.Shader == Shader && __capturedInputRevision == __inputRevision");
        for (int i = 0; i < resources.Length; i++)
            if (!IsStateful(resources[i]))
                text.Append(" && global::System.Object.ReferenceEquals(__captured_").Append(resources[i].Name).Append(", ")
                    .Append(IsCpuBuffer(resources[i].Type) ? "snapshot" : "input").Append(i).Append(')');
        text.Append(") return __capturedBindings;\n");
        var samplerIndices = Enumerable.Range(0, resources.Length).Where(i => Kind(resources[i]) == ShaderBindingKind.Sampler).ToArray();
        if (samplerIndices.Length != 0)
        {
            text.Append("var samplers = new global::System.Collections.Generic.List<").Append(rhi).Append("RHISamplerBinding>();\n");
            foreach (int i in samplerIndices)
            {
                ulong id = Identity(resources[i]);
                if (resources[i].Type is IArrayTypeSymbol)
                    text.Append("if (input").Append(i).Append(" is not null) for (int element = 0; element < input").Append(i).Append(".Length; element++) samplers.Add(new(").Append(id).Append("UL, input").Append(i).Append("[element], element));\n");
                else text.Append("samplers.Add(new(").Append(id).Append("UL, (global::VanillaGraphicsExpanded.Rendering.GpuTextureRef)input").Append(i).Append("));\n");
            }
        }
        text.Append("var captured = new ").Append(rhi).Append("RHIResourceBindings(Shader, samplers: ").Append(samplerIndices.Length == 0 ? "null" : "samplers");
        text.Append(", images: [");
        text.Append(string.Join(", ", Enumerable.Range(0, resources.Length).Where(i => Kind(resources[i]) == ShaderBindingKind.Image).Select(i =>
            "new " + rhi + "RHIImageInput(" + Identity(resources[i]) + "UL, " +
            (resources[i].Type.IsValueType && resources[i].Type.ToDisplayString() != "VanillaGraphicsExpanded.Rendering.GpuTextureRef"
                ? "input" + i : "(global::VanillaGraphicsExpanded.Rendering.GpuTextureRef)input" + i) + ")")));
        text.Append("], buffers: [");
        text.Append(string.Join(", ", Enumerable.Range(0, resources.Length).Where(i => (Kind(resources[i]) is ShaderBindingKind.UniformBlock or ShaderBindingKind.StorageBlock or ShaderBindingKind.AtomicCounter) && !IsCpuBuffer(resources[i].Type))
            .Select(i => BufferCapture(resources[i], i, rhi))));
        text.Append("], uniforms: [");
        text.Append(string.Join(", ", Enumerable.Range(0, resources.Length).Where(i => IsCpuBuffer(resources[i].Type)).Select(i =>
            "new " + rhi + "RHIUniformBinding(" + Identity(resources[i]) + "UL, snapshot" + i + ")")));
        text.Append("]);\n__capturedInputRevision = __inputRevision;\n");
        for (int i = 0; i < resources.Length; i++)
            if (!IsStateful(resources[i])) text.Append("__captured_").Append(resources[i].Name).Append(" = ")
                .Append(IsCpuBuffer(resources[i].Type) ? "snapshot" : "input").Append(i).Append(";\n");
        return text.Append("return __capturedBindings = captured;\n}\n#endregion\n").ToString();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Reads the single authored resource kind used by capture and its generated identity.</summary>
    private static ShaderBindingKind Kind(IPropertySymbol property) => BindingReader.ReadKind(Attributes(property, "ShaderBinding").Single());
    /// <summary>Computes the same immutable entry identity used during executable preparation.</summary>
    private static ulong Identity(IPropertySymbol property) => GpuBindingEntry.Identity(Kind(property), Text(Attributes(property, "ShaderBinding").Single(), 0));
    /// <summary>Emits explicit whole/ranged buffer intent without capturing current capacity or native names.</summary>
    private static string BufferCapture(IPropertySymbol property, int index, string rhi)
    {
        string input = "input" + index;
        string role = Kind(property) switch { ShaderBindingKind.UniformBlock => "Uniform", ShaderBindingKind.StorageBlock => "ShaderStorage", _ => "AtomicCounter" };
        string prefix = "new " + rhi + "RHIBufferBinding(" + Identity(property) + "UL, " + rhi + "BufferRole." + role + ", ";
        if (property.Type.ToDisplayString() == "VanillaGraphicsExpanded.Rendering.GpuStorageBufferBinding")
            return input + ".Buffer is null ? " + prefix + "default) : " + prefix + input + ".Buffer, " + input + ".OffsetBytes, " + input + ".SizeBytes, false)";
        return prefix + "(global::VanillaGraphicsExpanded.Rendering.GpuBufferRef)" + input + ")";
    }
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
