using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Declares the GPU binding contract for PBRDisplayResolveShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IPBRDisplayResolveShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the primaryScene Sampler slot.</summary>
    [ShaderBinding("primaryScene", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PrimaryScene { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int PrimaryDepth { set; }
    /// <summary>Selects a linear handoff rather than the legacy opaque display conversion.</summary>
    [ShaderBinding("sceneLinear", ShaderBindingKind.UniformLocation, 2, ShaderStageKind.Fragment)]
    int SceneLinear { set; }
    /// <summary>Supplies visible premultiplied particle radiance after opaque transport has completed.</summary>
    [ShaderBinding("particleLayer", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, Required = false, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D? ParticleLayer { set; }
    /// <summary>Requires an explicitly published particle layer before sampling optional storage.</summary>
    [ShaderBinding("particleLayerEnabled", ShaderBindingKind.UniformLocation, 3, ShaderStageKind.Fragment)]
    int ParticleLayerEnabled { set; }
    #endregion
}
