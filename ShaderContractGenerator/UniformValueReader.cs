using Microsoft.CodeAnalysis;
using VanillaGraphicsExpanded.Rendering.Contracts;
using static ShaderContractGenerator.AttributeValues;

namespace ShaderContractGenerator;

/// <summary>Classifies ordinary upload values separately from resource and layout descriptors.</summary>
internal static class UniformValueReader
{
    #region Public API
    /// <summary>Identifies value-bearing ordinary locations in the authoritative interface.</summary>
    internal static bool IsValue(IPropertySymbol property) =>
        BindingReader.ReadKind(Attributes(property, "ShaderBinding").Single()) == ShaderBindingKind.UniformLocation && Supports(property.Type);

    /// <summary>Accepts upload representations with exact comparison and owned array snapshots.</summary>
    internal static bool Supports(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return array.Rank == 1 && Supports(array.ElementType) && array.ElementType is not IArrayTypeSymbol;
        return type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32 or SpecialType.System_Single ||
            type.ToDisplayString() is "System.Numerics.Vector2" or "System.Numerics.Vector3" or "System.Numerics.Vector4" or "System.Numerics.Matrix4x4";
    }
    #endregion
}
