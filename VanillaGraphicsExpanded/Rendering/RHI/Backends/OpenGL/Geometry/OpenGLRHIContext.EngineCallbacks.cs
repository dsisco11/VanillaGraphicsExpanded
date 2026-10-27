using System;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Contains native state effects caused by explicitly engine-owned geometry and particle callbacks.</summary>
internal static partial class OpenGLRHIContext
{
    #region Internal API
    /// <summary>Uploads engine-owned geometry while preserving the selectors the native uploader changes.</summary>
    internal static MeshRef UploadEngineMesh(IRenderAPI render, MeshData data)
    { _ = Current; return backend!.UploadEngineMesh(render, data); }
    /// <summary>Retires an engine allocation only after its existing owner admits cleanup.</summary>
    internal static void DeleteEngineMesh(IRenderAPI render, MeshRef mesh)
    { _ = Current; backend!.DeleteEngineMesh(render, mesh); }
    /// <summary>Captures the previous patch size and applies the engine executable's selected topology.</summary>
    internal static int SelectEnginePatchVertices(int count)
    { _ = Current; return backend!.SelectEnginePatchVertices(count); }
    /// <summary>Begins native particle redirection without transferring content publication ownership.</summary>
    internal static IDisposable BeginEngineParticleCapture(GpuFramebuffer target, GlPipelineDesc capture, GlPipelineDesc boundary)
    { _ = Current; return backend!.BeginEngineParticleCapture(target, capture, boundary); }
    #endregion
}
