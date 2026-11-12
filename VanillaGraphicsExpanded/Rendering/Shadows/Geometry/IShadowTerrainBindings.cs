using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns the material and virtual-writing resource indices for installed native terrain variants.</summary>
internal interface IShadowTerrainBindings
{
    #region Public API
    /// <summary>Preserves native atlas coordinates across the material stages.</summary>
    [ShaderBinding("vge_shadowUv", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    ShaderVaryingLocationBinding UvLocation { get; }
    /// <summary>Retains native warp, origin and atlas-padding values for one draw.</summary>
    [ShaderBinding("VgeShadowMaterial", ShaderBindingKind.UniformBlock, 0, ShaderStageKind.Vertex, ShaderStageKind.TessellationEvaluation)]
    CpuUniformBuffer Parameters { get; }
    /// <summary>Supplies the separate projection records without vertex-stage storage blocks.</summary>
    [ShaderBinding("shadowViews", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.TextureBuffer)]
    GpuTextureRef Views { set; }
    /// <summary>Supplies the native pass/atlas texture for unchanged cutout rejection.</summary>
    [ShaderBinding("vge_shadowAtlas", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
    GpuTextureRef Atlas { set; }
    /// <summary>Borrows packed native face data only for the engine's shader-storage layout.</summary>
    [ShaderBinding("VgeShadowFaces", ShaderBindingKind.StorageBlock, 3, ShaderStageKind.Vertex, Required = false)]
    GpuStorageBufferBinding Faces { set; }
    /// <summary>Supplies exact virtual-page ownership and projection identities.</summary>
    [ShaderBinding("ShadowWriteEntries", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Entries { set; }
    /// <summary>Supplies only cleared frozen replacement assignments.</summary>
    [ShaderBinding("ShadowWritableAssignments", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Fragment)]
    GpuShaderStorageBuffer? Writable { set; }
    /// <summary>Supplies the complete layered integer destination.</summary>
    [ShaderBinding("shadowDepthPool", ShaderBindingKind.Image, 0, ShaderStageKind.Fragment, ShaderType = ShaderResourceType.UnsignedIntImage2DArray)]
    RHIImageBinding Depth { set; }
    /// <summary>Preserves the original object identity independently of view selection.</summary>
    [ShaderBinding("vge_shadowObject", ShaderBindingKind.VaryingLocation, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    ShaderVaryingLocationBinding ObjectLocation { get; }
    /// <summary>Connects the compact view identity to exact fragment authorization.</summary>
    [ShaderBinding("vge_shadowView", ShaderBindingKind.VaryingLocation, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    ShaderVaryingLocationBinding ViewLocation { get; }
    /// <summary>Retains the final native deformation output before virtual projection.</summary>
    [ShaderBinding("vge_shadowPosition", ShaderBindingKind.VaryingLocation, 10, ShaderStageKind.Vertex)]
    ShaderVaryingLocationBinding PositionLocation { get; }
    #endregion
}
