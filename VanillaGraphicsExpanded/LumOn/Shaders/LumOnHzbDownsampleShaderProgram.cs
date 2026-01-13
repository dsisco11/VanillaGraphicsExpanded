using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for downsampling an HZB mip level into the next mip.
/// Uses MIN depth over a 2x2 block.
/// </summary>
public sealed class LumOnHzbDownsampleShaderProgram : VgeShaderProgram
{
    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnHzbDownsampleShaderProgram
        {
            PassName = "lumon_hzb_downsample",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("lumon_hzb_downsample", instance);
        instance.Initialize(api);
        instance.CompileAndLink();
    }

    /// <summary>
    /// HZB depth texture (mipmapped R32F).
    /// </summary>
    public int HzbDepth { set => BindTexture2D("hzbDepth", value, 0); }

    /// <summary>
    /// Source mip level to read from.
    /// </summary>
    public int SrcMip { set => Uniform("srcMip", value); }
}
