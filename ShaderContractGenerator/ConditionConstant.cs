using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ShaderContractGenerator;

/// <summary>Reads literal scalars and declared enum members without executing or binding arbitrary expressions.</summary>
internal static class ConditionConstant
{
    #region Typed constants
    /// <summary>Accepts exact Boolean/int/uint literals or a member of the option's own enum type.</summary>
    public static object Read(OptionDeclaration option, ExpressionSyntax syntax)
    {
        var type = option.Shared ? ((INamedTypeSymbol)option.Property.Type).TypeArguments[0] : option.Property.Type;
        if (type.TypeKind == TypeKind.Enum)
        {
            // Only the expected enum is searched; arbitrary member access and numeric enum casts are excluded.
            if (syntax is MemberAccessExpressionSyntax member && member.Name is IdentifierNameSyntax name)
            {
                string? qualifier = EnumQualifier(member.Expression);
                if (qualifier == type.Name || qualifier == type.ToDisplayString() || qualifier == type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                {
                    var field = type.GetMembers(name.Identifier.ValueText).OfType<IFieldSymbol>().SingleOrDefault(f => f.HasConstantValue);
                    if (field != null) return field.ConstantValue!;
                }
            }
            throw new ArgumentException($"Expected a declared constant of enum '{type}'.");
        }
        object? value = syntax is LiteralExpressionSyntax literal ? literal.Token.Value : null;
        // C# represents negative numbers as unary syntax. Preserve the int minimum boundary without truncation.
        if (syntax is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression) && unary.Operand is LiteralExpressionSyntax number)
            value = number.Token.Value switch
            {
                int signed => -signed,
                uint unsigned when unsigned == 2147483648u && !number.Token.Text.EndsWith("u", StringComparison.OrdinalIgnoreCase) => int.MinValue,
                _ => null
            };
        return (type.SpecialType, value) switch
        {
            (SpecialType.System_Boolean, bool boolean) => boolean,
            (SpecialType.System_Int32, int signed) => signed,
            (SpecialType.System_UInt32, uint unsigned) => unsigned,
            _ => throw new ArgumentException($"Expected a literal matching option type '{type}'.")
        };
    }
    /// <summary>Normalizes only qualified type names, permitting trivia and escaped identifiers without general member evaluation.</summary>
    private static string? EnumQualifier(ExpressionSyntax syntax) => syntax switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } member when EnumQualifier(member.Expression) is { } prefix =>
            prefix + "." + name.Identifier.ValueText,
        AliasQualifiedNameSyntax { Name: IdentifierNameSyntax name } alias when alias.Alias.Identifier.ValueText == "global" =>
            "global::" + name.Identifier.ValueText,
        _ => null
    };
    #endregion
}
