using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Numeric shader interface shape shared by vertex and fragment compatibility checks.</summary>
internal readonly record struct ShaderInterfaceShape(VertexInterpretation Interpretation, int Components, int Columns = 1, bool Unsigned = false)
{
    #region Public API
    /// <summary>Maps native reflected scalar/vector/matrix types to occupied attribute locations.</summary>
    internal static ShaderInterfaceShape From(ShaderValueType type) => type switch
    {
        ShaderValueType.Float => new(VertexInterpretation.Floating, 1),
        ShaderValueType.FloatVec2 => new(VertexInterpretation.Floating, 2),
        ShaderValueType.FloatVec3 => new(VertexInterpretation.Floating, 3),
        ShaderValueType.FloatVec4 => new(VertexInterpretation.Floating, 4),
        ShaderValueType.Int => new(VertexInterpretation.Integer, 1),
        ShaderValueType.IntVec2 => new(VertexInterpretation.Integer, 2),
        ShaderValueType.IntVec3 => new(VertexInterpretation.Integer, 3),
        ShaderValueType.IntVec4 => new(VertexInterpretation.Integer, 4),
        ShaderValueType.UnsignedInt => new(VertexInterpretation.Integer, 1, Unsigned: true),
        ShaderValueType.UnsignedIntVec2 => new(VertexInterpretation.Integer, 2, Unsigned: true),
        ShaderValueType.UnsignedIntVec3 => new(VertexInterpretation.Integer, 3, Unsigned: true),
        ShaderValueType.UnsignedIntVec4 => new(VertexInterpretation.Integer, 4, Unsigned: true),
        ShaderValueType.Double => new(VertexInterpretation.Double, 1),
        ShaderValueType.DoubleVec2 => new(VertexInterpretation.Double, 2),
        ShaderValueType.DoubleVec3 => new(VertexInterpretation.Double, 3),
        ShaderValueType.DoubleVec4 => new(VertexInterpretation.Double, 4),
        ShaderValueType.DoubleMat2 => new(VertexInterpretation.Double, 2, 2),
        ShaderValueType.DoubleMat3 => new(VertexInterpretation.Double, 3, 3),
        ShaderValueType.DoubleMat4 => new(VertexInterpretation.Double, 4, 4),
        ShaderValueType.DoubleMat2x3 => new(VertexInterpretation.Double, 3, 2),
        ShaderValueType.DoubleMat2x4 => new(VertexInterpretation.Double, 4, 2),
        ShaderValueType.DoubleMat3x2 => new(VertexInterpretation.Double, 2, 3),
        ShaderValueType.DoubleMat3x4 => new(VertexInterpretation.Double, 4, 3),
        ShaderValueType.DoubleMat4x2 => new(VertexInterpretation.Double, 2, 4),
        ShaderValueType.DoubleMat4x3 => new(VertexInterpretation.Double, 3, 4),
        ShaderValueType.FloatMat2 => new(VertexInterpretation.Floating, 2, 2),
        ShaderValueType.FloatMat3 => new(VertexInterpretation.Floating, 3, 3),
        ShaderValueType.FloatMat4 => new(VertexInterpretation.Floating, 4, 4),
        ShaderValueType.FloatMat2x3 => new(VertexInterpretation.Floating, 3, 2),
        ShaderValueType.FloatMat2x4 => new(VertexInterpretation.Floating, 4, 2),
        ShaderValueType.FloatMat3x2 => new(VertexInterpretation.Floating, 2, 3),
        ShaderValueType.FloatMat3x4 => new(VertexInterpretation.Floating, 4, 3),
        ShaderValueType.FloatMat4x2 => new(VertexInterpretation.Floating, 2, 4),
        ShaderValueType.FloatMat4x3 => new(VertexInterpretation.Floating, 3, 4),
        _ => throw new NotSupportedException($"Unsupported graphics interface type {type}.")
    };

    /// <summary>Checks storage interpretation and integer signedness without requiring shader input debug names.</summary>
    internal bool Accepts(RHIVertexAttribute attribute)
    {
        if (Interpretation == VertexInterpretation.Floating)
            return attribute.Interpretation is VertexInterpretation.Floating or VertexInterpretation.Normalized;
        if (Interpretation != attribute.Interpretation) return false;
        return Interpretation != VertexInterpretation.Integer || Unsigned == (attribute.Storage is
            VertexComponentFormat.UnsignedByte or VertexComponentFormat.UnsignedShort or VertexComponentFormat.UnsignedInt);
    }
    #endregion
}
