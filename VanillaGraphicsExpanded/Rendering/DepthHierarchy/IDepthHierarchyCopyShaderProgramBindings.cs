using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Declares the GPU binding contract for DepthHierarchyCopyShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IDepthHierarchyCopyShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? PrimaryDepth { set; }
    #endregion
}
