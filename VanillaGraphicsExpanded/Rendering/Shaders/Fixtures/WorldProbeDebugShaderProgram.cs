using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/worldprobe_debug", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_debug.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_debug_view_world_probe_irradiance_combined.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "World")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "WorldGather")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeBaseSpacing), SpecializationId = 11, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeDiffuseStride), SpecializationId = 15, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeLevels), SpecializationId = 12, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbes))]
internal static partial class WorldProbeDebugShaderProgram
{
    #region Shader options
    /// <summary>Exposes the shared DirectVisibility shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.DirectVisibility))]
    internal static partial ShaderOption<bool> DirectVisibility { get; }

    /// <summary>Exposes the shared WorldProbeBaseSpacing shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeBaseSpacing))]
    internal static partial ShaderOption<float> WorldProbeBaseSpacing { get; }

    /// <summary>Exposes the shared WorldProbeDiffuseStride shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeDiffuseStride))]
    internal static partial ShaderOption<int> WorldProbeDiffuseStride { get; }

    /// <summary>Exposes the shared WorldProbeLevels shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeLevels))]
    internal static partial ShaderOption<int> WorldProbeLevels { get; }

    /// <summary>Exposes the shared WorldProbeOctahedralSize shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeOctahedralSize))]
    internal static partial ShaderOption<int> WorldProbeOctahedralSize { get; }

    /// <summary>Exposes the shared WorldProbeResolution shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeResolution))]
    internal static partial ShaderOption<int> WorldProbeResolution { get; }

    /// <summary>Exposes the shared WorldProbes shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbes))]
    internal static partial ShaderOption<bool> WorldProbes { get; }
    #endregion

}
