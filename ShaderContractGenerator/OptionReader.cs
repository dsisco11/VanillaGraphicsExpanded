using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VanillaGraphicsExpanded.Rendering.Contracts;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Resolves typed option accessors and shared keys without evaluating user code.</summary>
internal sealed class OptionReader(Dictionary<INamedTypeSymbol, OwnerDeclaration> owners)
{
    private readonly HashSet<IPropertySymbol> reading = new(SymbolEqualityComparer.Default);

    #region Option reading
    /// <summary>Resolves one option, allowing typed references to another attributed declaration.</summary>
    public OptionDeclaration Read(OwnerDeclaration owner, IPropertySymbol property)
    {
        if (owner.Options.TryGetValue(property.Name, out var existing)) return existing;
        if (!reading.Add(property)) throw new ArgumentException($"Cyclic option reference '{property.Name}'.");
        try
        {
            ValidateProperty(property);
            var definition = Attributes(property, "ShaderOption").SingleOrDefault();
            var reference = Attributes(property, "ShaderOptionReference").SingleOrDefault();
            if ((definition == null) == (reference == null)) throw new ArgumentException($"Option '{property.Name}' needs exactly one definition or shared reference.");
            bool shared = property.IsStatic;
            var valueType = shared && property.Type is INamedTypeSymbol { Name: "ShaderOption", TypeArguments.Length: 1 } keyType &&
                keyType.ContainingNamespace.ToDisplayString() == Prefix.TrimEnd('.') ? keyType.TypeArguments[0] : property.Type;
            if (valueType.TypeKind == TypeKind.Enum && valueType.ContainingType != null) throw new ArgumentException("Shader option enums must be top-level declarations.");
            string typeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string keyName = shared ? property.Name : property.Name + "Option";
            ShaderOption model;
            string initializer;
            if (reference != null)
            {
                var type = Argument(reference, 0).Value as INamedTypeSymbol;
                string member = Text(reference, 1);
                if (type == null || !owners.TryGetValue(type, out var target) || type.GetMembers(member).OfType<IPropertySymbol>().SingleOrDefault() is not { } targetProperty)
                    throw new ArgumentException($"Unknown shared option '{type}.{member}'.");
                var option = Read(target, targetProperty);
                if (!option.Shared || option.TypeName != typeName) throw new ArgumentException($"Shared option '{member}' has an incompatible type or is not a static key.");
                model = option.Model;
                initializer = option.KeyExpression;
            }
            else
            {
                string name = Text(definition!, 0);
                var fallback = Argument(definition!, 1);
                var domain = Array(Named(definition!, "Domain"));
                var minimum = Named(definition!, "Minimum");
                var maximum = Named(definition!, "Maximum");
                var aliases = Array(Named(definition!, "Aliases"))?.Select(v => (string)v.Value!).ToArray();
                // Attribute constants must match the accessor exactly; implicit numeric coercion is not a shader setting rule.
                foreach (var value in new[] { fallback }.Concat(domain ?? []).Concat(minimum.IsNull || minimum.Type == null ? [] : new[] { minimum }).Concat(maximum.IsNull || maximum.Type == null ? [] : new[] { maximum }))
                    if (!SymbolEqualityComparer.Default.Equals(value.Type, valueType)) throw new ArgumentException($"Option '{name}' constant does not match accessor type '{typeName}'.");
                model = CreateModel(valueType, name, fallback, domain, minimum, maximum, aliases);
                initializer = $"new ShaderOption<{typeName}>({Quote(name)}, {Literal(fallback)}, " +
                    (domain == null ? "null" : "new " + typeName + "[] { " + string.Join(", ", domain.Select(Literal)) + " }") + ", " +
                    (minimum.Type == null || minimum.IsNull ? "null" : Literal(minimum)) + ", " +
                    (maximum.Type == null || maximum.IsNull ? "null" : Literal(maximum)) + ", " +
                    (aliases == null ? "null" : "new string[] { " + string.Join(", ", aliases.Select(Quote)) + " }") + ")";
            }
            var result = new OptionDeclaration(property, typeName, keyName, model, initializer, owner.Name + "." + keyName, shared);
            owner.Options.Add(property.Name, result);
            return result;
        }
        finally { reading.Remove(property); }
    }
    /// <summary>Enforces generated getter/setter ownership and rejects fields, auto-properties and indexers.</summary>
    private static void ValidateProperty(IPropertySymbol property)
    {
        if (property.IsIndexer || property.ReturnsByRef || property.ReturnsByRefReadonly || property.ExplicitInterfaceImplementations.Length != 0 ||
            property.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax syntax ||
            !syntax.Modifiers.Any(SyntaxKind.PartialKeyword) || syntax.ExpressionBody != null || syntax.Initializer != null ||
            syntax.AccessorList == null || syntax.AccessorList.Accessors.Any(a => a.Body != null || a.ExpressionBody != null || a.IsKind(SyntaxKind.InitAccessorDeclaration)) ||
            property.GetMethod == null || (property.IsStatic ? property.SetMethod != null : property.SetMethod == null))
            throw new ArgumentException($"Option '{property.Name}' must be a partial instance get/set property or static get-only ShaderOption<T> key.");
        if (property.IsStatic && (property.Type is not INamedTypeSymbol { Name: "ShaderOption", TypeArguments.Length: 1 } type || type.ContainingNamespace.ToDisplayString() != Prefix.TrimEnd('.')))
            throw new ArgumentException($"Static option '{property.Name}' must expose ShaderOption<T>.");
    }
    /// <summary>Constructs the existing scalar model, using the enum's exact underlying scalar for compile-time validation.</summary>
    private static ShaderOption CreateModel(ITypeSymbol type, string name, TypedConstant fallback, TypedConstant[]? domain,
        TypedConstant minimum, TypedConstant maximum, string[]? aliases)
    {
        var scalarType = type.TypeKind == TypeKind.Enum ? ((INamedTypeSymbol)type).EnumUnderlyingType! : type;
        T? Bound<T>(TypedConstant value) where T : struct => value.Type == null || value.IsNull ? null : (T)value.Value!;
        return scalarType.SpecialType switch
        {
            SpecialType.System_Boolean => new ShaderOption<bool>(name, (bool)fallback.Value!, domain?.Select(v => (bool)v.Value!), Bound<bool>(minimum), Bound<bool>(maximum), aliases),
            SpecialType.System_Int32 => new ShaderOption<int>(name, (int)fallback.Value!, domain?.Select(v => (int)v.Value!), Bound<int>(minimum), Bound<int>(maximum), aliases),
            SpecialType.System_UInt32 => new ShaderOption<uint>(name, (uint)fallback.Value!, domain?.Select(v => (uint)v.Value!), Bound<uint>(minimum), Bound<uint>(maximum), aliases),
            SpecialType.System_Single => new ShaderOption<float>(name, (float)fallback.Value!, domain?.Select(v => (float)v.Value!), Bound<float>(minimum), Bound<float>(maximum), aliases),
            _ => throw new ArgumentException($"Unsupported option type '{type}'.")
        };
    }
    #endregion
}

