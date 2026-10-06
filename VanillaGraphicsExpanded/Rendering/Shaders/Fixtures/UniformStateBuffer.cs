using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Retains typed fixture inputs with std140 array stride and immutable publication.</summary>
internal sealed class UniformStateBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates zero defaults for all numeric values, including both array elements.</summary>
    internal UniformStateBuffer() : base(128) { }
    /// <summary>Retains the scalar independently of its array neighbors.</summary>
    internal float Scalar { get => MemoryMarshal.Read<float>(Bytes); set => WriteFloat(0, value); }
    /// <summary>Copies both scalar array elements on assignment and returns independent snapshots.</summary>
    internal float[] Values
    {
        get => [MemoryMarshal.Read<float>(Bytes.Slice(16)), MemoryMarshal.Read<float>(Bytes.Slice(32))];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length != 2) throw new ArgumentException("The shader array requires exactly two elements.", nameof(value));
            // Each scalar occupies its own std140 array stride; padding remains zero.
            WriteFloat(16, value[0]);
            WriteFloat(32, value[1]);
        }
    }
    /// <summary>Retains the three occupied vector components without changing matrix alignment.</summary>
    internal Vector3 Vector { get => MemoryMarshal.Read<Vector3>(Bytes.Slice(48)); set => WriteVector3(48, value); }
    /// <summary>Retains the matrix in the established shader column ordering.</summary>
    internal Matrix4x4 Transform { get => MemoryMarshal.Read<Matrix4x4>(Bytes.Slice(64)); set => WriteMatrix4(64, value); }
    #endregion
}
