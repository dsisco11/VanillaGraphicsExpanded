using VanillaGraphicsExpanded.Rendering.Contracts;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Bindings for the read-only near-field scene used by screen-probe rays.</summary>
public partial class LumOnScreenProbeAtlasTraceShaderProgram
{
    private readonly LumOnNearFieldParamsUbo nearFieldParams = new();

    #region Near-Field Scene Binding
    /// <summary>Enables production near-field tracing; shader tests may explicitly compile the legacy path.</summary>
    internal bool EnsureNearFieldDefines() => !SetShaderOption(LumOnShaderOptions.NearField, true);

    /// <summary>Binds one coherent published scene, or an explicit unavailable scene.</summary>
    internal void BindNearFieldScene(TraceGeometryGpuScene? scene)
    {
        nearFieldParams.SetShared(scene);
        nearFieldParams.BindTo(this, LumOnNearFieldParamsUbo.BlockName, "LumOn.NearField.Parameters");
        BindTexture3D("nearFieldGeometry", scene?.Geometry, 10);
        BindTexture3D("nearFieldLight", scene?.Light, 13);
        BindTexture3D("nearFieldRegions", scene?.Readiness, 14);
        BindTexture2D("nearFieldMaterials", scene?.Materials, 15);
        BindTexture2D("traceSceneFaces", scene?.Faces, 19);
    }
    #endregion
}
