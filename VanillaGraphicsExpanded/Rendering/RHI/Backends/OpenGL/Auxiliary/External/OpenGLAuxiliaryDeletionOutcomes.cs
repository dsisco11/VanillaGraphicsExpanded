using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Reads exact external outcomes through the same loader as the original native command.</summary>
internal static partial class OpenGLAuxiliaryDeletionCalls
{
    #region Private
    /// <summary>Preserves native-width sync identity and one kind-specific outcome read per nonzero name.</summary>
    private static bool WasDeletedExternal(GpuResourceKind kind, nint id)
    {
        if (id == 0) return true;
        return kind switch
        {
            GpuResourceKind.Query => !GL.IsQuery((int)id),
            GpuResourceKind.Sync => !GL.IsSync(id),
            GpuResourceKind.TransformFeedback => !GL.IsTransformFeedback((int)id),
            GpuResourceKind.Semaphore => !GL.Ext.IsSemaphore((int)id),
            GpuResourceKind.MemoryObject => !GL.Ext.IsMemoryObject((int)id),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
    #endregion
}
