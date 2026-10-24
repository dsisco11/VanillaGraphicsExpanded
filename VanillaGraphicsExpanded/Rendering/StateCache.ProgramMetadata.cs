using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes native executable facts after managed program mutation commands.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Relinks a managed program and refreshes actual interface facts even when linking fails.</summary>
    internal void LinkProgram(GpuProgramObject program)
    {
        GL.LinkProgram(program.ProgramId);
        GpuResourceManager.ObserveWrapperMetadata(program, GpuProgramMetadataReader.Instance, GpuMetadataChange.Metadata);
    }
    #endregion
}
