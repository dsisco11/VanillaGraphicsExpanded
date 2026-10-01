using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for projecting the screen-probe atlas into packed SH L1 coefficients.
/// Used for Phase 12 Option B (cheap gather).
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_project_sh", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_project_sh.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_project_sh.fsh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
public partial class LumOnScreenProbeAtlasProjectSHShaderProgram : LumOnShaderProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer LumOnFrame { set; }
    /// <summary>Declares the octahedralAtlas Sampler slot.</summary>
    [ShaderBinding("octahedralAtlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture OctahedralAtlas { set; }
    /// <summary>Declares the probeAtlasMeta Sampler slot.</summary>
    [ShaderBinding("probeAtlasMeta", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture ProbeAtlasMeta { set; }
    /// <summary>Declares the probeAnchorPosition Sampler slot.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture ProbeAnchorPositionTexture { set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    public LumOnScreenProbeAtlasProjectSHShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasProjectSHShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Input stabilized screen-probe atlas radiance.
    /// Shader uniform name remains <c>octahedralAtlas</c> for compatibility.
    /// </summary>
    public GpuTexture? ScreenProbeAtlas { set => BindTexture2D("octahedralAtlas", value, 0); }

    /// <summary>
    /// Input stabilized probe-atlas meta (confidence + flags).
    /// </summary>
    public GpuTexture? ScreenProbeAtlasMeta { set => BindTexture2D("probeAtlasMeta", value, 1); }

    /// <summary>
    /// Probe anchor positions for validity checks.
    /// </summary>
    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    #endregion

    // Per-frame state (viewMatrix, probeGridSize) is provided via LumOnFrameUBO.
}
