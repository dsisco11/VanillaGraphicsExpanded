using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes program binary and interface-binding commands through canonical observation.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Executes the native command and publishes measured program facts to existing aliases.</summary>
    internal void ProgramBinary(int program, BinaryFormat format, nint data, int length)
    {
        var observation = BeginNativeProgramMetadata(program, true, false, -1, 1, executable: true);
        bool returned = false;
        try { GL.ProgramBinary(program, format, data, length); returned = true; }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes the native command and publishes measured program facts to existing aliases.</summary>
    internal void UniformBlockBinding(int program, int index, int binding)
    {
        var observation = BeginNativeProgramMetadata(program, true, false, -1, 1);
        bool returned = false;
        try { GL.UniformBlockBinding(program, index, binding); returned = true; }
        finally { observation.Complete(returned); }
    }

    /// <summary>Executes the native command and publishes measured program facts to existing aliases.</summary>
    internal void ShaderStorageBlockBinding(int program, int index, int binding)
    {
        var observation = BeginNativeProgramMetadata(program, true, false, -1, 1);
        bool returned = false;
        try { GL.ShaderStorageBlockBinding(program, index, binding); returned = true; }
        finally { observation.Complete(returned); }
    }

    #endregion
}
