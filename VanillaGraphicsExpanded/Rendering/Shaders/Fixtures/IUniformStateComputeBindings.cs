using System.Numerics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares typed ordinary values and a required output for transactional publication proofs.</summary>
internal interface IUniformStateComputeBindings
{
    #region Public API
    /// <summary>Publishes all numeric inputs as one immutable packed version.</summary>
    [ShaderBinding("UniformStateInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Retains an ordinary scalar, including its default upload.</summary>
    float Scalar { get; set; }
    /// <summary>Retains an owned mutable scalar array.</summary>
    float[] Values { get; set; }
    /// <summary>Retains an exact vector upload.</summary>
    Vector3 Vector { get; set; }
    /// <summary>Retains an exact matrix upload.</summary>
    Matrix4x4 Transform { get; set; }
    /// <summary>Supplies the required floating image output.</summary>
    [ShaderBinding("result", ShaderBindingKind.Image, 0, ShaderStageKind.Compute, ShaderType = ShaderResourceType.Image2D)]
    GpuTextureBinding Output { get; set; }
    #endregion
}
