using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Declares the owned texture display interface independently of engine blit shaders.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IDebugTextureBindings
{
    /// <summary>Binds the selected debug texture to the stable fragment sampler slot.</summary>
    [ShaderBinding("scene", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment,
        TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int Scene { set; }
}
