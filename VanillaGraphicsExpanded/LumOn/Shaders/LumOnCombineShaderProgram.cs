using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Combine/Integrate pass (SPG-009).
/// Combines indirect diffuse lighting with direct lighting and applies
/// proper material modulation (albedo, metallic rejection).
/// </summary>
public class LumOnCombineShaderProgram : GpuProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnCombineShaderProgram
        {
            PassName = "lumon_combine",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();

        api.Shader.RegisterMemoryShaderProgram("lumon_combine", instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Scene with direct lighting only (captured before GI application).
    /// </summary>
    public GpuTexture? SceneDirect { set => BindTexture2D("sceneDirect", value, 0); }

    /// <summary>
    /// LumOn indirect diffuse output (upsampled to full resolution).
    /// </summary>
    public GpuTexture? IndirectDiffuse { set => BindTexture2D("indirectDiffuse", value, 1); }

    /// <summary>
    /// G-Buffer albedo texture for material modulation.
    /// </summary>
    public int GBufferAlbedo { set => BindExternalTexture2D("gBufferAlbedo", value, 2, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-Buffer material properties (roughness, metallic, etc.).
    /// </summary>
    public int GBufferMaterial { set => BindExternalTexture2D("gBufferMaterial", value, 3, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-Buffer world-space normals.
    /// </summary>
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 5, GpuSamplers.NearestClamp); }

    /// <summary>
    /// Primary depth texture for sky detection.
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 4, GpuSamplers.NearestClamp); }

    #endregion

    // Per-frame state (matrices) is provided via LumOnFrameUBO.

    #region PBR Composite Defines (Phase 15 → SetDefine migration)

    public bool EnablePbrComposite { set => SetDefine(VgeShaderDefines.LumOnPbrComposite, value ? "1" : "0"); }

    public bool EnableAO { set => SetDefine(VgeShaderDefines.LumOnEnableAo, value ? "1" : "0"); }

    public bool EnableShortRangeAo { set => SetDefine(VgeShaderDefines.LumOnEnableShortRangeAo, value ? "1" : "0"); }

    [System.Obsolete("Renamed to EnableShortRangeAo.")]
    public bool EnableBentNormal { set => EnableShortRangeAo = value; }

    public float DiffuseAOStrength { set => Uniform("diffuseAOStrength", value); }

    public float SpecularAOStrength { set => Uniform("specularAOStrength", value); }

    #endregion

    #region Intensity Uniforms

    /// <summary>
    /// Global intensity multiplier for indirect lighting.
    /// Default: 1.0
    /// </summary>
    public float IndirectIntensity { set => Uniform("indirectIntensity", value); }

    /// <summary>
    /// RGB tint applied to indirect lighting.
    /// </summary>
    public Vec3f IndirectTint { set => Uniform("indirectTint", value); }

    #endregion

    #region Feature Toggle

    /// <summary>
    /// Whether LumOn is enabled.
    /// When disabled, passes through direct lighting unchanged.
    /// </summary>
    public bool LumOnEnabled { set => SetDefine(VgeShaderDefines.LumOnEnabled, value ? "1" : "0"); }

    #endregion
}
