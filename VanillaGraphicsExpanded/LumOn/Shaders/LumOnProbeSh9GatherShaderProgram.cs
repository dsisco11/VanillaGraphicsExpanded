using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for cheap gather from per-probe SH9 coefficients.
/// Intended for ProbeAtlasGatherMode.EvaluateProjectedSH.
/// </summary>
public class LumOnProbeSh9GatherShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeSh9GatherShaderProgram
        {
            PassName = "lumon_probe_sh9_gather",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_probe_sh9_gather", instance);
        instance.Compile();
    }

    #endregion

    #region SH9 Textures

    public int ProbeSh0 { set => BindTexture2D("probeSh0", value, 0); }
    public int ProbeSh1 { set => BindTexture2D("probeSh1", value, 1); }
    public int ProbeSh2 { set => BindTexture2D("probeSh2", value, 2); }
    public int ProbeSh3 { set => BindTexture2D("probeSh3", value, 3); }
    public int ProbeSh4 { set => BindTexture2D("probeSh4", value, 4); }
    public int ProbeSh5 { set => BindTexture2D("probeSh5", value, 5); }
    public int ProbeSh6 { set => BindTexture2D("probeSh6", value, 6); }

    #endregion

    #region Probe Anchors

    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 7); }
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 8); }

    #endregion

    #region GBuffer Inputs

    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 9); }
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 10); }

    #endregion

    #region Matrix Uniforms

    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    #endregion

    #region Uniforms

    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    public Vec2f HalfResSize { set => Uniform("halfResSize", value); }

    public float ZNear { set => Uniform("zNear", value); }

    public float ZFar { set => Uniform("zFar", value); }

    public float Intensity { set => Uniform("intensity", value); }

    public float[] IndirectTint { set => Uniform("indirectTint", new Vec3f(value[0], value[1], value[2])); }

    #endregion
}
