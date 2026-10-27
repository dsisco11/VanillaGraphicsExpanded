using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reads only externally altered executable fields selected by manager completion.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public int ReadExternalProgramUnit(int program, int location)
    {
        RequireContext();
        GL.GetUniform(program, location, out int value);
        RequireContext();
        return value;
    }
    /// <inheritdoc />
    public bool ReadExternalProgramBlockBinding(int program, ProgramInterface block, int index, out int value)
    {
        RequireContext();
        int[] values = [-1];
        GL.GetProgramResource(program, (Native.ProgramInterface)block, index, 1,
            [Native.ProgramProperty.BufferBinding], 1, out int written, values);
        RequireContext();
        value = values[0];
        return written == 1 && value >= 0;
    }
    #endregion
}
