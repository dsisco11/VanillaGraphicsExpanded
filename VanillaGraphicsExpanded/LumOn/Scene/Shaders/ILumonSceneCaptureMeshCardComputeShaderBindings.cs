using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the GPU binding contract for LumonSceneCaptureMeshCardComputeShader.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface ILumonSceneCaptureMeshCardComputeShaderBindings
{
    #region Public API
    /// <summary>Declares the VgeLumOnSceneCaptureMeshCardParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneCaptureMeshCardParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Declares the vge_depthAtlas Image slot.</summary>
    [ShaderBinding("vge_depthAtlas", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding DepthAtlas { set; }
    /// <summary>Declares the vge_materialAtlas Image slot.</summary>
    [ShaderBinding("vge_materialAtlas", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    GpuTextureBinding MaterialAtlas { set; }
    /// <summary>Declares the VgeMeshCardCaptureWork StorageBlock slot.</summary>
    [ShaderBinding("VgeMeshCardCaptureWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer MeshCardCaptureWork { set; }
    /// <summary>Declares the VgePatchMetadataBuffer StorageBlock slot.</summary>
    [ShaderBinding("VgePatchMetadataBuffer", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer PatchMetadata { set; }
    /// <summary>Declares the VgeTriangles StorageBlock slot.</summary>
    [ShaderBinding("VgeTriangles", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer Triangles { set; }
    #endregion
}
