using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Owns geometry-only visibility bindings shared by direct irradiance gather and debug consumers.</summary>
internal sealed class LumOnNearFieldVisibilityBindings
{
    public const string EnabledDefine = "VGE_LUMON_DIRECT_LOCAL_VISIBILITY";
    private readonly GpuProgramLayout layout;
    private readonly LumOnNearFieldParamsUbo parameters = new();

    #region Shader Contract
    /// <summary>Registers the two geometry samplers and the shared local mapping contract.</summary>
    public LumOnNearFieldVisibilityBindings(GpuProgramLayout layout, int geometryUnit, int readinessUnit)
    {
        this.layout = layout;
        layout.RegisterUniformBlockBinding(LumOnNearFieldParamsUbo.BlockName, LumOnNearFieldParamsUbo.Binding, required: false);
        layout.RegisterSamplerUnit("nearFieldGeometry", geometryUnit, required: false);
        layout.RegisterSamplerUnit("nearFieldRegions", readinessUnit, required: false);
    }

    /// <summary>Binds a coherent local snapshot; unavailable geometry never implies visibility.</summary>
    public void Bind(GpuProgram program, NearFieldGpuScene? scene)
    {
        parameters.Set(scene?.Origin ?? default, scene?.Resolution ?? 0, cellSize: scene?.CellSize ?? 16,
            supportedOrigins: scene?.SupportedOrigins, maximumTraceReach: scene?.MaximumTraceReach ?? 0);
        parameters.BindTo(program, LumOnNearFieldParamsUbo.BlockName, "LumOn.DirectVisibility");
        layout.TryBindSamplerTextureActive(program.ProgramId, "nearFieldGeometry", TextureTarget.Texture3D,
            scene?.Geometry.TextureId ?? 0, GpuSamplers.NearestClamp.SamplerId, warn: null);
        layout.TryBindSamplerTextureActive(program.ProgramId, "nearFieldRegions", TextureTarget.Texture3D,
            scene?.Regions.TextureId ?? 0, GpuSamplers.NearestClamp.SamplerId, warn: null);
    }
    #endregion
}
