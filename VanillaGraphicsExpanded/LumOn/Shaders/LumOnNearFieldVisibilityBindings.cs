using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
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
    /// <summary>Uses the owning program layout for the shared geometry sampler slots.</summary>
    public LumOnNearFieldVisibilityBindings(GpuProgramLayout layout)
    {
        this.layout = layout;
    }

    /// <summary>Binds a coherent local snapshot; unavailable geometry never implies visibility.</summary>
    public void Bind(GpuProgram program, TraceGeometryGpuScene? scene)
    {
        parameters.SetShared(scene);
        parameters.BindTo(program, LumOnNearFieldParamsUbo.BlockName, "LumOn.DirectVisibility");
        layout.TryBindSamplerTextureActive(program.ProgramId, "nearFieldGeometry", TextureTarget.Texture3D,
            scene?.Geometry.TextureId ?? 0, GpuSamplers.NearestClamp.SamplerId, warn: null);
        layout.TryBindSamplerTextureActive(program.ProgramId, "nearFieldRegions", TextureTarget.Texture3D,
            scene?.Readiness.TextureId ?? 0, GpuSamplers.NearestClamp.SamplerId, warn: null);
    }
    #endregion
}
