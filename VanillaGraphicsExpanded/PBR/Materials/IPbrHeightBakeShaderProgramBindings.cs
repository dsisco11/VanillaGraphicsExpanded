using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Declares the GPU binding contract for PbrHeightBakeShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IPbrHeightBakeShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgePbrHeightBakeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrHeightBakeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    CpuUniformBuffer Parameters { set; }
    /// <summary>Retains the g input for its consuming passes.</summary>
    [ShaderBinding("u_g", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "DivergenceContract" })]
    GpuTextureRef g { set; }
    /// <summary>Retains the g2 input for its consuming passes.</summary>
    [ShaderBinding("u_g2", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, Programs = new[] { "CombineContract" })]
    GpuTextureRef g2 { set; }
    /// <summary>Retains the a input for its consuming passes.</summary>
    [ShaderBinding("u_a", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "SubContract" })]
    GpuTextureRef a { set; }
    /// <summary>Retains the d input for its consuming passes.</summary>
    [ShaderBinding("u_d", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "GradientContract" })]
    GpuTextureRef d { set; }
    /// <summary>Retains the g4 input for its consuming passes.</summary>
    [ShaderBinding("u_g4", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment, Programs = new[] { "CombineContract" })]
    GpuTextureRef g4 { set; }
    /// <summary>Retains the b input for its consuming passes.</summary>
    [ShaderBinding("u_b", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, Programs = new[] { "SubContract", "JacobiContract", "ResidualContract" })]
    GpuTextureRef b { set; }
    /// <summary>Retains the h input for its consuming passes.</summary>
    [ShaderBinding("u_h", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "NormalizeContract", "JacobiContract", "ResidualContract" })]
    GpuTextureRef h { set; }
    /// <summary>Retains the g3 input for its consuming passes.</summary>
    [ShaderBinding("u_g3", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, Programs = new[] { "CombineContract" })]
    GpuTextureRef g3 { set; }
    /// <summary>Retains the albedoAtlas input for its consuming passes.</summary>
    [ShaderBinding("u_albedoAtlas", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, Programs = new[] { "PackToAtlasContract" }, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef albedoAtlas { set; }
    /// <summary>Retains the fineH input for its consuming passes.</summary>
    [ShaderBinding("u_fineH", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "ProlongateAddContract" })]
    GpuTextureRef fineH { set; }
    /// <summary>Retains the atlas input for its consuming passes.</summary>
    [ShaderBinding("u_atlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "LuminanceContract" }, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef atlas { set; }
    /// <summary>Retains the g1 input for its consuming passes.</summary>
    [ShaderBinding("u_g1", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "CombineContract" })]
    GpuTextureRef g1 { set; }
    /// <summary>Retains the coarseE input for its consuming passes.</summary>
    [ShaderBinding("u_coarseE", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, Programs = new[] { "ProlongateAddContract" })]
    GpuTextureRef coarseE { set; }
    /// <summary>Retains the fine input for its consuming passes.</summary>
    [ShaderBinding("u_fine", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "RestrictContract" })]
    GpuTextureRef fine { set; }
    /// <summary>Retains the height input for its consuming passes.</summary>
    [ShaderBinding("u_height", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "PackToAtlasContract" })]
    GpuTextureRef height { set; }
    /// <summary>Retains the src input for its consuming passes.</summary>
    [ShaderBinding("u_src", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, Programs = new[] { "CopyContract", "Gauss1dContract" })]
    GpuTextureRef src { set; }
    #endregion
}
