using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for building HZB mip 0 from the primary depth texture.
/// Outputs raw depth (0..1) into an R32F render target.
/// </summary>
public sealed class LumOnHzbCopyShaderProgram : GpuProgram
{
    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnHzbCopyShaderProgram
        {
            PassName = "lumon_hzb_copy",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_hzb_copy", instance);
    }

    /// <summary>
    /// Primary depth texture (VS depth buffer).
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 0, GpuSamplers.NearestClamp); }
}
