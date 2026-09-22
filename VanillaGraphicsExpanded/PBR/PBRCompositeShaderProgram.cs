using VanillaGraphicsExpanded.Rendering.Contracts;
using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Shader program for final compositing of PBR direct buffers + optional indirect lighting,
/// applying fog once and writing to the primary framebuffer.
/// </summary>
[ShaderProgram("Contract", "pbr_composite", 8)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_composite.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Lighting")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Composite")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(LumOnEnabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnablePbrComposite))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnableShortRangeAo))]
public sealed partial class PBRCompositeShaderProgram : GpuProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    // Cached state for compound properties
    private float _fogDensity, _fogMin;
    private System.Numerics.Vector3 _indirectTint;
    private float _indirectIntensity;
    private float _diffuseAO, _specularAO;

    protected override GpuProgramLayout CreateLayout() => new PbrCompositeProgramLayout();

    private PbrCompositeProgramLayout Layout => (PbrCompositeProgramLayout)ProgramLayout;

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new PBRCompositeShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();

        api.Shader.RegisterMemoryShaderProgram(Contract.Identity, instance);
    }

    #endregion

    private PbrCompositeParamsUbo Params => Layout.Params;

    private void UploadAndBindParamsUbo()
    {
        Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
    }

    #region Texture Samplers

    public GpuTexture? DirectDiffuse { set => Layout.BindDirectDiffuse(ProgramId, value?.TextureId ?? 0, LayoutWarn); }

    public GpuTexture? DirectSpecular { set => Layout.BindDirectSpecular(ProgramId, value?.TextureId ?? 0, LayoutWarn); }

    public GpuTexture? Emissive { set => Layout.BindEmissive(ProgramId, value?.TextureId ?? 0, LayoutWarn); }

    public GpuTexture? IndirectDiffuse { set => Layout.BindIndirectDiffuse(ProgramId, value?.TextureId ?? 0, LayoutWarn); }

    public int GBufferAlbedo { set => Layout.BindGBufferAlbedo(ProgramId, value, LayoutWarn); }

    public int GBufferMaterial { set => Layout.BindGBufferMaterial(ProgramId, value, LayoutWarn); }

    public int PrimaryDepth { set => Layout.BindPrimaryDepth(ProgramId, value, LayoutWarn); }

    public int GBufferNormal { set => Layout.BindGBufferNormal(ProgramId, value, LayoutWarn); }

    #endregion

    #region Fog

    public Vec4f RgbaFogIn
    {
        set
        {
            Params.RgbaFogIn = new System.Numerics.Vector4(value.X, value.Y, value.Z, value.W);
            UploadAndBindParamsUbo();
        }
    }

    public float FogDensityIn
    {
        set
        {
            _fogDensity = value;
            Params.FogParams = (_fogDensity, _fogMin);
            UploadAndBindParamsUbo();
        }
    }

    public float FogMinIn
    {
        set
        {
            _fogMin = value;
            Params.FogParams = (_fogDensity, _fogMin);
            UploadAndBindParamsUbo();
        }
    }

    #endregion

    #region Matrices

    public float[] InvProjectionMatrix
    {
        set
        {
            Params.InvProjectionMatrix = value;
            UploadAndBindParamsUbo();
        }
    }

    public float[] ViewMatrix
    {
        set
        {
            Params.ViewMatrix = value;
            UploadAndBindParamsUbo();
        }
    }

    #endregion

    #region Composite Controls

    public float IndirectIntensity
    {
        set
        {
            _indirectIntensity = value;
            Params.IndirectTintAndIntensity = (_indirectTint, _indirectIntensity);
            UploadAndBindParamsUbo();
        }
    }

    public Vec3f IndirectTint
    {
        set
        {
            _indirectTint = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            Params.IndirectTintAndIntensity = (_indirectTint, _indirectIntensity);
            UploadAndBindParamsUbo();
        }
    }

    /// <summary>Gets or sets the declared Enabled shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.Enabled))]
    public partial bool LumOnEnabled { get; set; }

    /// <summary>Gets or sets the declared PbrComposite shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.PbrComposite))]
    public partial bool EnablePbrComposite { get; set; }


    /// <summary>Gets or sets the declared ShortRangeAo shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ShortRangeAo))]
    public partial bool EnableShortRangeAo { get; set; }

    [System.Obsolete("Renamed to EnableShortRangeAo.")]
    public bool EnableBentNormal { set => EnableShortRangeAo = value; }

    public float DiffuseAOStrength
    {
        set
        {
            _diffuseAO = value;
            Params.AOStrengths = (_diffuseAO, _specularAO);
            UploadAndBindParamsUbo();
        }
    }

    public float SpecularAOStrength
    {
        set
        {
            _specularAO = value;
            Params.AOStrengths = (_diffuseAO, _specularAO);
            UploadAndBindParamsUbo();
        }
    }


    #endregion
}
