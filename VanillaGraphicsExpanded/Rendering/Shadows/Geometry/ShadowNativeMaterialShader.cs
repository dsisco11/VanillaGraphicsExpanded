using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns native deformation snapshots shared by packaged terrain and standard-object executables.</summary>
internal abstract class ShadowNativeMaterialShader : GpuProgram
{
    private readonly ShadowMaterialParameters parameters = new();

    #region Public API
    /// <summary>Identifies the installed native vertex-storage declaration required by this executable.</summary>
    internal abstract bool PackedLayout { get; }
    /// <summary>Identifies the primitive submitted to the executable's final producer stages.</summary>
    internal abstract PrimitiveTopology Topology { get; }

    /// <summary>Freezes the std140 material snapshot before a draw captures its logical uniform version.</summary>
    internal void CaptureMaterial(ReadOnlySpan<byte> data)
    {
        ShadowMaterialParameters.Validate(data);
        RequireInputMutation();
        parameters.SetValidatedMaterial(data);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes owned material storage through each generated stage binding contract.</summary>
    protected CpuUniformBuffer MaterialParameters => parameters;

    /// <summary>Retains material versions for the executable's existing RHI capture lifetime.</summary>
    protected ShadowNativeMaterialShader() { OwnUniformBuffer(parameters); }
    #endregion
}
