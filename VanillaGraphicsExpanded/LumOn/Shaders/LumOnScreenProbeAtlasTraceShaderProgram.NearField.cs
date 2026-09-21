using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
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
    internal bool EnsureNearFieldDefines() => !SetDefine("VGE_LUMON_NEAR_FIELD_ENABLED", "1");

    /// <summary>Binds one coherent published scene, or an explicit unavailable scene.</summary>
    internal void BindNearFieldScene(NearFieldGpuScene? scene)
    {
        nearFieldParams.Set(scene?.Origin ?? default(VectorInt3), scene?.Resolution ?? 0, cellSize: scene?.CellSize ?? 16,
            supportedOrigins: scene?.SupportedOrigins, maximumTraceReach: scene?.MaximumTraceReach ?? 0);
        nearFieldParams.BindTo(this, LumOnNearFieldParamsUbo.BlockName, "LumOn.NearField.Parameters");
        BindTexture3D("nearFieldGeometry", scene?.Geometry, 10);
        BindTexture3D("nearFieldLight", scene?.Light, 13);
        BindTexture3D("nearFieldRegions", scene?.Regions, 14);
        BindTexture2D("nearFieldMaterials", scene?.Materials, 15);
    }
    #endregion
}
