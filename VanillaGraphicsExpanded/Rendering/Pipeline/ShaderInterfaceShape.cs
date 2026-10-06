using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Numeric shader interface shape shared by vertex and fragment compatibility checks.</summary>
internal readonly record struct ShaderInterfaceShape(VertexInterpretation Interpretation, int Components, int Columns = 1, bool Unsigned = false)
{
    #region Public API
    /// <summary>Maps native reflected scalar/vector/matrix types to occupied attribute locations.</summary>
    internal static ShaderInterfaceShape From(ActiveAttribType type) => type switch
    {
        ActiveAttribType.Float => new(VertexInterpretation.Floating, 1),
        ActiveAttribType.FloatVec2 => new(VertexInterpretation.Floating, 2),
        ActiveAttribType.FloatVec3 => new(VertexInterpretation.Floating, 3),
        ActiveAttribType.FloatVec4 => new(VertexInterpretation.Floating, 4),
        ActiveAttribType.Int => new(VertexInterpretation.Integer, 1),
        ActiveAttribType.IntVec2 => new(VertexInterpretation.Integer, 2),
        ActiveAttribType.IntVec3 => new(VertexInterpretation.Integer, 3),
        ActiveAttribType.IntVec4 => new(VertexInterpretation.Integer, 4),
        ActiveAttribType.UnsignedInt => new(VertexInterpretation.Integer, 1, Unsigned: true),
        ActiveAttribType.UnsignedIntVec2 => new(VertexInterpretation.Integer, 2, Unsigned: true),
        ActiveAttribType.UnsignedIntVec3 => new(VertexInterpretation.Integer, 3, Unsigned: true),
        ActiveAttribType.UnsignedIntVec4 => new(VertexInterpretation.Integer, 4, Unsigned: true),
        ActiveAttribType.Double => new(VertexInterpretation.Double, 1),
        ActiveAttribType.DoubleVec2 => new(VertexInterpretation.Double, 2),
        ActiveAttribType.DoubleVec3 => new(VertexInterpretation.Double, 3),
        ActiveAttribType.DoubleVec4 => new(VertexInterpretation.Double, 4),
        ActiveAttribType.DoubleMat2 => new(VertexInterpretation.Double, 2, 2),
        ActiveAttribType.DoubleMat3 => new(VertexInterpretation.Double, 3, 3),
        ActiveAttribType.DoubleMat4 => new(VertexInterpretation.Double, 4, 4),
        ActiveAttribType.DoubleMat2x3 => new(VertexInterpretation.Double, 3, 2),
        ActiveAttribType.DoubleMat2x4 => new(VertexInterpretation.Double, 4, 2),
        ActiveAttribType.DoubleMat3x2 => new(VertexInterpretation.Double, 2, 3),
        ActiveAttribType.DoubleMat3x4 => new(VertexInterpretation.Double, 4, 3),
        ActiveAttribType.DoubleMat4x2 => new(VertexInterpretation.Double, 2, 4),
        ActiveAttribType.DoubleMat4x3 => new(VertexInterpretation.Double, 3, 4),
        ActiveAttribType.FloatMat2 => new(VertexInterpretation.Floating, 2, 2),
        ActiveAttribType.FloatMat3 => new(VertexInterpretation.Floating, 3, 3),
        ActiveAttribType.FloatMat4 => new(VertexInterpretation.Floating, 4, 4),
        ActiveAttribType.FloatMat2x3 => new(VertexInterpretation.Floating, 3, 2),
        ActiveAttribType.FloatMat2x4 => new(VertexInterpretation.Floating, 4, 2),
        ActiveAttribType.FloatMat3x2 => new(VertexInterpretation.Floating, 2, 3),
        ActiveAttribType.FloatMat3x4 => new(VertexInterpretation.Floating, 4, 3),
        ActiveAttribType.FloatMat4x2 => new(VertexInterpretation.Floating, 2, 4),
        ActiveAttribType.FloatMat4x3 => new(VertexInterpretation.Floating, 3, 4),
        _ => throw new NotSupportedException($"Unsupported graphics interface type {type}.")
    };

    /// <summary>Checks storage interpretation and integer signedness without requiring shader input debug names.</summary>
    internal bool Accepts(VertexAttributeDesc attribute)
    {
        if (Interpretation == VertexInterpretation.Floating)
            return attribute.Interpretation is VertexInterpretation.Floating or VertexInterpretation.Normalized;
        if (Interpretation != attribute.Interpretation) return false;
        return Interpretation != VertexInterpretation.Integer || Unsigned == (attribute.Storage is
            VertexAttribPointerType.UnsignedByte or VertexAttribPointerType.UnsignedShort or VertexAttribPointerType.UnsignedInt);
    }
    #endregion
}
