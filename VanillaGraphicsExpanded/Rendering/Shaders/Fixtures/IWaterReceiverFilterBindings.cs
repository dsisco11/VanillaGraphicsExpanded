using System.Numerics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Supplies explicit geometry and paired immutable receiver images to the shared filter fixture.</summary>
internal interface IWaterReceiverFilterBindings
{
    #region Public API
    /// <summary>Publishes the complete retained optical input block.</summary>
    [ShaderBinding("WaterReceiverFilterInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies associated linear radiance and receiver eligibility.</summary>
    [ShaderBinding("vge_refractionColor", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Color { get; set; }
    /// <summary>Supplies hardware depth and, at reduced size, original-source coordinates.</summary>
    [ShaderBinding("vge_refractionDepth", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Depth { get; set; }
    /// <summary>Locates the requested normalized background sample.</summary>
    Vector2 SampleUv { get; set; }

    /// <summary>Supplies the local interface point in view coordinates.</summary>
    Vector3 Surface { get; set; }

    /// <summary>Supplies the oriented local interface normal.</summary>
    Vector3 Normal { get; set; }

    /// <summary>Reconstructs receiver positions independently of background dimensions.</summary>
    Matrix4x4 InverseProjection { get; set; }

    /// <summary>Distinguishes original framebuffer dimensions from reduced background dimensions.</summary>
    Vector2 FullFrameSize { get; set; }
    #endregion
}
