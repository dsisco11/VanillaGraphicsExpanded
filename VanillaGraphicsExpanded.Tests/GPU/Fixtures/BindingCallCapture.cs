using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts real selector and binding-query dispatch without depending on JIT-inlining of managed wrappers.</summary>
internal sealed unsafe class BindingCallCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();
    private readonly Dictionary<string, int> calls = [];

    #region Internal API
    /// <summary>Installs typed ABI counters on the native loader, preserving every original effect.</summary>
    internal BindingCallCapture()
    {
        try
        {
            nint original0 = native.Original("glDepthMask");
            native.Add("glDepthMask", new Call0((a0) =>
            {
                native.Observe(() => calls["DepthMask"] = calls.GetValueOrDefault("DepthMask") + 1);
                ((delegate* unmanaged[Stdcall]<byte, void>)original0)(a0);
            }));
            nint original1 = native.Original("glDepthFunc");
            native.Add("glDepthFunc", new Call1((a0) =>
            {
                native.Observe(() => calls["DepthFunc"] = calls.GetValueOrDefault("DepthFunc") + 1);
                ((delegate* unmanaged[Stdcall]<int, void>)original1)(a0);
            }));
            nint original2 = native.Original("glGetIntegerv");
            native.Add("glGetIntegerv", new Call2((a0, a1) =>
            {
                native.Observe(() => calls["GetInteger"] = calls.GetValueOrDefault("GetInteger") + 1);
                ((delegate* unmanaged[Stdcall]<int, int*, void>)original2)(a0, a1);
            }));
            nint original3 = native.Original("glBindVertexArray");
            native.Add("glBindVertexArray", new Call3((a0) =>
            {
                native.Observe(() => calls["BindVertexArray"] = calls.GetValueOrDefault("BindVertexArray") + 1);
                ((delegate* unmanaged[Stdcall]<uint, void>)original3)(a0);
            }));
            nint original4 = native.Original("glUseProgram");
            native.Add("glUseProgram", new Call4((a0) =>
            {
                native.Observe(() => calls["UseProgram"] = calls.GetValueOrDefault("UseProgram") + 1);
                ((delegate* unmanaged[Stdcall]<uint, void>)original4)(a0);
            }));
            nint original5 = native.Original("glBindProgramPipeline");
            native.Add("glBindProgramPipeline", new Call5((a0) =>
            {
                native.Observe(() => calls["BindProgramPipeline"] = calls.GetValueOrDefault("BindProgramPipeline") + 1);
                ((delegate* unmanaged[Stdcall]<uint, void>)original5)(a0);
            }));
            nint original6 = native.Original("glBindFramebuffer");
            native.Add("glBindFramebuffer", new Call6((a0, a1) =>
            {
                native.Observe(() => calls["BindFramebuffer"] = calls.GetValueOrDefault("BindFramebuffer") + 1);
                ((delegate* unmanaged[Stdcall]<int, uint, void>)original6)(a0, a1);
            }));
            nint original7 = native.Original("glBindBuffer");
            native.Add("glBindBuffer", new Call7((a0, a1) =>
            {
                native.Observe(() => calls["BindBuffer"] = calls.GetValueOrDefault("BindBuffer") + 1);
                ((delegate* unmanaged[Stdcall]<int, uint, void>)original7)(a0, a1);
            }));
            nint original8 = native.Original("glBindBufferBase");
            native.Add("glBindBufferBase", new Call8((a0, a1, a2) =>
            {
                native.Observe(() => calls["BindBufferBase"] = calls.GetValueOrDefault("BindBufferBase") + 1);
                ((delegate* unmanaged[Stdcall]<int, uint, uint, void>)original8)(a0, a1, a2);
            }));
            nint original9 = native.Original("glBindBufferRange");
            native.Add("glBindBufferRange", new Call9((a0, a1, a2, a3, a4) =>
            {
                native.Observe(() => calls["BindBufferRange"] = calls.GetValueOrDefault("BindBufferRange") + 1);
                ((delegate* unmanaged[Stdcall]<int, uint, uint, nint, nint, void>)original9)(a0, a1, a2, a3, a4);
            }));
            nint original10 = native.Original("glGetVertexArrayIndexediv");
            native.Add("glGetVertexArrayIndexediv", new Call10((a0, a1, a2, a3) =>
            {
                native.Observe(() => calls["GetVertexArrayIndexed"] = calls.GetValueOrDefault("GetVertexArrayIndexed") + 1);
                ((delegate* unmanaged[Stdcall]<uint, uint, int, int*, void>)original10)(a0, a1, a2, a3);
            }));
            nint original11 = native.Original("glGetVertexArrayIndexed64iv");
            native.Add("glGetVertexArrayIndexed64iv", new Call11((a0, a1, a2, a3) =>
            {
                native.Observe(() => calls["GetVertexArrayIndexed64"] = calls.GetValueOrDefault("GetVertexArrayIndexed64") + 1);
                ((delegate* unmanaged[Stdcall]<uint, uint, int, long*, void>)original11)(a0, a1, a2, a3);
            }));
            nint uniform = native.Original("glUniform1i");
            native.Add("glUniform1i", new Uniform((location, value) =>
            {
                native.Observe(() => calls["Uniform1"] = calls.GetValueOrDefault("Uniform1") + 1);
                ((delegate* unmanaged[Stdcall]<int, int, void>)uniform)(location, value);
            }));
            nint namedUniform = native.Original("glProgramUniform1i");
            native.Add("glProgramUniform1i", new NamedUniform((program, location, value) =>
            {
                native.Observe(() => calls["ProgramUniform1"] = calls.GetValueOrDefault("ProgramUniform1") + 1);
                ((delegate* unmanaged[Stdcall]<uint, int, int, void>)namedUniform)(program, location, value);
            }));
            native.Install();
        }
        catch { native.Dispose(); throw; }
    }
    /// <summary>Returns actual dispatches for the named OpenTK operation.</summary>
    internal int Calls(string name) => calls.GetValueOrDefault(name);
    /// <summary>Excludes setup and explicit verification operations from the next interval.</summary>
    internal void Reset() => calls.Clear();
    #endregion

    #region Public API
    /// <summary>Restores ordinary loading and reports any captured test assertion failure.</summary>
    public void Dispose() => native.Dispose();
    #endregion

    #region Private
    /// <summary>Preserves a scalar bound integer uniform ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Uniform(int location, int value);
    /// <summary>Preserves a scalar named integer uniform ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void NamedUniform(uint program, int location, int value);
    /// <summary>Preserves the native ABI of glDepthMask.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call0(byte a0);
    /// <summary>Preserves the native ABI of glDepthFunc.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call1(int a0);
    /// <summary>Preserves the native ABI of glGetIntegerv.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call2(int a0, int* a1);
    /// <summary>Preserves the native ABI of glBindVertexArray.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call3(uint a0);
    /// <summary>Preserves the native ABI of glUseProgram.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call4(uint a0);
    /// <summary>Preserves the native ABI of glBindProgramPipeline.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call5(uint a0);
    /// <summary>Preserves the native ABI of glBindFramebuffer.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call6(int a0, uint a1);
    /// <summary>Preserves the native ABI of glBindBuffer.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call7(int a0, uint a1);
    /// <summary>Preserves the native ABI of glBindBufferBase.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call8(int a0, uint a1, uint a2);
    /// <summary>Preserves the native ABI of glBindBufferRange.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call9(int a0, uint a1, uint a2, nint a3, nint a4);
    /// <summary>Preserves the native ABI of glGetVertexArrayIndexediv.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call10(uint a0, uint a1, int a2, int* a3);
    /// <summary>Preserves the native ABI of glGetVertexArrayIndexed64iv.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Call11(uint a0, uint a1, int a2, long* a3);
    #endregion
}
