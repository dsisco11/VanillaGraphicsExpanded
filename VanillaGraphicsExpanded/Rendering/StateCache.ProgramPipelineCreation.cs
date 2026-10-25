using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes ProgramPipeline creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests ProgramPipeline creation; configuration and use remain separate operations.</summary>
    internal GpuProgramPipelineMetadata? CreateProgramPipeline(out nint id)
    {
        int nativeId;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess) GL.CreateProgramPipelines(1, out nativeId);
        else nativeId = GL.GenProgramPipeline();
        id = nativeId;
        if (id == 0) return null;
        // Realize fallback names while preserving the caller's selection.
        if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            using var binding = BindProgramPipelineScope(nativeId);
        }
        return new GpuProgramPipelineMetadata();
    }
    #endregion
}
