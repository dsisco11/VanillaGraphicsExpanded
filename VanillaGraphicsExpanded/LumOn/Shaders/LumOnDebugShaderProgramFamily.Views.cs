using System;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Maps fullscreen view identity directly to its independently compiled program.</summary>
internal static partial class LumOnDebugShaderProgramFamily
{
    #region View lookup
    /// <summary>Returns the unique shader identity; non-fullscreen views have their own rendering paths.</summary>
    internal static string GetProgramName(LumOnDebugMode mode) => mode switch
    {
        LumOnDebugMode.ProbeGrid => "lumon_debug_view_probe_grid",
        LumOnDebugMode.ProbeDepth => "lumon_debug_view_probe_depth",
        LumOnDebugMode.ProbeNormal => "lumon_debug_view_probe_normal",
        LumOnDebugMode.SceneDepth => "lumon_debug_view_scene_depth",
        LumOnDebugMode.SceneNormal => "lumon_debug_view_scene_normal",
        LumOnDebugMode.TemporalWeight => "lumon_debug_view_temporal_weight",
        LumOnDebugMode.TemporalRejection => "lumon_debug_view_temporal_rejection",
        LumOnDebugMode.ShCoefficients => "lumon_debug_view_sh_coefficients",
        LumOnDebugMode.InterpolationWeights => "lumon_debug_view_interpolation_weights",
        LumOnDebugMode.RadianceOverlay => "lumon_debug_view_radiance_overlay",
        LumOnDebugMode.GatherWeight => "lumon_debug_view_gather_weight",
        LumOnDebugMode.ProbeAtlasMetaConfidence => "lumon_debug_view_probe_atlas_meta_confidence",
        LumOnDebugMode.ProbeAtlasTemporalAlpha => "lumon_debug_view_probe_atlas_temporal_alpha",
        LumOnDebugMode.ProbeAtlasMetaFlags => "lumon_debug_view_probe_atlas_meta_flags",
        LumOnDebugMode.ProbeAtlasFilteredRadiance => "lumon_debug_view_probe_atlas_filtered_radiance",
        LumOnDebugMode.ProbeAtlasFilterDelta => "lumon_debug_view_probe_atlas_filter_delta",
        LumOnDebugMode.ProbeAtlasGatherInputSource => "lumon_debug_view_probe_atlas_gather_input_source",
        LumOnDebugMode.CompositeAO => "lumon_debug_view_composite_ao",
        LumOnDebugMode.CompositeIndirectDiffuse => "lumon_debug_view_composite_indirect_diffuse",
        LumOnDebugMode.CompositeIndirectSpecular => "lumon_debug_view_composite_indirect_specular",
        LumOnDebugMode.CompositeMaterial => "lumon_debug_view_composite_material",
        LumOnDebugMode.DirectDiffuse => "lumon_debug_view_direct_diffuse",
        LumOnDebugMode.DirectSpecular => "lumon_debug_view_direct_specular",
        LumOnDebugMode.DirectEmissive => "lumon_debug_view_direct_emissive",
        LumOnDebugMode.DirectTotal => "lumon_debug_view_direct_total",
        LumOnDebugMode.VelocityMagnitude => "lumon_debug_view_velocity_magnitude",
        LumOnDebugMode.VelocityValidity => "lumon_debug_view_velocity_validity",
        LumOnDebugMode.VelocityPrevUv => "lumon_debug_view_velocity_prev_uv",
        LumOnDebugMode.MaterialBands => "lumon_debug_view_material_bands",
        LumOnDebugMode.WorldProbeIrradianceCombined => "lumon_debug_view_world_probe_irradiance_combined",
        LumOnDebugMode.WorldProbeIrradianceLevel => "lumon_debug_view_world_probe_irradiance_level",
        LumOnDebugMode.WorldProbeConfidence => "lumon_debug_view_world_probe_confidence",
        LumOnDebugMode.WorldProbeShortRangeAoDirection => "lumon_debug_view_world_probe_short_range_ao_direction",
        LumOnDebugMode.WorldProbeShortRangeAoConfidence => "lumon_debug_view_world_probe_short_range_ao_confidence",
        LumOnDebugMode.WorldProbeHitDistance => "lumon_debug_view_world_probe_hit_distance",
        LumOnDebugMode.WorldProbeMetaFlagsHeatmap => "lumon_debug_view_world_probe_meta_flags_heatmap",
        LumOnDebugMode.WorldProbeBlendWeights => "lumon_debug_view_world_probe_blend_weights",
        LumOnDebugMode.WorldProbeCrossLevelBlend => "lumon_debug_view_world_probe_cross_level_blend",
        LumOnDebugMode.PomMetrics => "lumon_debug_view_pom_metrics",
        LumOnDebugMode.WorldProbeRawConfidences => "lumon_debug_view_world_probe_raw_confidences",
        LumOnDebugMode.WorldProbeLightingEffect => "lumon_debug_view_world_probe_lighting_effect",
        LumOnDebugMode.WorldProbeSuppressedLighting => "lumon_debug_view_world_probe_suppressed_lighting",
        LumOnDebugMode.ProbeAtlasCurrentRadiance => "lumon_debug_view_probe_atlas_current_radiance",
        LumOnDebugMode.ProbeAtlasGatherInputRadiance => "lumon_debug_view_probe_atlas_gather_input_radiance",
        LumOnDebugMode.ProbeAtlasHitDistance => "lumon_debug_view_probe_atlas_hit_distance",
        LumOnDebugMode.ProbeAtlasTraceRadiance => "lumon_debug_view_probe_atlas_trace_radiance",
        LumOnDebugMode.ProbeAtlasTemporalRejection => "lumon_debug_view_probe_atlas_temporal_rejection",
        LumOnDebugMode.ProbeAtlasPisTraceMask => "lumon_debug_view_probe_atlas_pis_trace_mask",
        LumOnDebugMode.ProbePisEnergy => "lumon_debug_view_probe_pis_energy",
        LumOnDebugMode.LumonScenePageReady => "lumon_debug_view_lumon_scene_page_ready",
        LumOnDebugMode.LumonScenePatchUv => "lumon_debug_view_lumon_scene_patch_uv",
        LumOnDebugMode.LumonSceneIrradiance => "lumon_debug_view_lumon_scene_irradiance",
        LumOnDebugMode.TraceSceneBoundsL0 => "lumon_debug_view_trace_scene_bounds_l0",
        LumOnDebugMode.TraceSceneOccupancyL0 => "lumon_debug_view_trace_scene_occupancy_l0",
        LumOnDebugMode.TraceScenePayloadL0 => "lumon_debug_view_trace_scene_payload_l0",
        LumOnDebugMode.LumOnScenesOverview => "lumon_debug_view_lum_on_scenes_overview",
        LumOnDebugMode.LumonSceneChunkSlot => "lumon_debug_view_lumon_scene_chunk_slot",
        LumOnDebugMode.LumonSceneSlotGeneration => "lumon_debug_view_lumon_scene_slot_generation",
        LumOnDebugMode.LumonScenePageTableOccupancy => "lumon_debug_view_lumon_scene_page_table_occupancy",
        LumOnDebugMode.LumonSceneMaterial => "lumon_debug_view_lumon_scene_material",
        LumOnDebugMode.LumonSceneMaterialAtlasAll => "lumon_debug_view_lumon_scene_material_atlas_all",
        LumOnDebugMode.LumonSceneMaterialRoughness => "lumon_debug_view_lumon_scene_material_roughness",
        LumOnDebugMode.LumonSceneMaterialAtlasAllRoughness => "lumon_debug_view_lumon_scene_material_atlas_all_roughness",
        LumOnDebugMode.LumonSceneMaterialAtlasAllNormals => "lumon_debug_view_lumon_scene_material_atlas_all_normals",
        LumOnDebugMode.TraceSceneDdaDistanceL0 => "lumon_debug_view_trace_scene_dda_distance_l0",
        LumOnDebugMode.WorldProbeImportance => "lumon_debug_view_world_probe_importance",
        LumOnDebugMode.ProbeAtlasTraceOutcome => "lumon_debug_view_probe_atlas_trace_outcome",
        LumOnDebugMode.NearFieldGeometry => "lumon_debug_view_near_field_geometry",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "This view has no fullscreen shader.")
    };

    /// <summary>Looks up the selected declaration without preparing unrelated view executables.</summary>
    internal static bool TryGet(LumOnDebugMode mode, out LumOnDebugShaderProgram program)
    {
        // Stale configuration values must not turn an optional overlay into a render-loop exception.
        if (!Enum.IsDefined(mode) || mode is LumOnDebugMode.Off or LumOnDebugMode.VgeNormalDepthAtlas or LumOnDebugMode.WorldProbeOrbsPoints)
        {
            program = null!;
            return false;
        }
        return TryGet(GetProgramName(mode), out program);
    }
    #endregion
}
