using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Adapts legacy raw-handle GPU fixtures to program-owned numeric interfaces.</summary>
internal static class TestShaderInterfaces
{
    [ThreadStatic] private static Dictionary<int, VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract>? shaders;
    [ThreadStatic] private static Dictionary<int, GpuProgramInterface>? programs;
    private static Dictionary<int, VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract> Shaders => shaders ??= new();
    private static Dictionary<int, GpuProgramInterface> Programs => programs ??= new();

    [ThreadStatic] private static Dictionary<int, Action>? shaderReleases;
    [ThreadStatic] private static Dictionary<int, Action>? programReleases;

    #region Fixture ownership
    /// <summary>Notifies the allocating helper when another fixture releases its shader handle.</summary>
    public static void OnShaderRelease(int shader, Action release) => (shaderReleases ??= new()).Add(shader, release);
    /// <summary>Notifies the allocating helper before its program handle can be recycled.</summary>
    public static void OnProgramRelease(int program, Action release) => (programReleases ??= new()).Add(program, release);
    /// <summary>Retains fixture metadata until the raw shader handle is released.</summary>
    public static void TrackShader(int shader, VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract variant) => Shaders[shader] = variant;
    /// <summary>Records a production pipeline's existing interface for raw-handle fixture assertions.</summary>
    public static void TrackProgram(int program, GpuProgramInterface value) => Programs[program] = value;
    /// <summary>Links binary fixtures and creates the same interface snapshot as production.</summary>
    public static void LinkProgram(int program)
    {
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0) return;
        GL.GetProgram(program, GetProgramParameterName.AttachedShaders, out int count);
        int[] attached = new int[count]; GL.GetAttachedShaders(program, count, out _, attached);
        Programs[program] = new GpuProgramInterface(program, attached.Select(id => Shaders[id]));
    }
    /// <summary>Deletes a fixture shader and forgets its metadata.</summary>
    public static void DeleteShader(int shader)
    {
        Shaders.Remove(shader);
        if (shaderReleases?.Remove(shader, out var released) == true) released();
        GL.DeleteShader(shader);
    }
    /// <summary>Deletes a fixture program and forgets its metadata.</summary>
    public static void DeleteProgram(int program) { ForgetProgram(program); GL.DeleteProgram(program); }
    /// <summary>Forgets a production-owned fixture before its pipeline disposes.</summary>
    public static void ForgetProgram(int program)
    {
        Programs.Remove(program);
        if (programReleases?.Remove(program, out var released) == true) released();
    }
    /// <summary>Constructs a layout with the fixture program's interface for binding tests.</summary>
    public static GpuProgramLayout BuildLayout(int program)
    {
        var layout = new GpuProgramLayout { BinaryInterface = Programs[program] };
        layout.RebuildCache(program);
        return layout;
    }
    #endregion

    #region Assertions and uniform setters
    /// <summary>Looks up an explicit standalone location.</summary>
    public static int GetUniformLocation(int program, string name) => Programs[program].GetUniformLocation(name);
    /// <summary>Looks up a block index for fixture assertions.</summary>
    public static int GetUniformBlockIndex(int program, string name) => Programs[program].GetUniformBlockIndex(name);
    /// <summary>Names numeric resources only for fixture diagnostics.</summary>
    public static void GetProgramResourceName(int program, ProgramInterface kind, int index, int capacity, out int length, out string name)
        => Programs[program].GetProgramResourceName(kind, index, capacity, out length, out name);
    #endregion
}
