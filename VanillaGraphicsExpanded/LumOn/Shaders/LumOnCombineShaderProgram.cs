using VanillaGraphicsExpanded.Rendering.Contracts;
using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Combine/Integrate pass (SPG-009).
/// Combines indirect diffuse lighting with direct lighting and applies
/// proper material modulation (albedo, metallic rejection).
/// </summary>
[ShaderProgram("Contract", "lumon_combine", 16)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_combine.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_combine.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Lighting")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Composite")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Ao")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnableAO))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(LumOnEnabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnablePbrComposite))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnableShortRangeAo))]
public partial class LumOnCombineShaderProgram : LumOnShaderProgram, ILumOnCombineShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    protected override GpuProgramLayout CreateLayout() => new LumOnCombineProgramLayout();

    private LumOnCombineProgramLayout Layout => (LumOnCombineProgramLayout)ProgramLayout;

    /// <summary>Exposes retained parameters with an owner mutation guard.</summary>
    private LumOnCombineParamsUbo Params
    {
        get
        {
            var parameters = Layout.Params;
            parameters.SetWriteGuard(RequireInputMutation);
            return parameters;
        }
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnCombineShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Scene with direct lighting only (captured before GI application).
    /// </summary>
    public partial GpuTexture? SceneDirect { set; }

    /// <summary>
    /// LumOn indirect diffuse output (upsampled to full resolution).
    /// </summary>
    public partial GpuTexture? IndirectDiffuse { set; }

    /// <summary>
    /// G-Buffer albedo texture for material modulation.
    /// </summary>
    public partial int GBufferAlbedo { set; }

    /// <summary>
    /// G-Buffer material properties (roughness, metallic, etc.).
    /// </summary>
    public partial int GBufferMaterial { set; }

    /// <summary>
    /// G-Buffer world-space normals.
    /// </summary>
    public partial int GBufferNormal { set; }

    /// <summary>
    /// Primary depth texture for sky detection.
    /// </summary>
    public partial int PrimaryDepth { set; }

    #endregion

    // Per-frame state (matrices) is provided via LumOnFrameUBO.

    #region Composite options

    /// <summary>Gets or sets the declared PbrComposite shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.PbrComposite))]
    public partial bool EnablePbrComposite { get; set; }

    /// <summary>Gets or sets the declared AmbientOcclusion shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AmbientOcclusion))]
    public partial bool EnableAO { get; set; }

    /// <summary>Gets or sets the declared ShortRangeAo shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ShortRangeAo))]
    public partial bool EnableShortRangeAo { get; set; }

    [System.Obsolete("Renamed to EnableShortRangeAo.")]
    public bool EnableBentNormal { set => EnableShortRangeAo = value; }

    public float DiffuseAOStrength
    {
        set
        {
            Params.DiffuseAOStrength = value;
        }
    }

    public float SpecularAOStrength
    {
        set
        {
            Params.SpecularAOStrength = value;
        }
    }

    #endregion

    #region Intensity Uniforms

    /// <summary>
    /// Global intensity multiplier for indirect lighting.
    /// Default: 1.0
    /// </summary>
    public float IndirectIntensity
    {
        set
        {
            Params.IndirectIntensity = value;
        }
    }

    /// <summary>
    /// RGB tint applied to indirect lighting.
    /// </summary>
    public Vec3f IndirectTint
    {
        set
        {
            Params.IndirectTint = new System.Numerics.Vector3(value.X, value.Y, value.Z);
        }
    }

    #endregion

    #region Feature Toggle

    /// <summary>
    /// Whether LumOn is enabled.
    /// When disabled, passes through direct lighting unchanged.
    /// </summary>
    /// <summary>Gets or sets the declared Enabled shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.Enabled))]
    public partial bool LumOnEnabled { get; set; }

    #endregion
    #region Binding sources
    /// <summary>Supplies shared lighting storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnCombineShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies packed parameters for one publication per use.</summary>
    CpuUniformBuffer ILumOnCombineShaderProgramBindings.Parameters => Params;
    #endregion
}
