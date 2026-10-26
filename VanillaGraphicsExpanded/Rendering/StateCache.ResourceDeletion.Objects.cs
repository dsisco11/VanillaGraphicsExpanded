using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Observes deletion for native resource kinds without existing texture or framebuffer binding retirement.</summary>
internal sealed partial class StateCache
{
    #region Internal API




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






    #endregion
}
