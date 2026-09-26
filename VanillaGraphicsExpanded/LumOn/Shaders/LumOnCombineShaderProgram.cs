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
public partial class LumOnCombineShaderProgram : LumOnShaderProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    protected override GpuProgramLayout CreateLayout() => new LumOnCombineProgramLayout();

    private LumOnCombineProgramLayout Layout => (LumOnCombineProgramLayout)ProgramLayout;

    private LumOnCombineParamsUbo Params => Layout.Params;

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
    public GpuTexture? SceneDirect { set => Layout.BindSceneDirect(ProgramId, value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// LumOn indirect diffuse output (upsampled to full resolution).
    /// </summary>
    public GpuTexture? IndirectDiffuse { set => Layout.BindIndirectDiffuse(ProgramId, value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// G-Buffer albedo texture for material modulation.
    /// </summary>
    public int GBufferAlbedo { set => Layout.BindGBufferAlbedo(ProgramId, value, LayoutWarn); }

    /// <summary>
    /// G-Buffer material properties (roughness, metallic, etc.).
    /// </summary>
    public int GBufferMaterial { set => Layout.BindGBufferMaterial(ProgramId, value, LayoutWarn); }

    /// <summary>
    /// G-Buffer world-space normals.
    /// </summary>
    public int GBufferNormal { set => Layout.BindGBufferNormal(ProgramId, value, LayoutWarn); }

    /// <summary>
    /// Primary depth texture for sky detection.
    /// </summary>
    public int PrimaryDepth { set => Layout.BindPrimaryDepth(ProgramId, value, LayoutWarn); }

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
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public float SpecularAOStrength
    {
        set
        {
            Params.SpecularAOStrength = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
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
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
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
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
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
}
