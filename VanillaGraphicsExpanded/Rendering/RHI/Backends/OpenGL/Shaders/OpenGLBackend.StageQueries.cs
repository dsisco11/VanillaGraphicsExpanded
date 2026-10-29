using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Provides core native query services for manager-owned external stage acquisition.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Reads fixed stage facts without repeating a validity query on an established native identity.</summary>
    public bool ReadShaderMetadata(nint id, out GpuShaderMetadata metadata)
    {
        RequireContext();
        metadata = default;
        if (id == 0 || id < int.MinValue || id > uint.MaxValue) return false;
        int deleting = -1, stage = 0;
        GL.GetShader(unchecked((int)id), ShaderParameter.DeleteStatus, out deleting);
        RequireContext();
        if (deleting != 0) return false;
        GL.GetShader(unchecked((int)id), ShaderParameter.ShaderType, out stage);
        RequireContext();
        if ((ShaderType)stage is not (ShaderType.VertexShader or ShaderType.FragmentShader or ShaderType.GeometryShader
            or ShaderType.TessControlShader or ShaderType.TessEvaluationShader or ShaderType.ComputeShader)) return false;
        metadata = new(OpenGLShaderValues.Stage((ShaderType)stage));
        return true;
    }
    #endregion
}
