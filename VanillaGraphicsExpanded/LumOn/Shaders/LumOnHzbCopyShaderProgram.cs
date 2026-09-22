using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for building HZB mip 0 from the primary depth texture.
/// Outputs raw depth (0..1) into an R32F render target.
/// </summary>
public sealed partial class LumOnHzbCopyShaderProgram : GpuProgram
{
    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    public LumOnHzbCopyShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);

    }

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnHzbCopyShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram(Contract.Identity, instance);
    }

    /// <summary>
    /// Primary depth texture (VS depth buffer).
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 0, GpuSamplers.NearestClamp); }
}
