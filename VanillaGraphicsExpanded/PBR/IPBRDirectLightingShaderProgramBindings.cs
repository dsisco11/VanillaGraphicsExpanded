using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Declares the GPU binding contract for PBRDirectLightingShaderProgram.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IPBRDirectLightingShaderProgramBindings
{
    #region Public API
    /// <summary>Declares the VgePbrDirectLightingParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrDirectLightingParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the primaryScene Sampler slot.</summary>
    [ShaderBinding("primaryScene", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int PrimaryScene { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int PrimaryDepth { set; }
    /// <summary>Declares the gBufferPosition Sampler slot.</summary>
    [ShaderBinding("gBufferPosition", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int GBufferPosition { set; }
    /// <summary>Declares the gBufferEnvironment Sampler slot.</summary>
    [ShaderBinding("gBufferEnvironment", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int GBufferEnvironment { set; }
    /// <summary>Declares the gBufferNormal Sampler slot.</summary>
    [ShaderBinding("gBufferNormal", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int GBufferNormal { set; }
    /// <summary>Declares the gBufferMaterial Sampler slot.</summary>
    [ShaderBinding("gBufferMaterial", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int GBufferMaterial { set; }
    /// <summary>Declares the shadowMapNear Sampler slot.</summary>
    [ShaderBinding("shadowMapNear", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int ShadowMapNear { set; }
    /// <summary>Declares the shadowMapFar Sampler slot.</summary>
    [ShaderBinding("shadowMapFar", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    int ShadowMapFar { set; }
    #endregion
}
