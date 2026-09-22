using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Numeric specialization declarations and the structural branches that use them.</summary>
internal static partial class GpuShaderContracts
{
    #region Specialization declarations
    /// <summary>Declares stable IDs, defaults and conditional availability for each owning stage.</summary>
    private static void DeclareSpecializations(string source, ShaderStageContract stage)
    {
        if (source == "lumon_probe_atlas_trace.fsh")
        {
            stage.Specializations.Add(new(0, "LUMON_EMISSIVE_BOOST", "float", "1.0"));
            stage.Specializations.Add(new(1, "VGE_LUMON_ATLAS_TEXELS_PER_FRAME", "int", "16"));
            stage.Specializations.Add(new(2, "VGE_LUMON_HZB_COARSE_MIP", "int", "4"));
            stage.Specializations.Add(new(3, "VGE_LUMON_RAY_MAX_DISTANCE", "float", "4.0"));
            stage.Specializations.Add(new(4, "VGE_LUMON_RAY_STEPS", "int", "10"));
            stage.Specializations.Add(new(5, "VGE_LUMON_RAY_THICKNESS", "float", "0.5"));
            stage.Specializations.Add(new(6, "VGE_LUMON_SKY_MISS_WEIGHT", "float", "0.5",
                values => ShaderStageContract.Value("VGE_LUMON_NEAR_FIELD_ENABLED", "0", values) == "0"));
        }
        if (source is "lumon_probe_atlas_temporal.fsh" or "lumon_probe_atlas_pis_mask.fsh")
            stage.Specializations.Add(new(1, "VGE_LUMON_ATLAS_TEXELS_PER_FRAME", "int", "16"));
        if (source == "lumon_probe_atlas_pis_mask.fsh")
        {
            stage.Specializations.Add(new(7, "VGE_LUMON_PROBE_PIS_MIN_CONFIDENCE_WEIGHT", "float", "0.1"));
            stage.Specializations.Add(new(8, "VGE_LUMON_PROBE_PIS_EXPLORE_COUNT", "int", "-1", UsesImportanceSampling));
            stage.Specializations.Add(new(9, "VGE_LUMON_PROBE_PIS_EXPLORE_FRACTION", "float", "0.25", UsesImportanceSampling));
            stage.Specializations.Add(new(10, "VGE_LUMON_PROBE_PIS_WEIGHT_EPSILON", "float", "1e-6", UsesImportanceSampling));
        }
        if (source is "lumon_probe_atlas_trace.fsh" or "lumon_probe_atlas_gather.fsh" or
            "lumon_probe_sh9_gather.fsh" or "lumon_debug.fsh" or "lumon_debug_worldprobe.fsh")
        {
            stage.Specializations.Add(new(11, "VGE_LUMON_WORLDPROBE_BASE_SPACING", "float", "0.0", UsesWorldProbes));
            stage.Specializations.Add(new(12, "VGE_LUMON_WORLDPROBE_LEVELS", "int", "0", UsesWorldProbes));
            stage.Specializations.Add(new(13, "VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE", "int", "16", UsesWorldProbes));
            stage.Specializations.Add(new(14, "VGE_LUMON_WORLDPROBE_RESOLUTION", "int", "0", UsesWorldProbes));
            if (source != "lumon_probe_atlas_trace.fsh")
                stage.Specializations.Add(new(15, "VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE", "int", "2", UsesWorldProbes));
        }
        if (source == "vge_worldprobe_orbs_points.fsh")
        {
            stage.Specializations.Add(new(13, "VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE", "int", "16"));
            stage.Specializations.Add(new(14, "VGE_LUMON_WORLDPROBE_RESOLUTION", "int", "0"));
        }
    }

    /// <summary>World-probe constants belong only to variants containing world-probe sampling.</summary>
    private static bool UsesWorldProbes(IReadOnlyDictionary<string, string?>? values) =>
        ShaderStageContract.Value("VGE_LUMON_WORLDPROBE_ENABLED", "0", values) == "1";

    /// <summary>Exploration constants are used only by importance sampling without either uniform-mask override.</summary>
    private static bool UsesImportanceSampling(IReadOnlyDictionary<string, string?>? values) =>
        ShaderStageContract.Value("VGE_LUMON_PROBE_PIS_ENABLED", "0", values) == "1" &&
        ShaderStageContract.Value("VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING", "0", values) == "0" &&
        ShaderStageContract.Value("VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK", "0", values) == "0";
    #endregion
}
