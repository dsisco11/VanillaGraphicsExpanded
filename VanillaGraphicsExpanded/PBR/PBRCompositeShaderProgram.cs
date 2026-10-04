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
[ShaderProgram("Contract", "pbr_composite", 16)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_composite.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Lighting")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Composite")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(LumOnEnabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnablePbrComposite))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EnableShortRangeAo))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(PreOverlayOnly))]
public sealed partial class PBRCompositeShaderProgram : GpuProgram, IPBRCompositeShaderProgramBindings
{
    /// <summary>Registry identity for the retained environment-only pre-overlay executable and inputs.</summary>
    internal const string PreOverlayPassName = "pbr_composite_pre_overlay";

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

    /// <summary>Exposes retained parameters with an owner mutation guard.</summary>
    private PbrCompositeParamsUbo Params
    {
        get
        {
            var parameters = Layout.Params;
            parameters.SetWriteGuard(RequireInputMutation);
            return parameters;
        }
    }

#region Texture Samplers
    /// <summary>Controls access to optional pre-overlay images without sampling absent fallback storage.</summary>
    internal bool PreOverlaySourceEnabled { set => Params.PreOverlaySourceEnabled = value; }
    /// <summary>Unattenuated world color captured before the local overlay.</summary>
    public partial DynamicTexture2D? PreOverlayColor { set; }
    /// <summary>Matching clean world depth.</summary>
    public partial DynamicTexture2D? PreOverlayDepth { set; }

    public partial GpuTexture? DirectDiffuse { set; }

    public partial GpuTexture? DirectSpecular { set; }

    public partial GpuTexture? Emissive { set; }

    public partial GpuTexture? IndirectDiffuse { set; }

    public partial int GBufferAlbedo { set; }

    public partial DynamicTexture2D? GBufferMaterial { set; }

    public partial int PrimaryDepth { set; }

    /// <summary>Unbiased first-person view-space positions, independent of visibility depth.</summary>
    public partial int GBufferPosition { set; }

    public partial DynamicTexture2D? GBufferNormal { set; }

    /// <summary>Supplies standalone environmental irradiance without any LumOn texture dependency.</summary>
    public partial DynamicTexture2D? GBufferEnvironment { set; }
    /// <summary>Supplies completed water boundary optical depth.</summary>
    public partial DynamicTexture2D? WaterOpticalDepth { set; }
    /// <summary>Supplies completed water scattering-source accumulation.</summary>
    public partial DynamicTexture2D? WaterSource { set; }
    /// <summary>Publishes water resources and invalidates the optional path when capture is unavailable.</summary>
    internal void SetWaterVolume(Liquids.WaterVolumeFrame? frame)
    {
        Params.SetWaterVolume(frame);
        WaterOpticalDepth = frame?.OpticalDepth;
        WaterSource = frame?.Source;
    }

    #endregion

    #region Fog

    /// <summary>Enables optional pre-transport MRT publication without changing normal composition.</summary>
    internal bool RefractionSourceEnabled { set => Params.RefractionSourceEnabled = value; }

    /// <summary>Sets the shared atmospheric aerial-perspective approximation before final display conversion.</summary>
    internal void SetAtmosphere(Atmosphere.AtmosphereLighting? lighting)
    {
        Params.SetAtmosphere(lighting);
        AtmosphereAerialRadiance = ModSystems.AtmosphereModSystem.AerialRadianceTexture;
        AtmosphereAerialAttenuation = ModSystems.AtmosphereModSystem.AerialAttenuationTexture;
    }

    /// <summary>Selects engine underwater fog without applying it again to atmospheric air.</summary>
    internal void SetUnderwater(bool underwater)
    {
        Params.SetUnderwater(underwater);
    }

    public Vec4f RgbaFogIn
    {
        set
        {
            RequireInputMutation();
            Params.RgbaFogIn = new System.Numerics.Vector4(value.X, value.Y, value.Z, value.W);
        }
    }

    public float FogDensityIn
    {
        set
        {
            RequireInputMutation();
            _fogDensity = value;
            Params.FogParams = (_fogDensity, _fogMin);
        }
    }

    public float FogMinIn
    {
        set
        {
            RequireInputMutation();
            _fogMin = value;
            Params.FogParams = (_fogDensity, _fogMin);
        }
    }

    #endregion

    #region Matrices

    public float[] InvProjectionMatrix
    {
        set
        {
            RequireInputMutation();
            Params.InvProjectionMatrix = value;
        }
    }

    public float[] ViewMatrix
    {
        set
        {
            RequireInputMutation();
            Params.ViewMatrix = value;
        }
    }

    #endregion

    #region Composite Controls

    /// <summary>Specializes the retained capture owner to publish only unattenuated receiver color and depth.</summary>
    [ShaderOption("VGE_COMPOSITE_PRE_OVERLAY_ONLY", false)]
    internal partial bool PreOverlayOnly { get; set; }

    public float IndirectIntensity
    {
        set
        {
            RequireInputMutation();
            _indirectIntensity = value;
            Params.IndirectTintAndIntensity = (_indirectTint, _indirectIntensity);
        }
    }

    public Vec3f IndirectTint
    {
        set
        {
            RequireInputMutation();
            _indirectTint = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            Params.IndirectTintAndIntensity = (_indirectTint, _indirectIntensity);
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
            RequireInputMutation();
            _diffuseAO = value;
            Params.AOStrengths = (_diffuseAO, _specularAO);
        }
    }

    public float SpecularAOStrength
    {
        set
        {
            RequireInputMutation();
            _specularAO = value;
            Params.AOStrengths = (_diffuseAO, _specularAO);
        }
    }


    #endregion
    #region Binding sources
    /// <summary>Supplies packed parameters for one publication per use.</summary>
    CpuUniformBuffer IPBRCompositeShaderProgramBindings.Parameters => Params;
    #endregion
}
