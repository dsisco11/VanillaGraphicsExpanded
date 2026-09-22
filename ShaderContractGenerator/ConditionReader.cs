using Microsoft.CodeAnalysis;
using VanillaGraphicsExpanded.Rendering.Contracts;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Builds closed structural condition trees from named attribute nodes.</summary>
internal sealed class ConditionReader(OwnerDeclaration owner)
{
    #region Condition resolution
    /// <summary>Resolves a condition and emits the same shared-model construction expression.</summary>
    public (ShaderCondition Model, string Expression) Read(string name, HashSet<string>? path = null)
    {
        path ??= new(StringComparer.Ordinal);
        if (!path.Add(name)) throw new ArgumentException($"Cyclic condition '{name}'.");
        try
        {
            var matches = new[] { "ShaderEquals", "ShaderAll", "ShaderAny", "ShaderNot" }
                .SelectMany(kind => Attributes(owner.Symbol, kind).Where(a => Text(a, 0) == name).Select(a => (kind, a))).ToArray();
            if (matches.Length != 1) throw new ArgumentException($"Condition '{name}' is missing or duplicated.");
            var (kind, attribute) = matches[0];
            if (kind == "ShaderEquals")
            {
                if (!owner.Options.TryGetValue(Text(attribute, 1), out var option)) throw new ArgumentException($"Condition '{name}' references an unknown option.");
                var value = Argument(attribute, 2);
                var valueType = option.Shared ? ((INamedTypeSymbol)option.Property.Type).TypeArguments[0] : option.Property.Type;
                if (!SymbolEqualityComparer.Default.Equals(value.Type, valueType)) throw new ArgumentException($"Condition '{name}' value must match option type '{option.TypeName}'.");
                var scalar = option.Model.Validate(Scalar(value));
                ShaderCondition model = option.Model switch
                {
                    ShaderOption<bool> key => ShaderCondition.Equal(key, scalar.Bits != 0),
                    ShaderOption<int> key => ShaderCondition.Equal(key, unchecked((int)scalar.Bits)),
                    ShaderOption<uint> key => ShaderCondition.Equal(key, scalar.Bits),
                    _ => throw new ArgumentException($"Condition '{name}' requires a structural scalar.")
                };
                return (model, $"ShaderCondition.Equal({option.KeyExpression}, ({option.TypeName}){Literal(value)})");
            }
            var names = kind == "ShaderNot" ? new[] { Text(attribute, 1) } : Argument(attribute, 1).Values.Select(v => (string)v.Value!).ToArray();
            var children = names.Select(child => Read(child, path)).ToArray();
            var result = kind switch
            {
                "ShaderAll" => ShaderCondition.All(children.Select(c => c.Model).ToArray()),
                "ShaderAny" => ShaderCondition.Any(children.Select(c => c.Model).ToArray()),
                _ => ShaderCondition.Not(children[0].Model)
            };
            return (result, "ShaderCondition." + kind[6..] + "(" + string.Join(", ", children.Select(c => c.Expression)) + ")");
        }
        finally { path.Remove(name); }
    }
    #endregion
}

