using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns draw publication and the texture target and sampler policy for every liquid image.</summary>
internal sealed partial class LiquidShaderProgram
{
    #region Draw inputs
    /// <summary>Sets ModelViewMatrix for the next draw submission.</summary>
    internal float[] ModelViewMatrix { set { draw.SetModelView(value); } }
    /// <summary>Sets Origin for the next draw submission.</summary>
    internal Vector3 Origin { set { draw.SetOrigin(value); } }
    /// <summary>Sets ForcedTransparency for the next draw submission.</summary>
    internal float ForcedTransparency { set { draw.SetTransparency(value); } }
    /// <summary>Stages the depth pass's coherent wave snapshot before publishing frame inputs.</summary>
    internal LiquidWaveFrame WaveFrame { set { wave.Phases = value.Phases; wave.Wind = value.Wind; } }
    /// <summary>Stages material-medium availability before frame publication.</summary>
    internal bool MediumLookupEnabled { set => frame.MediumLookupEnabled = value; }
    /// <summary>Selects interface-only OIT when bulk medium transport has already affected the opaque background.</summary>
    internal bool VolumeTransportEnabled { set => frame.VolumeTransportEnabled = value; }
    /// <summary>Stages availability of immutable opaque radiance and depth.</summary>
    internal bool RefractionEnabled { set => frame.RefractionEnabled = value; }
    /// <summary>Selects the frame's common scene color convention before optical composition.</summary>
    internal bool SceneLinear { set => frame.SceneLinear = value; }
    #endregion

    #region Texture inputs
    /// <summary>Retains immutable unattenuated opaque scene-linear radiance.</summary>
    public partial DynamicTexture2D? RefractionColorTexture { set; }
    /// <summary>Retains the hardware-depth image from the same opaque invocation.</summary>
    public partial DynamicTexture2D? RefractionDepthTexture { set; }
    /// <summary>Retains the borrowed TerrainTexture with its declared target and sampler.</summary>
    public partial int TerrainTexture { set; }
    /// <summary>Retains the borrowed DepthTexture with its declared target and sampler.</summary>
    public partial int DepthTexture { set; }
    /// <summary>Retains the borrowed MaterialParamsTexture with its declared target and sampler.</summary>
    public partial Texture2D? MaterialParamsTexture { set; }
    /// <summary>Binds the current material-medium index image.</summary>
    public partial Texture2D? WaterMediumIndicesTexture { set; }
    /// <summary>Binds the current material-medium coefficient table.</summary>
    public partial Texture2D? WaterMediumRecordsTexture { set; }
    /// <summary>Retains the borrowed ShadowMapNear with its declared target and sampler.</summary>
    public partial int ShadowMapNear { set; }
    /// <summary>Retains the borrowed ShadowMapFar with its declared target and sampler.</summary>
    public partial int ShadowMapFar { set; }
    /// <summary>Retains the borrowed AerialRadianceTexture with its declared target and sampler.</summary>
    public partial DynamicTexture3D? AerialRadianceTexture { set; }
    /// <summary>Retains the borrowed AerialAttenuationTexture with its declared target and sampler.</summary>
    public partial DynamicTexture3D? AerialAttenuationTexture { set; }
    #endregion


}
