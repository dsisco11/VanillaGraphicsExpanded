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
    internal void ApplyInputs()
    {
        PublishFrame();
        if (!GpuUniformRingSystem.TryBind(this, LiquidWaveParamsUbo.BlockName, wave.Bytes, "VGE.Liquid.Waves", true))
            throw new System.InvalidOperationException("Liquid waves require an active uniform ring and linked block.");
        PublishDraw();
    }
    /// <summary>Stages the depth pass's coherent wave snapshot before publishing frame inputs.</summary>
    internal LiquidWaveFrame WaveFrame { set { wave.Phases = value.Phases; wave.Wind = value.Wind; } }
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

    #region Private: Interface implementation
    // The engine-facing setters retain internal visibility and their established sampler
    // policy. Explicit implementations expose the same behavior through the binding contract.
    /// <summary>Implements the terrain binding with its existing texture target.</summary>
    int ILiquidShaderProgramBindings.TerrainTexture { set => TerrainTexture = value; }
    /// <summary>Implements the depth binding without exposing the engine-facing setter.</summary>
    int ILiquidShaderProgramBindings.DepthTexture { set => DepthTexture = value; }
    /// <summary>Implements the material-parameter binding with its existing nearest sampler.</summary>
    int ILiquidShaderProgramBindings.MaterialParamsTexture { set => MaterialParamsTexture = value; }
    /// <summary>Implements the near-shadow binding with its comparison sampler.</summary>
    int ILiquidShaderProgramBindings.ShadowMapNear { set => ShadowMapNear = value; }
    /// <summary>Implements the far-shadow binding with its comparison sampler.</summary>
    int ILiquidShaderProgramBindings.ShadowMapFar { set => ShadowMapFar = value; }
    /// <summary>Implements the atmospheric radiance binding with its existing 3D target.</summary>
    int ILiquidShaderProgramBindings.AerialRadianceTexture { set => AerialRadianceTexture = value; }
    /// <summary>Implements the atmospheric attenuation binding with its existing 3D target.</summary>
    int ILiquidShaderProgramBindings.AerialAttenuationTexture { set => AerialAttenuationTexture = value; }
    #endregion
}
