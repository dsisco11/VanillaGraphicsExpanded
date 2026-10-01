using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Declares the GPU binding contract for PBRDisplayResolveShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(IPBRCompositeShaderProgramBindings), Stages = new[] { ShaderStageKind.Vertex })]
internal interface IPBRDisplayResolveShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the primaryScene Sampler slot.</summary>
    [ShaderBinding("primaryScene", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment)]
    int PrimaryScene { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
    int PrimaryDepth { set; }
    #endregion
}
