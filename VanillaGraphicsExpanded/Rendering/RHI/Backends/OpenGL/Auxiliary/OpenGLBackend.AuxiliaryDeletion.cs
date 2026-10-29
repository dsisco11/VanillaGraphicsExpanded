using System;
using GL = OpenTK.Graphics.OpenGL4.GL;
using ExtGL = OpenTK.Graphics.OpenGL.GL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Deletes admitted original auxiliary identities through existing retirement publication.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Retires the admitted original Query allocation and publishes its actual outcome.</summary>
    internal void DeleteQuery(int id)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteQuery(id);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Query, id);
    }
    /// <summary>Retires the admitted original Sync allocation and publishes its actual outcome.</summary>
    internal void DeleteSync(nint id)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteSync(id);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Sync, id);
    }
    /// <summary>Retires the admitted original TransformFeedback allocation and publishes its actual outcome.</summary>
    internal void DeleteTransformFeedback(int id)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteTransformFeedback(id);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, id);
    }
    /// <summary>Retires the admitted original Semaphore allocation and publishes its actual outcome.</summary>
    internal void DeleteSemaphore(int id)
    {
        RequireContext();
        ExtGL.Ext.DeleteSemaphore(id);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, id);
    }
    /// <summary>Retires the admitted original MemoryObject allocation and publishes its actual outcome.</summary>
    internal void DeleteMemoryObject(int id)
    {
        RequireContext();
        OpenGLDriverCalls.Ext.DeleteMemoryObject(id);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, id);
    }
    #endregion
}
/// <summary>Resolves deletion outcomes without consuming the native error queue.</summary>
internal static partial class OpenGLAuxiliaryDeletionCalls
{
    #region Internal API
    /// <summary>Checks only kind-specific actual deletion after an admitted native call.</summary>
    internal static bool WasDeleted(GpuResourceKind kind, nint id) => kind switch
    {
        GpuResourceKind.Query => !GL.IsQuery((int)id),
        GpuResourceKind.Sync => !GL.IsSync(id),
        GpuResourceKind.TransformFeedback => !GL.IsTransformFeedback((int)id),
        GpuResourceKind.Semaphore => !ExtGL.Ext.IsSemaphore((int)id),
        GpuResourceKind.MemoryObject => !ExtGL.Ext.IsMemoryObject((int)id),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    #endregion
}
