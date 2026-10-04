using System.Numerics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Supplies optical geometry and coherent receiver images to the UV receiver fixture.</summary>
internal interface IWaterUvRefractionBindings
{
    #region Public API
    /// <summary>Supplies the view-space interface point.</summary>
    [ShaderBinding("surfaceVS", ShaderBindingKind.UniformLocation, 120, ShaderStageKind.Fragment)]
    Vector3 Surface { get; set; }
    /// <summary>Supplies the oriented view-space interface normal.</summary>
    [ShaderBinding("normalVS", ShaderBindingKind.UniformLocation, 121, ShaderStageKind.Fragment)]
    Vector3 Normal { get; set; }
    /// <summary>Projects optical geometry using the complete camera projection.</summary>
    [ShaderBinding("projectionMatrix", ShaderBindingKind.UniformLocation, 122, ShaderStageKind.Fragment)]
    Matrix4x4 Projection { get; set; }
    /// <summary>Supplies original view dimensions independently of background resolution.</summary>
    [ShaderBinding("frameSize", ShaderBindingKind.UniformLocation, 126, ShaderStageKind.Fragment)]
    Vector2 FrameSize { get; set; }
    /// <summary>Selects the submerged-camera exit interface.</summary>
    [ShaderBinding("underwater", ShaderBindingKind.UniformLocation, 127, ShaderStageKind.Fragment)]
    int Underwater { get; set; }
    /// <summary>Reconstructs receivers with the CPU inverse of the supplied camera projection.</summary>
    [ShaderBinding("inverseProjectionMatrix", ShaderBindingKind.UniformLocation, 128, ShaderStageKind.Fragment)]
    Matrix4x4 InverseProjection { get; set; }
    /// <summary>Supplies linear radiance and eligibility metadata.</summary>
    [ShaderBinding("vge_refractionColor", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Color { get; set; }
    /// <summary>Supplies coherent hardware depth and optional source coordinates.</summary>
    [ShaderBinding("vge_refractionDepth", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Depth { get; set; }
    #endregion
}
