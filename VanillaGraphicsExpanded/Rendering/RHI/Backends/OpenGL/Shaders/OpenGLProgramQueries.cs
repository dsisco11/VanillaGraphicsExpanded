using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes explicit external-program discovery for existing manager-backed interface preparation.</summary>
internal static class OpenGLProgramQueries
{
    #region Internal API
    /// <summary>Discovers a standalone external uniform location.</summary>
    internal static int GetUniformLocation(int program, string name) => Native.GL.GetUniformLocation(program, name);
    /// <summary>Discovers an external uniform-block index.</summary>
    internal static int GetUniformBlockIndex(int program, string name) => Native.GL.GetUniformBlockIndex(program, name);
    /// <summary>Discovers an external program resource index.</summary>
    internal static int GetProgramResourceIndex(int program, ProgramInterface kind, string name) => Native.GL.GetProgramResourceIndex(program, (Native.ProgramInterface)kind, name);
    /// <summary>Reads an external resource name with its caller-provided capacity.</summary>
    internal static void GetProgramResourceName(int program, ProgramInterface kind, int index, int capacity, out int length, out string name)
        => Native.GL.GetProgramResourceName(program, (Native.ProgramInterface)kind, index, capacity, out length, out name);
    /// <summary>Reads an external program interface's declared size or count.</summary>
    internal static void GetProgramInterface(int program, ProgramInterface kind, ProgramInterfaceParameter parameter, out int value)
        => Native.GL.GetProgramInterface(program, (Native.ProgramInterface)kind, (Native.ProgramInterfaceParameter)parameter, out value);
    /// <summary>Reads selected external resource properties without relinking or assigning values.</summary>
    internal static void GetProgramResource(int program, ProgramInterface kind, int index, int count, ProgramProperty[] properties, int capacity, out int length, int[] values)
    {
        var native = new Native.ProgramProperty[properties.Length];
        for (int i = 0; i < native.Length; i++) native[i] = (Native.ProgramProperty)properties[i];
        Native.GL.GetProgramResource(program, (Native.ProgramInterface)kind, index, count, native, capacity, out length, values);
    }
    /// <summary>Reads one external block property for explicit contract admission.</summary>
    internal static void GetActiveUniformBlock(int program, int block, ActiveUniformBlockParameter parameter, out int value)
        => Native.GL.GetActiveUniformBlock(program, block, (Native.ActiveUniformBlockParameter)parameter, out value);
    /// <summary>Reads one external standalone integer uniform value.</summary>
    internal static void GetUniform(int program, int location, out int value) => Native.GL.GetUniform(program, location, out value);
    #endregion
}
