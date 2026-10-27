using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL indirect command buffer.
/// Intended for <c>glDraw*Indirect</c> (GL_DRAW_INDIRECT_BUFFER) and <c>glDispatchComputeIndirect</c> (GL_DISPATCH_INDIRECT_BUFFER).
/// </summary>
internal sealed class GpuIndirectBuffer : GpuBufferObject
{
    /// <summary>Declares buffer policy before manager-owned native creation.</summary>
    internal GpuIndirectBuffer(BufferUsage usage, string? debugName, BufferRole role = BufferRole.IndirectDraw)
    {
        Role = role;
        allocationUsage = usage;
        this.debugName = debugName;
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

}
