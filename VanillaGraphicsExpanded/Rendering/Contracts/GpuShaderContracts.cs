using System;
namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Creates the same resource declarations for runtime layouts and offline shader compilation.</summary>
internal static partial class GpuShaderContracts
{
    #region Construction
    /// <summary>Builds a program contract; shaders without runtime declarations retain their explicit source bindings.</summary>
    public static GpuBindingContract Create(string shader)
    {
        var contract = new GpuBindingContract();
        if (shader.StartsWith("lumon_debug_", StringComparison.Ordinal)) shader = "lumon_debug";
        if (shader.StartsWith("pbr_heightbake_", StringComparison.Ordinal)) shader = "pbr_heightbake";
        switch (shader)
        {
            case "lumon_combine": LumOnCombine(contract); break;
            case "lumon_debug": LumOnDebug(contract); break;
            case "lumon_probe_atlas_trace": LumOnTrace(contract); break;
            case "lumon_probe_atlas_gather": LumOnGather(contract); break;
            case "lumon_probe_sh9_gather": LumOnSh9Gather(contract); break;
            case "pbr_composite": PbrComposite(contract); break;
            case "pbr_direct_lighting": PbrDirectLighting(contract); break;
            case "pbr_heightbake": PbrHeightBake(contract); break;
            case "lumon_hzb_copy": LumOnHzbCopy(contract); break;
            case "lumon_hzb_downsample": LumOnHzbDownsample(contract); break;
            case "lumon_probe_anchor": LumOnProbeAnchor(contract); break;
            case "lumon_probe_atlas_pis_mask": LumOnProbeAtlasPisMask(contract); break;
            case "lumon_probe_atlas_filter": LumOnScreenProbeAtlasFilter(contract); break;
            case "lumon_probe_atlas_project_sh9": LumOnScreenProbeAtlasProjectSh9(contract); break;
            case "lumon_probe_atlas_project_sh": LumOnScreenProbeAtlasProjectSH(contract); break;
            case "lumon_probe_atlas_temporal": LumOnScreenProbeAtlasTemporal(contract); break;
            case "lumon_upsample": LumOnUpsample(contract); break;
            case "lumon_velocity": LumOnVelocity(contract); break;
            case "lumon_worldprobe_clipmap_resolve": LumOnWorldProbeClipmapResolve(contract); break;
            case "lumon_worldprobe_radiance_tile_resolve": LumOnWorldProbeRadianceTileResolve(contract); break;
            case "vge_debug_lines": VgeDebugLines(contract); break;
            case "vge_worldprobe_orbs_points": VgeWorldProbeOrbsPoints(contract); break;
            case "lumonscene_feedback_mark_pages": FeedbackMarkPages(contract); break;
            case "lumonscene_feedback_compact_pages": FeedbackCompactPages(contract); break;
            case "lumonscene_feedback_gather": FeedbackGather(contract); break;
            case "lumonscene_capture_voxel": CaptureVoxel(contract); break;
            case "lumonscene_capture_meshcard": CaptureMeshCard(contract); break;
            case "lumonscene_relight_voxel_dda": RelightVoxelDda(contract); break;
        }
        return contract;
    }
    #endregion
}
