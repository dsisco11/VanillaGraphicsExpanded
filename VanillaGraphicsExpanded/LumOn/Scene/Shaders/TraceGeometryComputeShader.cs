using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Retains a coherent geometry generation for generated compute submission.</summary>
internal abstract partial class TraceGeometryComputeShader : GpuComputeProgram, ITraceGeometryInputs
{
    private readonly LumOnNearFieldParamsUbo geometryParameters = new();
    private TraceGeometryGpuScene? scene;

    #region Public API
    /// <summary>Adopts the executable and guards changes to the geometry domain.</summary>
    protected TraceGeometryComputeShader(GpuComputePipeline pipeline) : base(pipeline)
        => OwnUniformBuffer(geometryParameters);

    /// <summary>Retains the scene and domain together without issuing GPU commands.</summary>
    public void BindSharedGeometry(TraceGeometryGpuScene? value)
    {
        RequireInputMutation();
        geometryParameters.SetShared(value);
        scene = value;
    }
    #endregion

    #region Private
    /// <summary>Supplies the retained logical sampling domains.</summary>
    CpuUniformBuffer ITraceGeometryInputs.LumOnNearField => geometryParameters;
    /// <summary>Supplies the retained occupancy texture.</summary>
    GpuTexture? ITraceGeometryInputs.NearFieldGeometry => scene?.Geometry;
    /// <summary>Supplies the retained readiness texture.</summary>
    GpuTexture? ITraceGeometryInputs.NearFieldRegions => scene?.Readiness;
    /// <summary>Supplies the retained legacy-light texture.</summary>
    GpuTexture? ITraceGeometryInputs.TraceSceneLegacy => scene?.Legacy;
    /// <summary>Supplies retained face geometry.</summary>
    GpuTexture? ITraceGeometryInputs.TraceSceneFaces => scene?.Faces;
    #endregion
}
