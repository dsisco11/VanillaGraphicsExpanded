using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderContractGenerator;

/// <summary>Parses restricted authoring expressions into the shared structural condition model.</summary>
internal sealed class ConditionReader(OwnerDeclaration owner, IReadOnlyList<ShaderOption> structural)
{
    #region Expression resolution
    /// <summary>Rejects malformed or partially parsed input before lowering every syntax node.</summary>
    public (ShaderCondition Model, string Expression) Read(string expression)
    {
        var syntax = SyntaxFactory.ParseExpression(expression, consumeFullText: true);
        if (string.IsNullOrWhiteSpace(expression) || syntax.ContainsDiagnostics)
            throw new ArgumentException("Malformed condition expression.");
        var result = Lower(syntax);
        result.Model.Validate(structural, "condition");
        return result;
    }

    /// <summary>Whitelists operators; even unreachable operands are validated rather than executed.</summary>
    private (ShaderCondition Model, string Expression) Lower(ExpressionSyntax syntax)
    {
        switch (syntax)
        {
            case ParenthesizedExpressionSyntax parentheses:
                return Lower(parentheses.Expression);
            case IdentifierNameSyntax name:
                return Equal(Option(name), true);
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                return (ShaderCondition.All(), "ShaderCondition.All()");
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression):
                return (ShaderCondition.Any(), "ShaderCondition.Any()");
            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                var child = Lower(unary.Operand);
                return (ShaderCondition.Not(child.Model), $"ShaderCondition.Not({child.Expression})");
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression):
                var left = Lower(binary.Left);
                var right = Lower(binary.Right);
                bool all = binary.IsKind(SyntaxKind.LogicalAndExpression);
                return (all ? ShaderCondition.All(left.Model, right.Model) : ShaderCondition.Any(left.Model, right.Model),
                    $"ShaderCondition.{(all ? "All" : "Any")}({left.Expression}, {right.Expression})");
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression):
                if (binary.Left is not IdentifierNameSyntax property)
                    throw new ArgumentException("Equality requires an option property on the left and a typed constant on the right.");
                var option = Option(property);
                return Equal(option, ConditionConstant.Read(option, binary.Right));
            default:
                throw new ArgumentException($"Unsupported condition syntax '{syntax.Kind()}'.");
        }
    }

    /// <summary>Resolves only local attributed property names, never GLSL names, aliases or generated keys.</summary>
    private OptionDeclaration Option(IdentifierNameSyntax name) => owner.Options.TryGetValue(name.Identifier.ValueText, out var option)
        ? option : throw new ArgumentException($"Unknown option property '{name.Identifier.ValueText}'.");

    /// <summary>Validates exact scalar types and domains, then emits the same typed equality at runtime.</summary>
    private static (ShaderCondition Model, string Expression) Equal(OptionDeclaration option, object value)
    {
        ShaderCondition model = (option.Model, value) switch
        {
            (ShaderOption<bool> key, bool scalar) => ShaderCondition.Equal(key, scalar),
            (ShaderOption<int> key, int scalar) => ShaderCondition.Equal(key, scalar),
            (ShaderOption<uint> key, uint scalar) => ShaderCondition.Equal(key, scalar),
            _ => throw new ArgumentException($"Condition constant must match structural option type '{option.TypeName}'.")
        };
        string literal = value switch
        {
            bool boolean => boolean ? "true" : "false",
            uint number => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "u",
            int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("Unsupported condition constant.")
        };
        return (model, $"ShaderCondition.Equal({option.KeyExpression}, ({option.TypeName}){literal})");
    }
    #endregion
}
