using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for building HZB mip 0 from the primary depth texture.
/// Outputs raw depth (0..1) into an R32F render target.
/// </summary>
public sealed class LumOnHzbCopyShaderProgram : ShaderProgram
{
    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnHzbCopyShaderProgram
        {
            PassName = "lumon_hzb_copy",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_hzb_copy", instance);
        instance.Compile();
    }

    /// <summary>
    /// Primary depth texture (VS depth buffer).
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 0); }
}
