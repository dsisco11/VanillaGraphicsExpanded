using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Observes deletion for native resource kinds without existing texture or framebuffer binding retirement.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Deletes a query name while preserving driver-managed active query references.</summary>
    internal void DeleteQuery(int id)
    {
        GL.DeleteQuery(id);
        CompleteResourceDeletion(GpuResourceKind.Query, id);
    }

    /// <summary>Deletes a transform feedback object through the common registry retirement route.</summary>
    internal void DeleteTransformFeedback(int id)
    {
        GL.DeleteTransformFeedback(id);
        CompleteResourceDeletion(GpuResourceKind.TransformFeedback, id);
    }

    /// <summary>Deletes a program pipeline through the common registry retirement route.</summary>
    internal void DeleteProgramPipeline(int id)
    {
        GL.DeleteProgramPipeline(id);
        CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, id);
    }

    /// <summary>Marks a shader deleted without claiming deferred native references have been reclaimed.</summary>
    internal void DeleteShader(int id)
    {
        GL.DeleteShader(id);
        CompleteResourceDeletion(GpuResourceKind.Shader, id);
    }

    /// <summary>Deletes a pointer-shaped sync identity without narrowing its native width.</summary>
    internal void DeleteSync(IntPtr id)
    {
        GL.DeleteSync(id);
        CompleteResourceDeletion(GpuResourceKind.Sync, (nint)id);
    }

    /// <summary>Deletes an EXT semaphore through the common registry retirement route.</summary>
    internal void DeleteSemaphore(int id)
    {
        GL.Ext.DeleteSemaphore(id);
        CompleteResourceDeletion(GpuResourceKind.Semaphore, id);
    }

    /// <summary>Deletes an EXT memory object through the common registry retirement route.</summary>
    internal void DeleteMemoryObject(int id)
    {
        GL.Ext.DeleteMemoryObject(id);
        CompleteResourceDeletion(GpuResourceKind.MemoryObject, id);
    }
    #endregion
}
