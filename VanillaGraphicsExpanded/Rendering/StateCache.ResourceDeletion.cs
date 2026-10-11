using System.Runtime.InteropServices;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Updates resource-specific binding snapshots after native deletion.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Buffers
    /// <summary>Deletes buffer storage and invalidates all assignments referencing its retired numeric name.</summary>
    internal void DeleteBuffer(int buffer)
    {
        GL.DeleteBuffer(buffer);
        ForgetDeletedBuffer(buffer);
    }


    /// <summary>Deletes the native buffer span and forgets only references to its nonzero names.</summary>
    internal void DeleteBuffers(int count, ref int buffers)
    {
        GL.DeleteBuffers(count, ref buffers);
        if (count <= 0) return;
        // The native ref overload already requires count contiguous names from the caller.
        foreach (int buffer in MemoryMarshal.CreateReadOnlySpan(ref buffers, count))
            ForgetDeletedBuffer(buffer);
    }

    #endregion

    #region Other resource bindings
    /// <summary>Forgets only the retired renderbuffer binding, including reuse during boundary cleanup.</summary>
    internal void DeleteRenderbuffer(int renderbuffer)
    {
        GL.DeleteRenderbuffer(renderbuffer);
        RecordBoundaryRetirement(EPipelineState.RenderbufferBinding, renderbuffer);
        if (renderbuffer != 0 && currentRenderbuffer == renderbuffer) currentRenderbuffer = 0;
    }

    /// <summary>Records implicit sampler unbinding only on slots referencing the deleted name.</summary>
    internal void DeleteSampler(int sampler)
    {
        GL.DeleteSampler(sampler);
        RecordBoundaryRetirement(EPipelineState.SamplerBindings, sampler);
        if (sampler == 0 || samplerBindingByUnit is null) return;
        for (int unit = 0; unit < samplerBindingByUnit.Length; unit++)
            if (samplerBindingByUnit[unit] == sampler) samplerBindingByUnit[unit] = 0;
    }

    /// <summary>Records implicit read/draw unbinding without losing the surviving direction.</summary>
    internal void DeleteFramebuffer(int framebuffer)
    {
        GL.DeleteFramebuffer(framebuffer);
        RecordBoundaryRetirement(EPipelineState.FramebufferBindings, framebuffer);
        if (framebuffer == 0) return;
        if (currentReadFramebuffer == framebuffer) currentReadFramebuffer = 0;
        if (currentDrawFramebuffer == framebuffer) currentDrawFramebuffer = 0;
        if (currentFramebuffer == framebuffer) currentFramebuffer = 0;
    }

    /// <summary>Removes only the deleted VAO's element association and resets selection when bound.</summary>
    internal void DeleteVertexArray(int array)
    {
        GL.DeleteVertexArray(array);
        RecordBoundaryRetirement(EPipelineState.VertexArray, array);
        if (array == 0) return;
        elementArrayBufferByVao.Remove(array);
        if (currentVao == array) currentVao = 0;
    }

    /// <summary>Marks a program for deletion without changing its current binding snapshot.</summary>
    internal void DeleteProgram(int program)
    {
        // A current program remains bound until a later UseProgram; deletion alone changes
        // neither the current executable nor the selected program-pipeline object.
        GL.DeleteProgram(program);
        RecordBoundaryRetirement(EPipelineState.Program, program);
        ProgramScopeTracker.MarkDeleted(program);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Forgets only references to a deleted buffer; other VAOs may retain attached storage.</summary>
    private void ForgetDeletedBuffer(int buffer)
    {
        RecordBoundaryRetirement(EPipelineState.BufferBindings, buffer);
        if (buffer == 0) return;
        foreach (var key in indexedBufferBindings.Where(pair => pair.Value.Buffer == buffer).Select(pair => pair.Key).ToArray())
            indexedBufferBindings.Remove(key);
        foreach (var key in bufferBindingByTarget.Where(pair => pair.Value == buffer).Select(pair => pair.Key).ToArray())
            bufferBindingByTarget.Remove(key);
        foreach (var key in elementArrayBufferByVao.Where(pair => pair.Value == buffer).Select(pair => pair.Key).ToArray())
            elementArrayBufferByVao.Remove(key);
    }
    #endregion
}
