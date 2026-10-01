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
    #endregion

    #region Texture inputs
    /// <summary>Retains the borrowed TerrainTexture with its declared target and sampler.</summary>
    public partial int TerrainTexture { set; }
    /// <summary>Retains the borrowed DepthTexture with its declared target and sampler.</summary>
    public partial int DepthTexture { set; }
    /// <summary>Retains the borrowed MaterialParamsTexture with its declared target and sampler.</summary>
    public partial int MaterialParamsTexture { set; }
    /// <summary>Retains the borrowed ShadowMapNear with its declared target and sampler.</summary>
    public partial int ShadowMapNear { set; }
    /// <summary>Retains the borrowed ShadowMapFar with its declared target and sampler.</summary>
    public partial int ShadowMapFar { set; }
    /// <summary>Retains the borrowed AerialRadianceTexture with its declared target and sampler.</summary>
    public partial int AerialRadianceTexture { set; }
    /// <summary>Retains the borrowed AerialAttenuationTexture with its declared target and sampler.</summary>
    public partial int AerialAttenuationTexture { set; }
    #endregion


}
