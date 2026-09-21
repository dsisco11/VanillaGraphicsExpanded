using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Owns geometry-only visibility bindings shared by direct irradiance gather and debug consumers.</summary>
internal sealed class LumOnLocalVisibilityBindings
{
    public const string EnabledDefine = "VGE_LUMON_DIRECT_LOCAL_VISIBILITY";
    private readonly GpuProgramLayout layout;
    private readonly LumOnLocalTraceParamsUbo parameters = new();

    #region Shader Contract
    /// <summary>Registers the two geometry samplers and the shared local mapping contract.</summary>
    public LumOnLocalVisibilityBindings(GpuProgramLayout layout, int geometryUnit, int readinessUnit)
    {
        this.layout = layout;
        layout.RegisterUniformBlockBinding(LumOnLocalTraceParamsUbo.BlockName, LumOnLocalTraceParamsUbo.Binding, required: false);
        layout.RegisterSamplerUnit("localTraceGeometry", geometryUnit, required: false);
        layout.RegisterSamplerUnit("localTraceRegions", readinessUnit, required: false);
    }

    /// <summary>Binds a coherent local snapshot; unavailable geometry never implies visibility.</summary>
    public void Bind(GpuProgram program, LocalTraceGpuScene? scene)
    {
        parameters.Set(scene?.Origin ?? default, scene?.Resolution ?? 0, cellSize: scene?.CellSize ?? 16,
            supportedOrigins: scene?.SupportedOrigins, maximumTraceReach: scene?.MaximumTraceReach ?? 0);
        parameters.BindTo(program, LumOnLocalTraceParamsUbo.BlockName, "LumOn.DirectVisibility");
        layout.TryBindSamplerTextureActive(program.ProgramId, "localTraceGeometry", TextureTarget.Texture3D,
            scene?.Geometry.TextureId ?? 0, GpuSamplers.NearestClamp.SamplerId, warn: null);
        layout.TryBindSamplerTextureActive(program.ProgramId, "localTraceRegions", TextureTarget.Texture3D,
            scene?.Regions.TextureId ?? 0, GpuSamplers.NearestClamp.SamplerId, warn: null);
    }
    #endregion
}
