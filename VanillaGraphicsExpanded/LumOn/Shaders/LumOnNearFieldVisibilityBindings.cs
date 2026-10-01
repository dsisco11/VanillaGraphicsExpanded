using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Owns geometry-only visibility bindings shared by direct irradiance gather and debug consumers.</summary>
internal sealed class LumOnNearFieldVisibilityBindings
{
    public const string EnabledDefine = "VGE_LUMON_DIRECT_LOCAL_VISIBILITY";
    private readonly LumOnNearFieldParamsUbo parameters = new();

    #region Shader Contract
    /// <summary>Retains geometry for the next owner submission.</summary>
    internal GpuTexture? Geometry { get; private set; }
    /// <summary>Retains readiness metadata for the next owner submission.</summary>
    internal GpuTexture? Regions { get; private set; }
    /// <summary>Exposes packed traversal parameters through the owner's binding contract.</summary>
    internal CpuUniformBuffer Parameters => parameters;

    /// <summary>Stages a coherent scene without issuing any graphics operations.</summary>
    public void Stage(GpuProgram program, TraceGeometryGpuScene? scene, LumOnNearFieldTraceSettings? settings = null)
    {
        program.RequireInputMutation();
        parameters.SetWriteGuard(program.RequireInputMutation);
        parameters.SetShared(scene, settings);
        Geometry = scene?.Geometry;
        Regions = scene?.Readiness;
    }
    #endregion
}
