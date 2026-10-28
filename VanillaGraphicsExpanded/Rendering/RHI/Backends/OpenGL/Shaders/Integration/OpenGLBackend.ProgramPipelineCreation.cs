using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes explicit integration-owned native program pipelines using the admitted core API.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Creates an initialized native pipeline name without binding it or querying unrepresented configuration.</summary>
    public GpuProgramPipelineMetadata? InitializeProgramPipeline(out nint id)
    {
        RequireContext();
        GL.CreateProgramPipelines(1, out int name);
        if (name != 0) cache.RecordPipelineCreated(name);
        id = name;
        RequireContext();
        return name == 0 ? null : new GpuProgramPipelineMetadata();
    }
    #endregion
}
