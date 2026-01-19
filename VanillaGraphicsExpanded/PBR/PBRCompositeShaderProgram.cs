using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Shader program for final compositing of PBR direct buffers + optional indirect lighting,
/// applying fog once and writing to the primary framebuffer.
/// </summary>
public sealed class PBRCompositeShaderProgram : VgeShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new PBRCompositeShaderProgram
        {
            PassName = "pbr_composite",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();

        api.Shader.RegisterMemoryShaderProgram("pbr_composite", instance);
    }

    #endregion

    #region Texture Samplers

    public int DirectDiffuse { set => BindTexture2D("directDiffuse", value, 0); }

    public int DirectSpecular { set => BindTexture2D("directSpecular", value, 1); }

    public int Emissive { set => BindTexture2D("emissive", value, 2); }

    public int IndirectDiffuse { set => BindTexture2D("indirectDiffuse", value, 3); }

    public int GBufferAlbedo { set => BindTexture2D("gBufferAlbedo", value, 4); }

    public int GBufferMaterial { set => BindTexture2D("gBufferMaterial", value, 5); }

    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 6); }

    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 7); }

    #endregion

    #region Fog

    public Vec4f RgbaFogIn { set => Uniform("rgbaFogIn", value); }

    public float FogDensityIn { set => Uniform("fogDensityIn", value); }

    public float FogMinIn { set => Uniform("fogMinIn", value); }

    #endregion

    #region Matrices

    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    #endregion

    #region Composite Controls

    public float IndirectIntensity { set => Uniform("indirectIntensity", value); }

    public Vec3f IndirectTint { set => Uniform("indirectTint", value); }

    public bool LumOnEnabled { set => SetDefine(VgeShaderDefines.LumOnEnabled, value ? "1" : "0"); }

    public bool EnablePbrComposite { set => SetDefine(VgeShaderDefines.LumOnPbrComposite, value ? "1" : "0"); }

    public bool EnableAO { set => SetDefine(VgeShaderDefines.LumOnEnableAo, value ? "1" : "0"); }

    public bool EnableShortRangeAo { set => SetDefine(VgeShaderDefines.LumOnEnableShortRangeAo, value ? "1" : "0"); }

    [System.Obsolete("Renamed to EnableShortRangeAo.")]
    public bool EnableBentNormal { set => EnableShortRangeAo = value; }

    public float DiffuseAOStrength { set => Uniform("diffuseAOStrength", value); }

    public float SpecularAOStrength { set => Uniform("specularAOStrength", value); }

    #endregion
}
