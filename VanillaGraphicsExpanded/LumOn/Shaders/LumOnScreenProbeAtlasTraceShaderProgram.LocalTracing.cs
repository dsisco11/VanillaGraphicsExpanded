using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Bindings for the read-only local scene used by screen-probe rays.</summary>
public partial class LumOnScreenProbeAtlasTraceShaderProgram
{
    private readonly LumOnLocalTraceParamsUbo localTraceParams = new();

    #region Local Scene Binding
    /// <summary>Enables production local tracing; shader tests may explicitly compile the legacy path.</summary>
    internal bool EnsureLocalTraceDefines() => !SetDefine("VGE_LUMON_LOCAL_TRACE_ENABLED", "1");

    /// <summary>Binds one coherent published scene, or an explicit unavailable scene.</summary>
    internal void BindLocalScene(LocalTraceGpuScene? scene)
    {
        localTraceParams.Set(scene?.Origin ?? default(VectorInt3), scene?.Resolution ?? 0);
        localTraceParams.BindTo(this, LumOnLocalTraceParamsUbo.BlockName, "LumOn.LocalTrace.Parameters");
        BindTexture3D("localTraceGeometry", scene?.Geometry, 10);
        BindTexture3D("localTraceLight", scene?.Light, 13);
        BindTexture3D("localTraceRegions", scene?.Regions, 14);
        BindTexture2D("localTraceMaterials", scene?.Materials, 15);
    }
    #endregion
}
