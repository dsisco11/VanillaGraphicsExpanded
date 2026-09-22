using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderContractGenerator;

/// <summary>Reads strongly bound attribute constants and formats safe generated C# literals.</summary>
internal static class AttributeValues
{
    internal const string Prefix = "VanillaGraphicsExpanded.Rendering.Contracts.";

    #region Semantic metadata
    /// <summary>Selects only the actual declaration attribute type, regardless of aliases or spelling.</summary>
    public static IEnumerable<AttributeData> Attributes(ISymbol symbol, string name) =>
        symbol.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == Prefix + name + "Attribute");
    /// <summary>Reads a required bound constructor argument.</summary>
    public static TypedConstant Argument(AttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Length <= index || attribute.ConstructorArguments[index].Kind == TypedConstantKind.Error)
            throw new ArgumentException("Unresolved attribute argument; use a compile-time constant or typeof/nameof reference.");
        return attribute.ConstructorArguments[index];
    }
    /// <summary>Reads an optional named attribute argument.</summary>
    public static TypedConstant Named(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(p => p.Key == name).Value;
    /// <summary>Reads a string-valued required constructor argument.</summary>
    public static string Text(AttributeData attribute, int index) => Argument(attribute, index).Value as string ?? throw new ArgumentException("Expected a non-null string argument.");
    /// <summary>Reads a named string or its declaration default.</summary>
    public static string? Text(AttributeData attribute, string name, string? fallback = null) => Named(attribute, name).Value as string ?? fallback;
    /// <summary>Formats a quoted C# string, preserving escaping.</summary>
    public static string Quote(string text) => SymbolDisplay.FormatLiteral(text, true);
    /// <summary>Formats the supported scalar set without locale-dependent output or lossy widening.</summary>
    public static string Literal(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Enum)
            return "(" + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" + Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
        return constant.Value switch
        {
            bool value => value ? "true" : "false",
            int value => value.ToString(CultureInfo.InvariantCulture),
            uint value => value.ToString(CultureInfo.InvariantCulture) + "u",
            float value when float.IsFinite(value) => value.ToString("R", CultureInfo.InvariantCulture) + "f",
            _ => throw new ArgumentException("Expected bool, int, uint, finite float or a 32-bit enum constant.")
        };
    }
    /// <summary>Maps supported metadata scalars onto the shared validation model.</summary>
    public static ShaderScalar Scalar(TypedConstant value) => value.Value switch
    {
        bool v => ShaderScalar.From(v), int v => ShaderScalar.From(v), uint v => ShaderScalar.From(v),
        float v => ShaderScalar.From(v), _ => throw new ArgumentException("Unsupported scalar constant.")
    };
    /// <summary>Reads an explicitly declared array, distinguishing omission from an empty array.</summary>
    public static TypedConstant[]? Array(TypedConstant value) => value.Kind == TypedConstantKind.Array && !value.IsNull ? value.Values.ToArray() : null;
    #endregion
}
