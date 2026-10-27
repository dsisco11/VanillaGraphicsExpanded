using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts concrete selector and binding-read entry points without changing native behavior.</summary>
internal sealed class BindingCallCapture : IDisposable
{
    [ThreadStatic] private static BindingCallCapture? active;
    private readonly Harmony harmony = new("Tests.BindingCallCapture");
    private readonly Dictionary<string, int> calls = [];

    #region Public API
    /// <summary>Installs compiler-selected native observers after fixture initialization.</summary>
    internal BindingCallCapture()
    {
        Assert.Null(active);
        active = this;
        try
        {
            // Typed delegates keep overload selection checked by the compiler.
            Delegate[] methods = [
                (Func<GetPName, int>)GL.GetInteger,
                (IntegerQuery)GL.GetInteger,
                (Action<int>)GL.BindVertexArray,
                (Action<int>)OpenTK.Graphics.OpenGL4.GL.BindVertexArray,
                (Action<int>)OpenTK.Graphics.OpenGL4.GL.UseProgram,
                (Action<int>)OpenTK.Graphics.OpenGL4.GL.BindProgramPipeline,
                (Action<OpenTK.Graphics.OpenGL4.FramebufferTarget, int>)OpenTK.Graphics.OpenGL4.GL.BindFramebuffer,
                (Func<OpenTK.Graphics.OpenGL4.GetPName, int>)OpenTK.Graphics.OpenGL4.GL.GetInteger,
                (CoreIntegerQuery)OpenTK.Graphics.OpenGL4.GL.GetInteger,
                (Action<int>)GL.UseProgram,
                (Action<int>)GL.BindProgramPipeline,
                (Action<BufferTarget, int>)GL.BindBuffer,
                (Action<OpenTK.Graphics.OpenGL4.BufferTarget, int>)OpenTK.Graphics.OpenGL4.GL.BindBuffer,
                (Action<BufferRangeTarget, int, int>)GL.BindBufferBase,
                (Action<BufferRangeTarget, int, int, nint, nint>)GL.BindBufferRange,
                (Action<OpenTK.Graphics.OpenGL4.BufferRangeTarget, int, int>)OpenTK.Graphics.OpenGL4.GL.BindBufferBase,
                (Action<OpenTK.Graphics.OpenGL4.BufferRangeTarget, int, int, nint, nint>)OpenTK.Graphics.OpenGL4.GL.BindBufferRange,
                (Action<FramebufferTarget, int>)GL.BindFramebuffer,
                (VertexQuery)GL.GetVertexArrayIndexed,
                (VertexWideQuery)GL.GetVertexArrayIndexed64
            ];
            foreach (var method in methods)
                harmony.Patch(method.Method, prefix: new HarmonyMethod(typeof(BindingCallCapture), nameof(Observe)));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Returns calls to the selected native method during the current interval.</summary>
    internal int Calls(string method) => calls.GetValueOrDefault(method);

    /// <summary>Clears setup or explicit verification calls before the next interval.</summary>
    internal void Reset() => calls.Clear();

    /// <summary>Removes observers before cleanup or unrelated graphics work.</summary>
    public void Dispose() { harmony.UnpatchAll(harmony.Id); active = null; }
    #endregion

    #region Private
    /// <summary>Names the integer output query overload.</summary>
    private delegate void IntegerQuery(GetPName parameter, out int value);
    /// <summary>Names the core integer output query overload used by managed backend operations.</summary>
    private delegate void CoreIntegerQuery(OpenTK.Graphics.OpenGL4.GetPName parameter, out int value);
    /// <summary>Names the narrow indexed VAO query overload.</summary>
    private delegate void VertexQuery(int vao, int index, VertexArrayIndexedParameter parameter, out int value);
    /// <summary>Names the native-width indexed VAO query overload.</summary>
    private delegate void VertexWideQuery(int vao, int index, VertexArrayIndexed64Parameter parameter, out long value);

    /// <summary>Records a call before allowing the exact native operation to execute.</summary>
    private static void Observe(MethodBase __originalMethod)
    {
        if (active is { } capture)
            capture.calls[__originalMethod.Name] = capture.calls.GetValueOrDefault(__originalMethod.Name) + 1;
    }
    #endregion
}
