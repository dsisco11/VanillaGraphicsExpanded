using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates semantic vertex component storage to supported native attribute representations.</summary>
internal static class OpenGLVertexValues
{
    #region Internal API
    /// <summary>Maps floating attribute format storage directly to core named-configuration enum members.</summary>
    internal static OpenTK.Graphics.OpenGL4.VertexAttribType FloatingFormat(VertexComponentFormat value) => value switch
    {
        VertexComponentFormat.SignedByte => OpenTK.Graphics.OpenGL4.VertexAttribType.Byte,
        VertexComponentFormat.UnsignedByte => OpenTK.Graphics.OpenGL4.VertexAttribType.UnsignedByte,
        VertexComponentFormat.SignedShort => OpenTK.Graphics.OpenGL4.VertexAttribType.Short,
        VertexComponentFormat.UnsignedShort => OpenTK.Graphics.OpenGL4.VertexAttribType.UnsignedShort,
        VertexComponentFormat.SignedInt => OpenTK.Graphics.OpenGL4.VertexAttribType.Int,
        VertexComponentFormat.UnsignedInt => OpenTK.Graphics.OpenGL4.VertexAttribType.UnsignedInt,
        VertexComponentFormat.Half => OpenTK.Graphics.OpenGL4.VertexAttribType.HalfFloat,
        VertexComponentFormat.Float => OpenTK.Graphics.OpenGL4.VertexAttribType.Float,
        VertexComponentFormat.Double => OpenTK.Graphics.OpenGL4.VertexAttribType.Double,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps integer-preserving attribute storage directly to core named-configuration enum members.</summary>
    internal static OpenTK.Graphics.OpenGL4.VertexAttribIType IntegerFormat(VertexComponentFormat value) => value switch
    {
        VertexComponentFormat.SignedByte => OpenTK.Graphics.OpenGL4.VertexAttribIType.Byte,
        VertexComponentFormat.UnsignedByte => OpenTK.Graphics.OpenGL4.VertexAttribIType.UnsignedByte,
        VertexComponentFormat.SignedShort => OpenTK.Graphics.OpenGL4.VertexAttribIType.Short,
        VertexComponentFormat.UnsignedShort => OpenTK.Graphics.OpenGL4.VertexAttribIType.UnsignedShort,
        VertexComponentFormat.SignedInt => OpenTK.Graphics.OpenGL4.VertexAttribIType.Int,
        VertexComponentFormat.UnsignedInt => OpenTK.Graphics.OpenGL4.VertexAttribIType.UnsignedInt,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps storage for floating or normalized vertex interpretation, including double-to-float conversion.</summary>
    internal static VertexAttribPointerType FloatingStorage(VertexComponentFormat value) => value switch
    {
        VertexComponentFormat.SignedByte => VertexAttribPointerType.Byte,
        VertexComponentFormat.UnsignedByte => VertexAttribPointerType.UnsignedByte,
        VertexComponentFormat.SignedShort => VertexAttribPointerType.Short,
        VertexComponentFormat.UnsignedShort => VertexAttribPointerType.UnsignedShort,
        VertexComponentFormat.SignedInt => VertexAttribPointerType.Int,
        VertexComponentFormat.UnsignedInt => VertexAttribPointerType.UnsignedInt,
        VertexComponentFormat.Half => VertexAttribPointerType.HalfFloat,
        VertexComponentFormat.Float => VertexAttribPointerType.Float,
        VertexComponentFormat.Double => VertexAttribPointerType.Double,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps integer-preserving storage and rejects noninteger component formats.</summary>
    internal static VertexAttribIntegerType IntegerStorage(VertexComponentFormat value) => value switch
    {
        VertexComponentFormat.SignedByte => VertexAttribIntegerType.Byte,
        VertexComponentFormat.UnsignedByte => VertexAttribIntegerType.UnsignedByte,
        VertexComponentFormat.SignedShort => VertexAttribIntegerType.Short,
        VertexComponentFormat.UnsignedShort => VertexAttribIntegerType.UnsignedShort,
        VertexComponentFormat.SignedInt => VertexAttribIntegerType.Int,
        VertexComponentFormat.UnsignedInt => VertexAttribIntegerType.UnsignedInt,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    #endregion
}
