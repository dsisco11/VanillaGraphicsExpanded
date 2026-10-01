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
/// applying fog once and writing scene-linear lighting for the separate display resolve.
/// </summary>
[ShaderProgram("Contract", "pbr_composite", 9)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_composite.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Lighting")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Composite")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(LumOnEnabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnablePbrComposite))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnableShortRangeAo))]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
public sealed partial class PBRCompositeShaderProgram : GpuProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the VgePbrCompositeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgePbrCompositeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the directDiffuse Sampler slot.</summary>
    [ShaderBinding("directDiffuse", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture DirectDiffuseTexture { set; }
    /// <summary>Declares the directSpecular Sampler slot.</summary>
    [ShaderBinding("directSpecular", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture DirectSpecularTexture { set; }
    /// <summary>Declares the emissive Sampler slot.</summary>
    [ShaderBinding("emissive", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture EmissiveTexture { set; }
    /// <summary>Declares the indirectDiffuse Sampler slot.</summary>
    [ShaderBinding("indirectDiffuse", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture IndirectDiffuseTexture { set; }
    /// <summary>Declares the gBufferAlbedo Sampler slot.</summary>
    [ShaderBinding("gBufferAlbedo", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture GBufferAlbedoTexture { set; }
    /// <summary>Declares the gBufferMaterial Sampler slot.</summary>
    [ShaderBinding("gBufferMaterial", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture GBufferMaterialTexture { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture PrimaryDepthTexture { set; }
    /// <summary>Declares the gBufferPosition Sampler slot.</summary>
    [ShaderBinding("gBufferPosition", ShaderBindingKind.Sampler, 11, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture GBufferPositionTexture { set; }
    /// <summary>Declares the gBufferNormal Sampler slot.</summary>
    [ShaderBinding("gBufferNormal", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture GBufferNormalTexture { set; }
    /// <summary>Declares the gBufferEnvironment Sampler slot.</summary>
    [ShaderBinding("gBufferEnvironment", ShaderBindingKind.Sampler, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture GBufferEnvironmentTexture { set; }
    /// <summary>Declares the vge_atmosphereAerialRadiance Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.Sampler, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture AtmosphereAerialRadiance { set; }
    /// <summary>Declares the vge_atmosphereAerialAttenuation Sampler slot.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.Sampler, 10, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture AtmosphereAerialAttenuation { set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

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
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
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

    /// <summary>Unbiased first-person view-space positions, independent of visibility depth.</summary>
    public int GBufferPosition { set => Layout.BindGBufferPosition(ProgramId, value, LayoutWarn); }

    public int GBufferNormal { set => Layout.BindGBufferNormal(ProgramId, value, LayoutWarn); }

    /// <summary>Supplies standalone environmental irradiance without any LumOn texture dependency.</summary>
    public int GBufferEnvironment { set => Layout.BindEnvironment(ProgramId, value, LayoutWarn); }

    #endregion

    #region Fog

    /// <summary>Sets the shared atmospheric aerial-perspective approximation before final display conversion.</summary>
    internal void SetAtmosphere(Atmosphere.AtmosphereLighting? lighting)
    {
        Params.SetAtmosphere(lighting);
        Layout.BindAerial(ProgramId, ModSystems.AtmosphereModSystem.AerialRadianceTextureId,
            ModSystems.AtmosphereModSystem.AerialAttenuationTextureId, LayoutWarn);
        UploadAndBindParamsUbo();
    }

    /// <summary>Selects engine underwater fog without applying it again to atmospheric air.</summary>
    internal void SetUnderwater(bool underwater)
    {
        Params.SetUnderwater(underwater);
        UploadAndBindParamsUbo();
    }

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
