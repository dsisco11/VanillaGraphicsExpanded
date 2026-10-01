using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Retains a coherent borrowed surface-lighting generation for trace submission.</summary>
public partial class LumOnScreenProbeAtlasTraceShaderProgram
{
    private readonly SurfaceLightingParamsUbo surfaceLightingParameters = new();
    private SurfaceLightingSnapshot? surfaceLighting;

    #region Public API
    /// <summary>Stages the domain and resources together; their owner retains disposal rights.</summary>
    internal void SetSurfaceLighting(SurfaceLightingSnapshot? snapshot)
    {
        RequireInputMutation();
        surfaceLightingParameters.SetWriteGuard(RequireInputMutation);
        surfaceLightingParameters.Set(snapshot);
        surfaceLighting = snapshot;
    }

    /// <summary>Supplies the retained sampling domain.</summary>
    CpuUniformBuffer ILumOnScreenProbeAtlasTraceShaderProgramBindings.SurfaceLightingParameters => surfaceLightingParameters;
    /// <summary>Supplies captured material from the retained generation.</summary>
    GpuTexture? ILumOnScreenProbeAtlasTraceShaderProgramBindings.CapturedMaterial => surfaceLighting?.Material;
    /// <summary>Supplies radiance from the retained generation.</summary>
    GpuTexture? ILumOnScreenProbeAtlasTraceShaderProgramBindings.PreviousOutgoing => surfaceLighting?.OutgoingRadiance;
    /// <summary>Supplies page addresses from the retained generation.</summary>
    GpuTexture? ILumOnScreenProbeAtlasTraceShaderProgramBindings.SurfacePages => surfaceLighting?.PageTable;
    /// <summary>Supplies patch metadata from the retained generation.</summary>
    GpuShaderStorageBuffer? ILumOnScreenProbeAtlasTraceShaderProgramBindings.SurfacePatches => surfaceLighting?.Patches;
    /// <summary>Supplies slot ownership from the retained generation.</summary>
    GpuShaderStorageBuffer? ILumOnScreenProbeAtlasTraceShaderProgramBindings.SurfaceSlots => surfaceLighting?.Slots;
    /// <summary>Supplies readiness from the retained generation.</summary>
    GpuShaderStorageBuffer? ILumOnScreenProbeAtlasTraceShaderProgramBindings.SurfaceReady => surfaceLighting?.Readiness;
    #endregion
}
