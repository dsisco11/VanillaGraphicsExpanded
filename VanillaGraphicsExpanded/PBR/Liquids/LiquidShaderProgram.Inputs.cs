using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns draw publication and the texture target and sampler policy for every liquid image.</summary>
internal sealed partial class LiquidShaderProgram
{
    #region Draw inputs
    /// <summary>Sets ModelViewMatrix and publishes an immutable draw snapshot on the active program.</summary>
    internal float[] ModelViewMatrix { set { draw.SetModelView(value); PublishDraw(); } }
    /// <summary>Sets Origin and publishes an immutable draw snapshot on the active program.</summary>
    internal Vector3 Origin { set { draw.SetOrigin(value); PublishDraw(); } }
    /// <summary>Sets ForcedTransparency and publishes an immutable draw snapshot on the active program.</summary>
    internal float ForcedTransparency { set { draw.SetTransparency(value); PublishDraw(); } }
    /// <summary>Publishes staged frame inputs and the current draw parameters on the active program.</summary>
    internal void ApplyInputs() { PublishFrame(); PublishDraw(); }
    #endregion

    #region Texture inputs
    /// <summary>Binds the borrowed TerrainTexture with its declared target and sampler.</summary>
    internal int TerrainTexture { set => BindImage("terrainTex", value, TextureTarget.Texture2D, 0); }
    /// <summary>Binds the borrowed DepthTexture with its declared target and sampler.</summary>
    internal int DepthTexture { set => BindImage("depthTex", value, TextureTarget.Texture2D, GpuSamplers.NearestClamp.SamplerId); }
    /// <summary>Binds the borrowed MaterialParamsTexture with its declared target and sampler.</summary>
    internal int MaterialParamsTexture { set => BindImage("vge_materialParamsTex", value, TextureTarget.Texture2D, GpuSamplers.NearestClamp.SamplerId); }
    /// <summary>Binds the borrowed ShadowMapNear with its declared target and sampler.</summary>
    internal int ShadowMapNear { set => BindImage("shadowMapNear", value, TextureTarget.Texture2D, GpuSamplers.ShadowCompareLinearClamp.SamplerId); }
    /// <summary>Binds the borrowed ShadowMapFar with its declared target and sampler.</summary>
    internal int ShadowMapFar { set => BindImage("shadowMapFar", value, TextureTarget.Texture2D, GpuSamplers.ShadowCompareLinearClamp.SamplerId); }
    /// <summary>Binds the borrowed AerialRadianceTexture with its declared target and sampler.</summary>
    internal int AerialRadianceTexture { set => BindImage("vge_atmosphereAerialRadiance", value, TextureTarget.Texture3D, 0); }
    /// <summary>Binds the borrowed AerialAttenuationTexture with its declared target and sampler.</summary>
    internal int AerialAttenuationTexture { set => BindImage("vge_atmosphereAerialAttenuation", value, TextureTarget.Texture3D, 0); }
    #endregion
}
