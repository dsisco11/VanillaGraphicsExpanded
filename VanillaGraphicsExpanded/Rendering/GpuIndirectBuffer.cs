using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL indirect command buffer.
/// Intended for <c>glDraw*Indirect</c> (GL_DRAW_INDIRECT_BUFFER) and <c>glDispatchComputeIndirect</c> (GL_DISPATCH_INDIRECT_BUFFER).
/// </summary>
internal sealed class GpuIndirectBuffer : GpuBufferObject
{
    private GpuIndirectBuffer(int bufferId, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        target = BufferTarget.DrawIndirectBuffer;
        this.usage = usage;
        this.debugName = debugName;
    }

    /// <summary>
    /// Creates a new indirect command buffer object name and wraps it in an RAII instance.
    /// </summary>
    public static GpuIndirectBuffer Create(
        BufferUsageHint usage = BufferUsageHint.DynamicDraw,
        string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return new GpuIndirectBuffer(id, usage, debugName);
    }

    /// <summary>
    /// Binds this buffer as the draw-indirect buffer (GL_DRAW_INDIRECT_BUFFER).
    /// </summary>
    public void BindDraw()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuIndirectBuffer] Attempted to bind disposed or invalid buffer");
            return;
        }

        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, bufferId);
    }

    /// <summary>
    /// Binds this buffer as the dispatch-indirect buffer (GL_DISPATCH_INDIRECT_BUFFER).
    /// </summary>
    public void BindDispatch()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuIndirectBuffer] Attempted to bind disposed or invalid buffer");
            return;
        }

        GL.BindBuffer(BufferTarget.DispatchIndirectBuffer, bufferId);
    }

    /// <summary>
    /// Binds this buffer as the draw-indirect buffer and returns a scope that restores the previous binding.
    /// </summary>
    public IndirectBindingScope BindDrawScope()
    {
        int previous = 0;
        try { GL.GetInteger(GetPName.DrawIndirectBufferBinding, out previous); } catch { }

        BindDraw();
        return new IndirectBindingScope(BufferTarget.DrawIndirectBuffer, previous);
    }

    /// <summary>
    /// Binds this buffer as the dispatch-indirect buffer and returns a scope that restores the previous binding.
    /// </summary>
    public IndirectBindingScope BindDispatchScope()
    {
        int previous = 0;
        try { GL.GetInteger(GetPName.DispatchIndirectBufferBinding, out previous); } catch { }

        BindDispatch();
        return new IndirectBindingScope(BufferTarget.DispatchIndirectBuffer, previous);
    }

    /// <summary>
    /// Uploads a single <see cref="DrawArraysIndirectCommand"/> command at offset 0.
    /// </summary>
    public void UploadDrawArraysCommand(in DrawArraysIndirectCommand cmd)
    {
        if (!IsValid)
        {
            return;
        }

        Span<DrawArraysIndirectCommand> tmp = stackalloc DrawArraysIndirectCommand[1];
        tmp[0] = cmd;
        UploadData((ReadOnlySpan<DrawArraysIndirectCommand>)tmp);
    }

    /// <summary>
    /// Uploads a single <see cref="DrawElementsIndirectCommand"/> command at offset 0.
    /// </summary>
    public void UploadDrawElementsCommand(in DrawElementsIndirectCommand cmd)
    {
        if (!IsValid)
        {
            return;
        }

        Span<DrawElementsIndirectCommand> tmp = stackalloc DrawElementsIndirectCommand[1];
        tmp[0] = cmd;
        UploadData((ReadOnlySpan<DrawElementsIndirectCommand>)tmp);
    }

    /// <summary>
    /// Uploads a single <see cref="DispatchIndirectCommand"/> command at offset 0.
    /// </summary>
    public void UploadDispatchCommand(in DispatchIndirectCommand cmd)
    {
        if (!IsValid)
        {
            return;
        }

        Span<DispatchIndirectCommand> tmp = stackalloc DispatchIndirectCommand[1];
        tmp[0] = cmd;
        UploadData((ReadOnlySpan<DispatchIndirectCommand>)tmp);
    }

    /// <summary>
    /// DrawArraysIndirect command layout (matches OpenGL spec).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DrawArraysIndirectCommand
    {
        public readonly uint Count;
        public readonly uint InstanceCount;
        public readonly uint First;
        public readonly uint BaseInstance;

        public DrawArraysIndirectCommand(uint count, uint instanceCount, uint first, uint baseInstance)
        {
            Count = count;
            InstanceCount = instanceCount;
            First = first;
            BaseInstance = baseInstance;
        }
    }

    /// <summary>
    /// DrawElementsIndirect command layout (matches OpenGL spec).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DrawElementsIndirectCommand
    {
        public readonly uint Count;
        public readonly uint InstanceCount;
        public readonly uint FirstIndex;
        public readonly int BaseVertex;
        public readonly uint BaseInstance;

        public DrawElementsIndirectCommand(uint count, uint instanceCount, uint firstIndex, int baseVertex, uint baseInstance)
        {
            Count = count;
            InstanceCount = instanceCount;
            FirstIndex = firstIndex;
            BaseVertex = baseVertex;
            BaseInstance = baseInstance;
        }
    }

    /// <summary>
    /// DispatchComputeIndirect command layout (matches OpenGL spec).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DispatchIndirectCommand
    {
        public readonly uint NumGroupsX;
        public readonly uint NumGroupsY;
        public readonly uint NumGroupsZ;

        public DispatchIndirectCommand(uint numGroupsX, uint numGroupsY, uint numGroupsZ)
        {
            NumGroupsX = numGroupsX;
            NumGroupsY = numGroupsY;
            NumGroupsZ = numGroupsZ;
        }
    }

    /// <summary>
    /// Scope that restores the previous indirect-buffer binding when disposed.
    /// </summary>
    public readonly struct IndirectBindingScope : IDisposable
    {
        private readonly BufferTarget target;
        private readonly int previous;

        public IndirectBindingScope(BufferTarget target, int previous)
        {
            this.target = target;
            this.previous = previous;
        }

        public void Dispose()
        {
            try { GL.BindBuffer(target, previous); } catch { }
        }
    }
}
