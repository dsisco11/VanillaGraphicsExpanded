using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VanillaGraphicsExpanded.Rendering.Contracts;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Resolves property layouts and validates independent index namespaces before either consumer compiles.</summary>
internal static class BindingReader
{
    #region Public API
    /// <summary>Implements typed descriptors or runtime resource setters while keeping game types out of offline shells.</summary>
    public static string EmitProperties(OwnerDeclaration owner, bool offline)
    {
        var text = new System.Text.StringBuilder();
        foreach (var property in owner.Symbol.GetMembers().OfType<IPropertySymbol>().Where(p => Attributes(p, "ShaderBinding").Any()).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var attribute = Attributes(property, "ShaderBinding").Single();
            var slot = ReadProperty(property, attribute);
            if (!property.IsStatic && offline) continue;
            var syntax = (PropertyDeclarationSyntax)property.DeclaringSyntaxReferences[0].GetSyntax();
            var modifiers = syntax.Modifiers.Where(m => !m.IsKind(SyntaxKind.PartialKeyword) || !offline || owner.OfflineSourcePresent).Select(m => m.Text);
            string type = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            text.Append("/// <summary>Generated typed access to the declared GPU resource.</summary>\n")
                .Append(string.Join(" ", modifiers)).Append(' ').Append(type).Append(' ').Append(property.Name).Append(" { ");
            if (property.IsStatic)
                text.Append("get => new ").Append(type).Append('(').Append(Quote(slot.Name)).Append(", ").Append(slot.Index).Append(", ").Append(slot.Required ? "true" : "false").Append("); ");
            else
            {
                string target = owner.Symbol.GetMembers("pipeline").OfType<IFieldSymbol>().Any(f => f.Type.ToDisplayString() == "VanillaGraphicsExpanded.Rendering.GpuComputePipeline") ? "pipeline.ProgramLayout, pipeline.ProgramId" : "ProgramLayout, ProgramId";
                text.Append("set => global::VanillaGraphicsExpanded.Rendering.ShaderBindingAccess.").Append(slot.Kind)
                    .Append('(').Append(target).Append(", ").Append(Quote(slot.Name)).Append(", value); ");
            }
            text.Append("}\n");
        }
        return text.ToString();
    }

    /// <summary>Validates declarations even on unused shared layouts so authoring errors cannot remain dormant.</summary>
    public static void Validate(INamedTypeSymbol owner)
    {
        if (owner.GetMembers().Any(m => m is not IPropertySymbol && Attributes(m, "ShaderBinding").Any()))
            throw new ArgumentException("Shader bindings require strongly typed partial properties; slot fields are unsupported.");
        var programs = Attributes(owner, "ShaderProgram").ToDictionary(a => Text(a, 0), a =>
            Attributes(owner, "ShaderStage").Where(s => Text(s, 0) == Text(a, 0)).Select(s => (ShaderStageKind)(int)Argument(s, 1).Value!).ToArray(), StringComparer.Ordinal);
        foreach (var property in owner.GetMembers().OfType<IPropertySymbol>())
            foreach (var attribute in Attributes(property, "ShaderBinding"))
            {
                var slot = ReadProperty(property, attribute);
                foreach (string program in slot.Programs)
                    if (programs.Count != 0 && !programs.ContainsKey(program)) throw new ArgumentException($"Binding property '{property.Name}' references unknown program '{program}'.");
                if (programs.Count != 0)
                    foreach (var stage in slot.Stages)
                        if (!programs.Where(p => slot.Programs.Length == 0 || slot.Programs.Contains(p.Key)).Any(p => p.Value.Contains(stage)))
                            throw new ArgumentException($"Binding property '{property.Name}' references undeclared stage '{stage}'.");
            }
        foreach (var reference in Attributes(owner, "ShaderBindingSet"))
        {
            string? program = Text(reference, "Program");
            if (programs.Count != 0 && program != null && !programs.ContainsKey(program)) throw new ArgumentException($"Binding set references unknown program '{program}'.");
            if (Argument(reference, 0).Value is not INamedTypeSymbol { TypeKind: TypeKind.Class }) throw new ArgumentException("Binding sets require a class declaration.");
            var stages = Named(reference, "Stages");
            if (!stages.IsNull && (stages.Values.Length == 0 || stages.Values.Distinct().Count() != stages.Values.Length || stages.Values.Any(v => !Enum.IsDefined(typeof(ShaderStageKind), (int)v.Value!))))
                throw new ArgumentException("Binding sets require distinct valid stage filters.");
        }
    }

    /// <summary>Creates the exact model and construction expression for a consumer stage.</summary>
    public static (GpuBindingContract Model, string Expression) Read(INamedTypeSymbol owner, string program, ShaderStageKind stage)
    {
        var declarations = new List<(Slot Slot, bool Defaults)>();
        Collect(owner, false, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
        var selected = new Dictionary<(string Kind, string Name), Slot>();
        var explicitSlots = new List<Slot>();
        foreach (var declaration in declarations.OrderBy(d => d.Defaults))
        {
            var slot = declaration.Slot;
            var key = (slot.Kind, slot.Name);
            if (selected.TryGetValue(key, out var prior))
            {
                if (declaration.Defaults)
                {
                    // Explicit ownership overrides include defaults. Two independently authored
                    // defaults must still agree; traversal order must never select a GPU slot.
                    if (!explicitSlots.Any(s => s.Kind == slot.Kind && s.Name == slot.Name) && (prior.Index != slot.Index || prior.Required != slot.Required))
                        throw new ArgumentException($"Conflicting default binding '{slot.Name}' in {slot.Kind}.");
                    continue;
                }
                throw new ArgumentException($"Duplicate binding '{slot.Name}' in {slot.Kind} for '{program}'/{stage}.");
            }
            selected.Add(key, slot);
            if (!declaration.Defaults) explicitSlots.Add(slot);
        }
        foreach (var group in explicitSlots.GroupBy(s => (s.Kind, s.Index)))
            if (group.Count() > 1)
                throw new ArgumentException($"Conflicting {group.Key.Kind} index {group.Key.Index} for '{program}'/{stage}.");
        foreach (var group in selected.Values.Where(s => s.Kind is "Sampler" or "Image" or "UniformBlock" or "StorageBlock").GroupBy(s => s.Name, StringComparer.Ordinal))
            if (group.Count() > 1) throw new ArgumentException($"Conflicting resource kinds for '{group.Key}' in '{program}'/{stage}.");
        var model = new GpuBindingContract();
        foreach (var slot in selected.Values) Apply(model, slot);
        string expression = "new GpuBindingContract { " + string.Join(", ", selected.Values.GroupBy(s => Map(s.Kind)).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key + " = { " + string.Join(", ", g.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s =>
                "{ " + Quote(s.Name) + ", " + (s.Kind.EndsWith("Location", StringComparison.Ordinal) ? s.Index.ToString() : $"new GpuBindingContract.Binding({s.Index}, {s.Required.ToString().ToLowerInvariant()})") + " }")) + " }")) + " }";
        return (model, expression);

        /// <summary>Follows declared sets and base classes, rejecting cycles and retaining each property's ownership.</summary>
        void Collect(INamedTypeSymbol type, bool defaults, HashSet<INamedTypeSymbol> stack)
        {
            if (!stack.Add(type)) throw new ArgumentException($"Cyclic binding set '{type}'.");
            if (type.BaseType is { SpecialType: not SpecialType.System_Object } parent) Collect(parent, defaults, stack);
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
                foreach (var attribute in Attributes(property, "ShaderBinding"))
                {
                    var slot = ReadProperty(property, attribute);
                    if ((slot.Programs.Length == 0 || slot.Programs.Contains(program)) && slot.Stages.Contains(stage) &&
                        !declarations.Any(d => SymbolEqualityComparer.Default.Equals(d.Slot.Property, property) && d.Defaults == defaults))
                        declarations.Add((slot, defaults));
                }
            foreach (var reference in Attributes(type, "ShaderBindingSet"))
            {
                string? target = Text(reference, "Program");
                if (target != null && target != program) continue;
                var stages = Named(reference, "Stages");
                if (!stages.IsNull && !stages.Values.Any(v => (int)v.Value! == (int)stage)) continue;
                if (Argument(reference, 0).Value is not INamedTypeSymbol shared) throw new ArgumentException("Binding sets require a declared class.");
                Collect(shared, defaults || Named(reference, "Defaults").Value is true, stack);
            }
            stack.Remove(type);
        }
    }
    #endregion

    #region Private
    /// <summary>Requires an unimplemented partial property whose type agrees with its resource kind.</summary>
    private static void ValidateProperty(IPropertySymbol property, AttributeData attribute)
    {
        var syntax = property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (syntax == null || property.IsIndexer || property.ReturnsByRef || property.ReturnsByRefReadonly ||
            !syntax.Modifiers.Any(SyntaxKind.PartialKeyword) || syntax.Initializer != null ||
            syntax.AccessorList == null || syntax.AccessorList.Accessors.Any(a => a.Body != null || a.ExpressionBody != null))
            throw new ArgumentException($"Binding property '{property.Name}' requires a defining partial property.");
        int kind = (int)Argument(attribute, 1).Value!;
        string[] names = { "UniformLocation", "Sampler", "Image", "UniformBlock", "StorageBlock", "VaryingLocation", "FragmentOutputLocation" };
        if (kind < 0 || kind >= names.Length) throw new ArgumentException($"Invalid binding kind on '{property.Name}'.");
        string type = property.Type.ToDisplayString();
        bool descriptor = type == Prefix + "Shader" + names[kind] + "Binding";
        string? resourceType = names[kind] switch
        {
            "Sampler" => "GpuTexture", "Image" => "GpuTextureBinding", "UniformBlock" => "GpuUniformBuffer", "StorageBlock" => "GpuShaderStorageBuffer", _ => null
        };
        bool resource = (resourceType != null && type == "VanillaGraphicsExpanded.Rendering." + resourceType) ||
            (names[kind] == "Image" && type == "VanillaGraphicsExpanded.Rendering.GpuTexture");
        if ((descriptor && (!property.IsStatic || property.GetMethod == null || property.SetMethod != null)) ||
            (resource && (property.IsStatic || property.GetMethod != null || property.SetMethod == null || property.SetMethod.IsInitOnly)) ||
            (!descriptor && !resource))
            throw new ArgumentException($"Binding property '{property.Name}' has unsupported type/accessors for {names[kind]}.");
        if (resource && !HasRuntimeTarget(property.ContainingType))
            throw new ArgumentException($"Binding property '{property.Name}' requires a GpuProgram owner or an owning GpuComputePipeline field named 'pipeline'.");
    }

    /// <summary>Finds the established runtime layout boundary without emitting engine types offline.</summary>
    private static bool HasRuntimeTarget(INamedTypeSymbol type)
    {
        for (var owner = type; owner != null; owner = owner.BaseType)
            if (owner.ToDisplayString() == "VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram") return true;
        return type.GetMembers("pipeline").OfType<IFieldSymbol>().Any(f => f.Type.ToDisplayString() == "VanillaGraphicsExpanded.Rendering.GpuComputePipeline");
    }

    /// <summary>Rejects unsupported storage, malformed identifiers and stage lists at the declaring property.</summary>
    private static Slot ReadProperty(IPropertySymbol property, AttributeData attribute)
    {
        ValidateProperty(property, attribute);
        int index = (int)Argument(attribute, 2).Value!;
        if (index < 0) throw new ArgumentException($"Binding property '{property.Name}' has a negative index.");
        string name = Text(attribute, 0);
        ShaderContractNames.ValidateIdentifier(name);
        int kind = (int)Argument(attribute, 1).Value!;
        string[] kinds = { "UniformLocation", "Sampler", "Image", "UniformBlock", "StorageBlock", "VaryingLocation", "FragmentOutputLocation" };
        if (kind < 0 || kind >= kinds.Length) throw new ArgumentException($"Invalid binding kind on '{property.Name}'.");
        var stages = Argument(attribute, 3).Values.Select(v => (ShaderStageKind)(int)v.Value!).ToArray();
        if (stages.Length == 0 || stages.Distinct().Count() != stages.Length || stages.Any(s => !Enum.IsDefined(typeof(ShaderStageKind), s)))
            throw new ArgumentException($"Binding property '{property.Name}' requires distinct explicit stages.");
        string? program = Text(attribute, "Program");
        var programs = Named(attribute, "Programs");
        if (program != null && !programs.IsNull) throw new ArgumentException($"Binding property '{property.Name}' cannot specify both Program and Programs.");
        string[] targets = program != null ? new[] { program } : programs.IsNull ? System.Array.Empty<string>() : programs.Values.Select(v => (string)v.Value!).ToArray();
        if (!programs.IsNull && targets.Length == 0 || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new ArgumentException($"Binding property '{property.Name}' requires distinct program members.");
        foreach (string target in targets) ShaderContractNames.ValidateIdentifier(target);
        return new(property, name, kinds[kind], index, Named(attribute, "Required").Value is not false, targets, stages);
    }

    /// <summary>Maps declaration kinds to the shared model's independent dictionaries.</summary>
    private static string Map(string kind) => kind switch
    {
        "UniformLocation" => "UniformLocations", "Sampler" => "Samplers", "Image" => "Images",
        "UniformBlock" => "UniformBlocks", "StorageBlock" => "StorageBlocks", "VaryingLocation" => "VaryingLocations", _ => "FragmentOutputLocations"
    };

    /// <summary>Populates the same pure model used for compile-time shared-stage equivalence.</summary>
    private static void Apply(GpuBindingContract model, Slot slot)
    {
        switch (slot.Kind)
        {
            case "UniformLocation": model.UniformLocations.Add(slot.Name, slot.Index); break;
            case "VaryingLocation": model.VaryingLocations.Add(slot.Name, slot.Index); break;
            case "FragmentOutputLocation": model.FragmentOutputLocations.Add(slot.Name, slot.Index); break;
            case "Sampler": model.RegisterSamplerUnit(slot.Name, slot.Index, slot.Required); break;
            case "Image": model.RegisterImageUnit(slot.Name, slot.Index, slot.Required); break;
            case "UniformBlock": model.RegisterUniformBlockBinding(slot.Name, slot.Index, slot.Required); break;
            case "StorageBlock": model.RegisterShaderStorageBlockBinding(slot.Name, slot.Index, slot.Required); break;
        }
    }

    /// <summary>Contains one validated slot and its explicit consumer applicability.</summary>
    private sealed record Slot(IPropertySymbol Property, string Name, string Kind, int Index, bool Required, string[] Programs, ShaderStageKind[] Stages);
    #endregion
}
