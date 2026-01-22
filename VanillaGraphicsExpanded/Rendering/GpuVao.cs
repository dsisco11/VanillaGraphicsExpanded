using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL vertex array object (VAO).
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuVao : IDisposable
{
    private int vertexArrayId;
    private readonly string? debugName;
    private bool isDisposed;

    public int VertexArrayId => vertexArrayId;
    public string? DebugName => debugName;

    public bool IsDisposed => isDisposed;
    public bool IsValid => vertexArrayId != 0 && !isDisposed;

    private GpuVao(int vertexArrayId, string? debugName)
    {
        this.vertexArrayId = vertexArrayId;
        this.debugName = debugName;
    }

    public static GpuVao Create(string? debugName = null)
    {
        int id = GL.GenVertexArray();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenVertexArrays failed.");
        }

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray, id, debugName);
#endif

        return new GpuVao(id, debugName);
    }

    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to bind disposed or invalid VAO");
            return;
        }

        GL.BindVertexArray(vertexArrayId);
    }

    public void Unbind()
    {
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        if (vertexArrayId != 0)
        {
            GL.DeleteVertexArray(vertexArrayId);
            vertexArrayId = 0;
        }

        isDisposed = true;
    }
}

