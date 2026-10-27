using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Executes native callback selector changes under the existing cache and original context.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Contains temporary VAO/array-buffer selection caused by the engine uploader.</summary>
    internal MeshRef UploadEngineMesh(IRenderAPI render, MeshData data)
    {
        RequireContext(); cache.RequireOutsideEngineBoundary();
        using var vertex = cache.BindVertexArrayScope(0);
        using var array = cache.BindBufferScope(BufferTarget.ArrayBuffer, 0);
        var mesh = render.UploadMesh(data);
        RequireContext();
        return mesh;
    }
    /// <summary>Executes an already admitted engine mesh cleanup against its native owner.</summary>
    internal void DeleteEngineMesh(IRenderAPI render, MeshRef mesh)
    { RequireContext(); render.DeleteMesh(mesh); RequireContext(); }
    /// <summary>Changes patch size and returns the original value for the enclosing engine callback.</summary>
    internal int SelectEnginePatchVertices(int count)
    {
        RequireContext();
        int previous = cache.PatchVertices;
        cache.SetPatchVertices(count);
        return previous;
    }
    /// <summary>Owns only native particle routing and blending; producer completion stays above execution.</summary>
    internal IDisposable BeginEngineParticleCapture(GpuFramebuffer target, GlPipelineDesc capture, GlPipelineDesc boundary)
    {
        RequireContext();
        return new ParticleCapture(this, target, capture, boundary);
    }
    #endregion
    /// <summary>Restores the engine's documented particle boundary exactly once.</summary>
    private sealed class ParticleCapture : IDisposable
    {
        private readonly OpenGLBackend backend;
        private readonly GlPipelineDesc boundary;
        private readonly OpenGlStateCache.FramebufferScope selectors;
        private bool disposed;
        #region Internal API
        /// <summary>Captures selectors before applying the known engine contract and redirecting output.</summary>
        internal ParticleCapture(OpenGLBackend backend, GpuFramebuffer target, GlPipelineDesc capture, GlPipelineDesc boundary)
        {
            this.backend = backend; this.boundary = boundary;
            target.ValidateAttachments();
            selectors = backend.cache.BindFramebufferScope();
            try
            {
                backend.cache.Apply(boundary);
                backend.SelectFramebufferOwner(target, FramebufferTarget.Framebuffer);
                backend.cache.Apply(capture);
            }
            catch { Dispose(); throw; }
        }
        #endregion
        #region Public API
        /// <summary>Restores selectors and the engine particle blend state after success or interruption.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; backend.RequireContext();
            try { selectors.Dispose(); }
            finally { backend.cache.Apply(boundary); }
        }
        #endregion
    }
}
