using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires the immutable native stage of an adopted shader object.</summary>
internal sealed class GpuShaderMetadataReader : IGpuResourceMetadataReader<GpuShaderMetadata>
{
    internal static readonly GpuShaderMetadataReader Instance = new();

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && GL.IsShader(unchecked((int)id));

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuShaderMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        GL.GetShader(unchecked((int)id), ShaderParameter.DeleteStatus, out int deleting);
        if (deleting != 0) return false;
        GL.GetShader(unchecked((int)id), ShaderParameter.ShaderType, out int stage);
        if (stage == 0) return false;
        metadata = new((ShaderType)stage);
        return true;
    }
    #endregion
}
