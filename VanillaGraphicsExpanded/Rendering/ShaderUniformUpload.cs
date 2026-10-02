using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes ordinary values at prepared numeric locations through the rendering boundary.</summary>
internal static class ShaderUniformUpload
{
    #region Public API
    /// <summary>Reports the linked GLSL type required by a supported upload representation.</summary>
    internal static ActiveUniformType Type<T>(T value) => value switch
    {
        bool or bool[] => ActiveUniformType.Bool,
        int or int[] => ActiveUniformType.Int,
        float or float[] => ActiveUniformType.Float,
        Vector2 or Vector2[] => ActiveUniformType.FloatVec2,
        Vector3 or Vector3[] => ActiveUniformType.FloatVec3,
        Vector4 or Vector4[] => ActiveUniformType.FloatVec4,
        Matrix4x4 or Matrix4x4[] => ActiveUniformType.FloatMat4,
        _ => throw new InvalidOperationException("An active ordinary uniform requires a supported value.")
    };

    /// <summary>Uploads only after the complete generated input set has passed validation.</summary>
    internal static void Write<T>(int location, T value)
    {
        switch (value)
        {
            case bool scalar: GL.Uniform1(location, scalar ? 1 : 0); break;
            case int scalar: GL.Uniform1(location, scalar); break;
            case float scalar: GL.Uniform1(location, scalar); break;
            case Vector2 vector: GL.Uniform2(location, vector.X, vector.Y); break;
            case Vector3 vector: GL.Uniform3(location, vector.X, vector.Y, vector.Z); break;
            case Vector4 vector: GL.Uniform4(location, vector.X, vector.Y, vector.Z, vector.W); break;
            case Matrix4x4 matrix: GL.UniformMatrix4(location, 1, false, Floats(new[] { matrix })); break;
            case bool[] array: GL.Uniform1(location, array.Length, array.Select(v => v ? 1 : 0).ToArray()); break;
            case int[] array: GL.Uniform1(location, array.Length, array); break;
            case float[] array: GL.Uniform1(location, array.Length, array); break;
            case Vector2[] array: GL.Uniform2(location, array.Length, Floats(array)); break;
            case Vector3[] array: GL.Uniform3(location, array.Length, Floats(array)); break;
            case Vector4[] array: GL.Uniform4(location, array.Length, Floats(array)); break;
            case Matrix4x4[] array: GL.UniformMatrix4(location, array.Length, false, Floats(array)); break;
            default: throw new InvalidOperationException("Unsupported ordinary uniform upload representation.");
        }
#if DEBUG
        GlDebug.ThrowIfErrors("ordinary uniform publication");
#endif
    }
    #endregion

    #region Private
    /// <summary>Copies contiguous vector/matrix data only when an actual upload is required.</summary>
    private static float[] Floats<T>(T[] values) where T : unmanaged => MemoryMarshal.Cast<T, float>(values.AsSpan()).ToArray();
    #endregion
}
