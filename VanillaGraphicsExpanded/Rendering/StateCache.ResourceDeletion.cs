using System.Runtime.InteropServices;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Updates resource-specific binding snapshots after native deletion.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Other resource bindings




    /// <summary>Records implicit read/draw unbinding without losing the surviving direction.</summary>
    internal void DeleteFramebuffer(int framebuffer)
    {
        RequireMutableFramebufferConfiguration(framebuffer);
        GL.DeleteFramebuffer(framebuffer);
        CompleteResourceDeletion(GpuResourceKind.Framebuffer, framebuffer);
    }

    /// <summary>Removes only the deleted VAO's element association and resets selection when bound.</summary>
    internal void DeleteVertexArray(int array)
    {
        GL.DeleteVertexArray(array);
        CompleteResourceDeletion(GpuResourceKind.VertexArray, array);
    }

    /// <summary>Marks a program for deletion without changing its current binding snapshot.</summary>
    internal void DeleteProgram(int program)
    {
        GL.DeleteProgram(program);
        CompleteResourceDeletion(GpuResourceKind.Program, program);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Forgets only references to a deleted buffer; other VAOs may retain attached storage.</summary>
    private void ForgetDeletedBuffer(int buffer)
    {
        RecordBoundaryRetirement(EPipelineState.BufferBindings, buffer);
        if (buffer == 0) return;
        VertexConfigurationEpoch++;
        foreach (var key in indexedBufferBindings.Where(pair => pair.Value.Buffer == buffer).Select(pair => pair.Key).ToArray())
            indexedBufferBindings.Remove(key);
        foreach (var key in bufferBindingByTarget.Where(pair => pair.Value == buffer).Select(pair => pair.Key).ToArray())
            bufferBindingByTarget.Remove(key);
        foreach (var key in elementArrayBufferByVao.Where(pair => pair.Value == buffer).Select(pair => pair.Key).ToArray())
            elementArrayBufferByVao.Remove(key);
    }
    #endregion
}
