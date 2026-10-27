using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Exports opaque executable bytes without owning cache selection, persistence or fallback policy.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public unsafe RHIShaderBinary? ExportShaderBinary(RHIShaderRef shader, int maximumBytes)
    {
        RequireContext();
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var owner = shader.RequireShader();
        if (!owner.Description.RetrievableBinary) return null;
        GL.GetProgram(owner.Program.ProgramId, GetProgramParameterName.ProgramBinaryLength, out int length);
        RequireContext();
        owner.RequireUsable();
        if (length <= 0 || length > maximumBytes) return null;
        byte[] bytes = new byte[length];
        int actual;
        BinaryFormat format;
        fixed (byte* pointer = bytes)
            GL.GetProgramBinary(owner.Program.ProgramId, length, out actual, out format, (nint)pointer);
        RequireContext();
        owner.RequireUsable();
        if (actual <= 0 || actual > bytes.Length) return null;
        return new((int)format, bytes.AsSpan(0, actual));
    }
    #endregion
}
