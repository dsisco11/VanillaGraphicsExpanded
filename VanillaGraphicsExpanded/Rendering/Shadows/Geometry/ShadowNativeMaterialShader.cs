using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns native deformation snapshots shared by packaged terrain and standard-object executables.</summary>
internal abstract class ShadowNativeMaterialShader : GpuProgram
{
    private readonly PackedUniformBuffer parameters = new(208);

    #region Public API
    /// <summary>Identifies the installed native vertex-storage declaration required by this executable.</summary>
    internal abstract bool PackedLayout { get; }
    /// <summary>Identifies the primitive submitted to the executable's final producer stages.</summary>
    internal abstract PrimitiveTopology Topology { get; }

    /// <summary>Freezes the std140 material snapshot before a draw captures its logical uniform version.</summary>
    internal void CaptureMaterial(ReadOnlySpan<byte> data)
    {
        if (data.Length != 208) throw new ArgumentException("Native material snapshot is 208 bytes.", nameof(data));
        for (int offset = 0; offset < data.Length; offset += 4)
            if (offset != 132 && !float.IsFinite(BitConverter.ToSingle(data.Slice(offset, 4))))
                throw new ArgumentException("Native material snapshot contains nonfinite deformation input.", nameof(data));
        RequireInputMutation(); parameters.SetBytes(data);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes owned material storage through each generated stage binding contract.</summary>
    protected CpuUniformBuffer MaterialParameters => parameters;

    /// <summary>Retains material versions for the executable's existing RHI capture lifetime.</summary>
    protected ShadowNativeMaterialShader() { OwnUniformBuffer(parameters); }
    #endregion
}
