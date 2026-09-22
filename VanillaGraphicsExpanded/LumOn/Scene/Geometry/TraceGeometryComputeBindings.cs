using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Shared geometry texture and domain bindings for surface-cache compute consumers.</summary>
internal sealed class TraceGeometryComputeBindings : IDisposable
{
    private readonly LumOnNearFieldParamsUbo parameters = new();
    private readonly GpuUniformBuffer buffer = GpuUniformBuffer.Create(debugName: "LumOn.SharedGeometry.Parameters");

    #region Compute binding contract
    /// <summary>Registers the shared inputs; unused companions may be optimized away by individual consumers.</summary>
    public static void Register(GpuProgramLayout layout)
    {
        layout.RegisterUniformBlockBinding(LumOnNearFieldParamsUbo.BlockName, LumOnNearFieldParamsUbo.Binding);
        layout.RegisterSamplerUnit("nearFieldGeometry", 8);
        layout.RegisterSamplerUnit("nearFieldRegions", 9);
        layout.RegisterSamplerUnit("traceSceneLegacy", 10, required: false);
        layout.RegisterSamplerUnit("traceSceneFaces", 11);
    }

    /// <summary>Binds a coherent scene and explicit unavailable domains when no generation exists.</summary>
    public void Bind(TraceGeometryGpuScene? scene)
    {
        parameters.SetShared(scene);
        buffer.UploadOrResize(parameters.Bytes, growExponentially: false);
        buffer.BindBase(LumOnNearFieldParamsUbo.Binding);
        GlStateCache.Current.BindTexture(TextureTarget.Texture3D, 8, scene?.Geometry.TextureId ?? 0);
        GlStateCache.Current.BindTexture(TextureTarget.Texture3D, 9, scene?.Readiness.TextureId ?? 0);
        GlStateCache.Current.BindTexture(TextureTarget.Texture3D, 10, scene?.Legacy.TextureId ?? 0);
        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, 11, scene?.Faces.TextureId ?? 0);
        for (int i = 8; i <= 11; i++) GpuSamplers.NearestClamp.Bind(i);
    }

    /// <summary>Releases the compute consumer's parameter buffer.</summary>
    public void Dispose() => buffer.Dispose();
    #endregion
}
