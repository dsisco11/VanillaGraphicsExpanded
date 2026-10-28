using System;
using System.Runtime.InteropServices;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Deletes already-admitted original buffer identities through the owning backend.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Deletes one name after the existing retirement service admits its original association.</summary>
    internal void DeleteBuffer(int buffer)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteBuffer(buffer);
        RequireContext();
        if (buffer == 0) return;
        bool deleted = !GL.IsBuffer(buffer);
        RequireContext();
        if (deleted) cache.ReconcileBufferDeletion(buffer);
    }
    /// <summary>Preserves count and duplicate-name semantics while reconciling each returned outcome.</summary>
    internal void DeleteBuffers(int count, ref int buffers)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteBuffers(count, ref buffers);
        RequireContext();
        if (count <= 0) return;
        foreach (int buffer in MemoryMarshal.CreateReadOnlySpan(ref buffers, count))
        {
            RequireContext();
            if (buffer == 0) continue;
            bool deleted = !GL.IsBuffer(buffer);
            RequireContext();
            if (deleted) cache.ReconcileBufferDeletion(buffer);
        }
    }
    #endregion
}
