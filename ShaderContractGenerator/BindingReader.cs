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
    /// <summary>Reads the shared binding kind and rejects undefined enum constants before classification.</summary>
    internal static ShaderBindingKind ReadKind(AttributeData attribute)
    {
        var kind = (ShaderBindingKind)(int)Argument(attribute, 1).Value!;
        if (!Enum.IsDefined(typeof(ShaderBindingKind), kind))
            throw new ArgumentException($"Invalid binding kind '{kind}'.");
        return kind;
    }
    /// <summary>Implements typed descriptors or runtime resource setters while keeping game types out of offline shells.</summary>
    public static string EmitProperties(OwnerDeclaration owner, bool offline)
    {
        return offline ? string.Empty : InterfaceBindingReader.EmitProperties(owner.Symbol);
    }

    /// <summary>Validates declarations even on unused shared layouts so authoring errors cannot remain dormant.</summary>
    public static void Validate(INamedTypeSymbol owner)
    {
        if (owner.GetMembers().Any(m => m is not IPropertySymbol && Attributes(m, "ShaderBinding").Any()))
            throw new ArgumentException("Shader bindings require attributed interface properties; slot fields are unsupported.");
        if (owner.TypeKind != TypeKind.Interface && owner.GetMembers().Any(m => Attributes(m, "ShaderBinding").Any()))
            throw new ArgumentException("ShaderBinding metadata belongs on interface properties; concrete properties implement that contract without repeating attributes.");
        var programs = Attributes(owner, "ShaderProgram").ToDictionary(a => Text(a, 0), a =>
            Attributes(owner, "ShaderStage").Where(s => Text(s, 0) == Text(a, 0)).Select(s => (ShaderStageKind)(int)Argument(s, 1).Value!).ToArray(), StringComparer.Ordinal);
        foreach (var property in owner.GetMembers().OfType<IPropertySymbol>())
            foreach (var attribute in Attributes(property, "ShaderBinding"))
                _ = ReadProperty(property, attribute);
        foreach (var reference in Attributes(owner, "ShaderBindingSet"))
        {
            string? program = Text(reference, "Program");
            if (programs.Count != 0 && program != null && !programs.ContainsKey(program)) throw new ArgumentException($"Binding set references unknown program '{program}'.");
            if (Argument(reference, 0).Value is not INamedTypeSymbol shared || shared.TypeKind != TypeKind.Interface)
                throw new ArgumentException("Binding sets require an interface declaration.");
            var stages = Named(reference, "Stages");
            if (!stages.IsNull && (stages.Values.Length == 0 || stages.Values.Distinct().Count() != stages.Values.Length || stages.Values.Any(v => !Enum.IsDefined(typeof(ShaderStageKind), (int)v.Value!))))
                throw new ArgumentException("Binding sets require distinct valid stage filters.");
        }
        if (owner.TypeKind == TypeKind.Class) InterfaceBindingReader.ValidateImplementations(owner);
        else InterfaceBindingReader.ValidateHierarchy(owner);
    }

    /// <summary>Creates the exact model and construction expression for a consumer stage.</summary>
    public static (GpuBindingContract Model, string Expression) Read(INamedTypeSymbol owner, string program, ShaderStageKind stage)
    {
        var declarations = new List<(Slot Slot, bool Defaults)>();
        Collect(owner, false, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
        var selected = new Dictionary<(ShaderBindingKind Kind, string Name), Slot>();
        var explicitSlots = new List<Slot>();
        // Normalize explicit inherited overrides before dictionary insertion, so a composite
        // interface can resolve sibling declarations regardless of traversal or type-name order.
        var effective = declarations.Where(d => d.Defaults || !declarations.Any(other => !other.Defaults &&
            InterfaceBindingReader.Overrides(other.Slot.Property, d.Slot.Property)));
        foreach (var declaration in effective.OrderBy(d => d.Defaults))
        {
            var slot = declaration.Slot;
            var key = (slot.Kind, slot.Name);
            if (selected.TryGetValue(key, out var prior))
            {
                // A redeclared interface property owns the explicit override. Unrelated
                // declarations remain ambiguous even if equal.
                if (!declaration.Defaults && InterfaceBindingReader.Overrides(slot.Property, prior.Property))
                {
                    selected[key] = slot;
                    explicitSlots.Remove(prior);
                    explicitSlots.Add(slot);
                    continue;
                }
                if (!declaration.Defaults && InterfaceBindingReader.Overrides(prior.Property, slot.Property)) continue;
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
        foreach (var group in selected.Values.Where(s => s.Kind is ShaderBindingKind.Sampler or ShaderBindingKind.Image or ShaderBindingKind.UniformBlock or ShaderBindingKind.StorageBlock).GroupBy(s => s.Name, StringComparer.Ordinal))
            if (group.Count() > 1) throw new ArgumentException($"Conflicting resource kinds for '{group.Key}' in '{program}'/{stage}.");
        var model = new GpuBindingContract();
        foreach (var slot in selected.Values) Apply(model, slot);
        string expression = "new GpuBindingContract { " + string.Join(", ", selected.Values.GroupBy(s => Map(s.Kind)).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key + " = { " + string.Join(", ", g.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s =>
                "{ " + Quote(s.Name) + ", " + (s.Kind is ShaderBindingKind.UniformLocation or ShaderBindingKind.VaryingLocation or ShaderBindingKind.FragmentOutputLocation ? s.Index.ToString() : $"new GpuBindingContract.Binding({s.Index}, {s.Required.ToString().ToLowerInvariant()})") + " }")) + " }")) + " }";
        return (model, expression);

        /// <summary>Follows declared sets and base classes, rejecting cycles and retaining each property's ownership.</summary>
        void Collect(INamedTypeSymbol type, bool defaults, HashSet<INamedTypeSymbol> stack)
        {
            if (!stack.Add(type)) throw new ArgumentException($"Cyclic binding set '{type}'.");
            if (type.BaseType is { SpecialType: not SpecialType.System_Object } parent) Collect(parent, defaults, stack);
            foreach (var contract in type.Interfaces.OrderBy(i => i.ToDisplayString(), StringComparer.Ordinal)) Collect(contract, defaults, stack);
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
                if (Argument(reference, 0).Value is not INamedTypeSymbol shared) throw new ArgumentException("Binding sets require a declared interface.");
                Collect(shared, defaults || Named(reference, "Defaults").Value is true, stack);
            }
            stack.Remove(type);
        }
    }
    #endregion

    #region Private
    /// <summary>Requires an ordinary interface property whose type agrees with its resource kind.</summary>
    private static void ValidateProperty(IPropertySymbol property, AttributeData attribute)
    {
        var syntax = property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        bool contract = property.ContainingType.TypeKind == TypeKind.Interface;
        if (syntax == null || property.IsIndexer || property.ReturnsByRef || property.ReturnsByRefReadonly ||
            !contract || syntax.Modifiers.Any(SyntaxKind.PartialKeyword) || syntax.Initializer != null ||
            (syntax.AccessorList == null && syntax.ExpressionBody == null))
            throw new ArgumentException($"Binding property '{property.Name}' requires an ordinary interface property.");
        var kind = ReadKind(attribute);
        string type = property.Type.ToDisplayString();
        bool descriptor = type == Prefix + "Shader" + kind + "Binding";
        string? resourceType = kind switch
        {
            ShaderBindingKind.Sampler => "GpuTexture", ShaderBindingKind.Image => "GpuTextureBinding", ShaderBindingKind.UniformBlock => "GpuUniformBuffer", ShaderBindingKind.StorageBlock => "GpuShaderStorageBuffer", _ => null
        };
        type = type.TrimEnd('?');
        bool cpuBuffer = kind == ShaderBindingKind.UniformBlock && RuntimeSubmissionEmitter.IsCpuBuffer(property.Type);
        bool resource = cpuBuffer || (resourceType != null && type == "VanillaGraphicsExpanded.Rendering." + resourceType) ||
            (contract && kind == ShaderBindingKind.Sampler && property.Type.SpecialType == SpecialType.System_Int32) ||
            (kind == ShaderBindingKind.Image && type == "VanillaGraphicsExpanded.Rendering.GpuTexture");
        if ((descriptor && (property.GetMethod == null || property.SetMethod != null)) ||
            (resource && (property.IsStatic || (property.GetMethod == null && property.SetMethod == null) || property.SetMethod?.IsInitOnly == true)) ||
            (!descriptor && !resource) || property.IsStatic || property.DeclaredAccessibility != Accessibility.Public)
            throw new ArgumentException($"Binding property '{property.Name}' has unsupported type/accessors for {kind}.");
    }

    /// <summary>Finds the established runtime layout boundary without emitting engine types offline.</summary>
    internal static bool HasRuntimeTarget(INamedTypeSymbol type)
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
        var kind = ReadKind(attribute);
        _ = NamedEnum(attribute, "TextureTarget");
        _ = NamedEnum(attribute, "Sampler");
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
        return new(property, name, kind, index, Named(attribute, "Required").Value is not false, targets, stages);
    }

    /// <summary>Maps declaration kinds to the shared model's independent dictionaries.</summary>
    private static string Map(ShaderBindingKind kind) => kind switch
    {
        ShaderBindingKind.UniformLocation => nameof(GpuBindingContract.UniformLocations),
        ShaderBindingKind.Sampler => nameof(GpuBindingContract.Samplers),
        ShaderBindingKind.Image => nameof(GpuBindingContract.Images),
        ShaderBindingKind.UniformBlock => nameof(GpuBindingContract.UniformBlocks),
        ShaderBindingKind.StorageBlock => nameof(GpuBindingContract.StorageBlocks),
        ShaderBindingKind.VaryingLocation => nameof(GpuBindingContract.VaryingLocations),
        ShaderBindingKind.FragmentOutputLocation => nameof(GpuBindingContract.FragmentOutputLocations),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Populates the same pure model used for compile-time shared-stage equivalence.</summary>
    private static void Apply(GpuBindingContract model, Slot slot)
    {
        switch (slot.Kind)
        {
            case ShaderBindingKind.UniformLocation: model.UniformLocations.Add(slot.Name, slot.Index); break;
            case ShaderBindingKind.VaryingLocation: model.VaryingLocations.Add(slot.Name, slot.Index); break;
            case ShaderBindingKind.FragmentOutputLocation: model.FragmentOutputLocations.Add(slot.Name, slot.Index); break;
            case ShaderBindingKind.Sampler: model.RegisterSamplerUnit(slot.Name, slot.Index, slot.Required); break;
            case ShaderBindingKind.Image: model.RegisterImageUnit(slot.Name, slot.Index, slot.Required); break;
            case ShaderBindingKind.UniformBlock: model.RegisterUniformBlockBinding(slot.Name, slot.Index, slot.Required); break;
            case ShaderBindingKind.StorageBlock: model.RegisterShaderStorageBlockBinding(slot.Name, slot.Index, slot.Required); break;
        }
    }

    /// <summary>Contains one validated slot and its explicit consumer applicability.</summary>
    private sealed record Slot(IPropertySymbol Property, string Name, ShaderBindingKind Kind, int Index, bool Required, string[] Programs, ShaderStageKind[] Stages);
    #endregion
}
