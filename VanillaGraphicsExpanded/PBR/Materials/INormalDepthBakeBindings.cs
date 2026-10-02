using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Declares the atlas sampled by the normal/depth bake executable.</summary>
internal interface INormalDepthBakeBindings
{
    #region Public API
    /// <summary>Declares the albedo atlas's existing texture unit.</summary>
    [ShaderBinding("baseAlbedoAtlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, ShaderType = ShaderResourceType.Sampler2D)]
    ShaderSamplerBinding Atlas { get; }
    #endregion
}
