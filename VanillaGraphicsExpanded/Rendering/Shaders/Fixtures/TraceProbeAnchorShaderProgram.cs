using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/trace_probe_anchor", 32)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_anchor.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_trace.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Tracing")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Pis")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "World")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(AtlasTexelsPerFrame), SpecializationId = 1)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(BatchSlicing))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EmissiveBoost), SpecializationId = 0)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(HzbCoarseMip), SpecializationId = 2)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(ImportanceSampling))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(NearField))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(RayMaxDistance), SpecializationId = 3)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(RaySteps), SpecializationId = 4)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(RayThickness), SpecializationId = 5)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(SkyMissWeight), SpecializationId = 6, When = "!NearField")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeBaseSpacing), SpecializationId = 11, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeLevels), SpecializationId = 12, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbes))]
internal static partial class TraceProbeAnchorShaderProgram
{
    #region Shader options
    /// <summary>Exposes the shared AtlasTexelsPerFrame shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AtlasTexelsPerFrame))]
    internal static partial ShaderOption<int> AtlasTexelsPerFrame { get; }

    /// <summary>Exposes the shared BatchSlicing shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.BatchSlicing))]
    internal static partial ShaderOption<bool> BatchSlicing { get; }

    /// <summary>Exposes the shared DirectVisibility shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.DirectVisibility))]
    internal static partial ShaderOption<bool> DirectVisibility { get; }

    /// <summary>Exposes the shared EmissiveBoost shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.EmissiveBoost))]
    internal static partial ShaderOption<float> EmissiveBoost { get; }

    /// <summary>Exposes the shared HzbCoarseMip shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.HzbCoarseMip))]
    internal static partial ShaderOption<int> HzbCoarseMip { get; }

    /// <summary>Exposes the shared ImportanceSampling shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ImportanceSampling))]
    internal static partial ShaderOption<bool> ImportanceSampling { get; }

    /// <summary>Exposes the shared NearField shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.NearField))]
    internal static partial ShaderOption<bool> NearField { get; }

    /// <summary>Exposes the shared RayMaxDistance shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RayMaxDistance))]
    internal static partial ShaderOption<float> RayMaxDistance { get; }

    /// <summary>Exposes the shared RaySteps shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RaySteps))]
    internal static partial ShaderOption<int> RaySteps { get; }

    /// <summary>Exposes the shared RayThickness shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RayThickness))]
    internal static partial ShaderOption<float> RayThickness { get; }

    /// <summary>Exposes the shared SkyMissWeight shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.SkyMissWeight))]
    internal static partial ShaderOption<float> SkyMissWeight { get; }

    /// <summary>Exposes the shared WorldProbeBaseSpacing shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeBaseSpacing))]
    internal static partial ShaderOption<float> WorldProbeBaseSpacing { get; }

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
