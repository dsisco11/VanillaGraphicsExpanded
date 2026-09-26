using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Preloads screen-lighting programs whose inputs are already known by the owning renderer.</summary>
public partial class LumOnRenderer
{
    #region Shader preparation
    /// <summary>Batches invariant passes; resource-dependent variants are prepared after their view settings are known.</summary>
    private void PreloadInvariantPrograms()
    {
        if (!config.LumOn.Enabled) return;
        var programs = ImmutableArray.CreateBuilder<GpuProgram>();
        foreach (string name in new[] { "lumon_velocity", "lumon_probe_anchor", "lumon_hzb_copy",
            "lumon_hzb_downsample", "lumon_probe_atlas_project_sh9", "lumon_probe_atlas_filter", "lumon_upsample" })
        {
            var program = GpuShaderPrograms.Get<GpuProgram>(capi, name);
            if (program != null) programs.Add(program);
        }
        if (!GpuShaderPrograms.Preload(capi, programs.ToImmutable()))
            capi.Logger.Warning("[LumOn] Required shader preload failed; rendering will retain readiness checks.");
    }
    #endregion
}
