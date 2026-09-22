using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Stage-owned structural options shared by offline compilation and runtime selection.</summary>
internal static partial class GpuShaderContracts
{
    #region Stage declarations
    /// <summary>Declares supported preprocessing choices explicitly, independent of compiler reflection.</summary>
    public static LegacyShaderStageContract CreateStage(string source)
    {
        var stage = new LegacyShaderStageContract();
        switch (source)
        {
            case "lumon_debug_direct.fsh":
            case "lumon_debug_gbuffer.fsh":
            case "lumon_debug_indirect.fsh":
            case "lumon_debug_probe_anchors.fsh":
            case "lumon_debug_probe_atlas.fsh":
            case "lumon_debug_sh.fsh":
            case "lumon_debug_temporal.fsh":
            case "lumon_debug_velocity.fsh":
            case "vge_worldprobe_orbs_points.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_DIRECT_LOCAL_VISIBILITY", "0");
                break;
            case "lumon_debug_composite.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_DIRECT_LOCAL_VISIBILITY", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLE_AO", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLE_SHORT_RANGE_AO", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_PBR_COMPOSITE", "1");
                break;
            case "lumon_debug.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_DIRECT_LOCAL_VISIBILITY", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLE_AO", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLE_SHORT_RANGE_AO", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_PBR_COMPOSITE", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_WORLDPROBE_ENABLED", "0");
                break;
            case "lumon_probe_atlas_trace.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_DIRECT_LOCAL_VISIBILITY", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_NEAR_FIELD_ENABLED", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_PROBE_PIS_ENABLED", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_WORLDPROBE_ENABLED", "0");
                break;
            case "lumon_debug_worldprobe.fsh":
            case "lumon_probe_atlas_gather.fsh":
            case "lumon_probe_sh9_gather.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_DIRECT_LOCAL_VISIBILITY", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_WORLDPROBE_ENABLED", "0");
                break;
            case "lumon_combine.fsh":
            case "tests/vge_global_defines_smoke.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLED", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLE_AO", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLE_SHORT_RANGE_AO", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_PBR_COMPOSITE", "1");
                break;
            case "pbr_composite.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLED", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_ENABLE_SHORT_RANGE_AO", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_PBR_COMPOSITE", "1");
                break;
            case "lumon_probe_atlas_temporal.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_PROBE_PIS_ENABLED", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING", "0");
                break;
            case "lumon_probe_atlas_pis_mask.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_PROBE_PIS_ENABLED", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_PROBE_PIS_FORCE_BATCH_SLICING", "0");
                stage.StructuralDefaults.Add("VGE_LUMON_PROBE_PIS_FORCE_UNIFORM_MASK", "0");
                break;
            case "lumon_upsample.fsh":
                stage.StructuralDefaults.Add("VGE_LUMON_UPSAMPLE_DENOISE", "1");
                stage.StructuralDefaults.Add("VGE_LUMON_UPSAMPLE_HOLEFILL", "1");
                break;
        }
        DeclareSpecializations(source, stage);
        return stage;
    }
    #endregion
}
