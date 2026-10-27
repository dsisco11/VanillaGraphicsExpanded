using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes exact native buffer-binding overloads and their conditional VAO effects.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Withdraws binding knowledge and observes VAO metadata only for element bindings.</summary>
    internal static NativeBufferBinding BeginNativeBufferBinding(BufferTarget target)
    {
        var cache = Current;
        cache.bufferBindingByTarget.Remove(target);
        var vertex = target == BufferTarget.ElementArrayBuffer
            ? BeginNativeVertexMetadata(0, named: false, elementBinding: true) : default;
        return new(cache, target, vertex);
    }
    #endregion

    /// <summary>Preserves the exact native call while invalidating only its affected binding knowledge.</summary>
    internal readonly struct NativeBufferBinding(OpenGlStateCache cache, BufferTarget target, NativeVertexMetadata vertex)
    {
        #region Internal API
        /// <summary>Publishes VAO revision when applicable; other bindings have no resource metadata effect.</summary>
        internal void Complete(bool returned)
        {
            cache.bufferBindingByTarget.Remove(target);
            if (target == BufferTarget.ElementArrayBuffer) vertex.Complete(returned);
        }
        #endregion
    }
}
