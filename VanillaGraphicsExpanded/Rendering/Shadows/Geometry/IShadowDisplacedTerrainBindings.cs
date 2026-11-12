using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Extends native terrain material bindings through the shared displacement stages.</summary>
internal interface IShadowDisplacedTerrainBindings : IShadowTerrainBindings
{
    // Native/final attributes retain 0..10. Control-to-evaluation attributes use
    // 11..18 so each stage's existing name-to-location contract stays unambiguous.
    #region Public API
    /// <summary>Freezes the native edge-level and height-fade policy.</summary>
    [ShaderBinding("VgeShadowSubdivision", ShaderBindingKind.UniformBlock, 1, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    CpuUniformBuffer SubdivisionParameters { get; }
    /// <summary>Supplies the existing authored displacement index texture.</summary>
    [ShaderBinding("vge_displacementTex", ShaderBindingKind.Sampler, 2, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef DisplacementIndices { set; }
    /// <summary>Supplies the existing authored displacement record texture.</summary>
    [ShaderBinding("vge_displacementRecords", ShaderBindingKind.Sampler, 4, ShaderStageKind.TessellationControl, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef DisplacementRecords { set; }
    /// <summary>Supplies initialized native height data with the shared nearest-clamp sampling rule.</summary>
    [ShaderBinding("vge_normalDepthTex", ShaderBindingKind.Sampler, 3, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef NormalDepth { set; }
    /// <summary>Preserves UVs entering control and leaving evaluation.</summary>
    [ShaderBinding("vge_shadowUv", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding TessellationUv { get; }
    /// <summary>Preserves UVs between tessellation stages.</summary>
    [ShaderBinding("tc_vge_shadowUv", ShaderBindingKind.VaryingLocation, 11, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlUv { get; }
    /// <summary>Preserves undeformed authored normals for coherent height extrusion.</summary>
    [ShaderBinding("vge_shadowNormal", ShaderBindingKind.VaryingLocation, 1, ShaderStageKind.Vertex, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding NormalLocation { get; }
    /// <summary>Transfers authored normals through control output.</summary>
    [ShaderBinding("tc_vge_shadowNormal", ShaderBindingKind.VaryingLocation, 12, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlNormal { get; }
    /// <summary>Preserves native material eligibility flags.</summary>
    [ShaderBinding("vge_shadowVertexFlags", ShaderBindingKind.VaryingLocation, 2, ShaderStageKind.Vertex, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding FlagsLocation { get; }
    /// <summary>Transfers native flags without interpolation.</summary>
    [ShaderBinding("tc_vge_shadowVertexFlags", ShaderBindingKind.VaryingLocation, 13, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlFlags { get; }
    /// <summary>Preserves the resolved atlas rectangle origin.</summary>
    [ShaderBinding("vge_uvBase", ShaderBindingKind.VaryingLocation, 3, ShaderStageKind.Vertex, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding UvBaseLocation { get; }
    /// <summary>Transfers atlas rectangle origins through control output.</summary>
    [ShaderBinding("tc_vge_uvBase", ShaderBindingKind.VaryingLocation, 14, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlUvBase { get; }
    /// <summary>Preserves the resolved atlas rectangle extent.</summary>
    [ShaderBinding("vge_uvExtent", ShaderBindingKind.VaryingLocation, 4, ShaderStageKind.Vertex, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding UvExtentLocation { get; }
    /// <summary>Transfers atlas rectangle extents through control output.</summary>
    [ShaderBinding("tc_vge_uvExtent", ShaderBindingKind.VaryingLocation, 15, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlUvExtent { get; }
    /// <summary>Transfers patch-wide authored height amplitude.</summary>
    [ShaderBinding("vge_patchAmplitude", ShaderBindingKind.VaryingLocation, 5, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding AmplitudeLocation { get; }
    /// <summary>Transfers patch-wide atlas bounds for pinned edges.</summary>
    [ShaderBinding("vge_patchRect", ShaderBindingKind.VaryingLocation, 6, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding RectangleLocation { get; }
    /// <summary>Preserves compact native object identity through tessellation.</summary>
    [ShaderBinding("vge_shadowObject", ShaderBindingKind.VaryingLocation, 8, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding TessellationObject { get; }
    /// <summary>Transfers compact native object identity without interpolation.</summary>
    [ShaderBinding("tc_vge_shadowObject", ShaderBindingKind.VaryingLocation, 16, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlObject { get; }
    /// <summary>Preserves compact view identity through tessellation.</summary>
    [ShaderBinding("vge_shadowView", ShaderBindingKind.VaryingLocation, 9, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding TessellationView { get; }
    /// <summary>Transfers compact view identity without interpolation.</summary>
    [ShaderBinding("tc_vge_shadowView", ShaderBindingKind.VaryingLocation, 17, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlView { get; }
    /// <summary>Retains camera-relative positions until final displacement is complete.</summary>
    [ShaderBinding("vge_shadowPosition", ShaderBindingKind.VaryingLocation, 10, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding TessellationPosition { get; }
    /// <summary>Transfers the native positions used by the shared edge and height functions.</summary>
    [ShaderBinding("tc_vge_shadowPosition", ShaderBindingKind.VaryingLocation, 18, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation)]
    ShaderVaryingLocationBinding ControlPosition { get; }
    #endregion
}
