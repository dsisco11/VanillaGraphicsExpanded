using System.Numerics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares typed ordinary values and a required output for transactional publication proofs.</summary>
internal interface IUniformStateComputeBindings
{
    #region Public API
    /// <summary>Retains an ordinary scalar, including its default upload.</summary>
    [ShaderBinding("scalar", ShaderBindingKind.UniformLocation, 120, ShaderStageKind.Compute)]
    float Scalar { get; set; }
    /// <summary>Retains an owned mutable scalar array.</summary>
    [ShaderBinding("values", ShaderBindingKind.UniformLocation, 121, ShaderStageKind.Compute)]
    float[] Values { get; set; }
    /// <summary>Retains an exact vector upload.</summary>
    [ShaderBinding("vector", ShaderBindingKind.UniformLocation, 123, ShaderStageKind.Compute)]
    Vector3 Vector { get; set; }
    /// <summary>Retains an exact matrix upload.</summary>
    [ShaderBinding("transform", ShaderBindingKind.UniformLocation, 124, ShaderStageKind.Compute)]
    Matrix4x4 Transform { get; set; }
    /// <summary>Supplies the required floating image output.</summary>
    [ShaderBinding("result", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, ShaderType = ShaderResourceType.Image2D)]
    GpuTextureBinding Output { get; set; }
    #endregion
}
