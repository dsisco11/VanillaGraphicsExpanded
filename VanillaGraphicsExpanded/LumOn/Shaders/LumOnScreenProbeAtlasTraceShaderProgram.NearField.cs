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
    internal bool EnsureNearFieldDefines() => !SetShaderOptions(options => options.Set(LumOnShaderOptions.NearField, true));

    /// <summary>Binds one coherent scene with optional traversal policy; null scenes remain unavailable regardless of policy.</summary>
    internal void BindNearFieldScene(TraceGeometryGpuScene? scene, LumOnNearFieldTraceSettings? settings = null)
    {
        RequireInputMutation();
        nearFieldParams.SetWriteGuard(RequireInputMutation);
        nearFieldParams.SetShared(scene, settings);
        NearFieldGeometry = scene?.Geometry;
        NearFieldLight = scene?.Light;
        NearFieldRegions = scene?.Readiness;
        NearFieldMaterials = scene?.Materials;
        TraceSceneFaces = scene?.Faces;
    }
    /// <summary>Supplies retained near-field parameters for generated submission.</summary>
    CpuUniformBuffer ILumOnScreenProbeAtlasTraceShaderProgramBindings.LumOnNearField => nearFieldParams;
    #endregion
}
