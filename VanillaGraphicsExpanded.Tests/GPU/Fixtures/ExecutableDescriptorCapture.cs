using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts native stage and interface discovery independently of OpenTK wrapper inlining or bypass dispatch.</summary>
internal sealed unsafe class ExecutableDescriptorCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();

    /// <summary>Matches shader parameter and ordinary integer uniform output ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IntegerOutput(uint name, int parameter, int* value);
    /// <summary>Matches program interface enumeration ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void InterfaceOutput(uint name, int kind, int parameter, int* value);

    #region Internal API
    /// <summary>Counts fixed-stage discovery queries.</summary>
    internal int StageReads { get; private set; }
    /// <summary>Counts full-interface enumeration queries.</summary>
    internal int InterfaceReads { get; private set; }
    /// <summary>Counts sampler/image value getters.</summary>
    internal int UnitReads { get; private set; }
    /// <summary>Counts program status and completion outputs separately from interface discovery.</summary>
    internal int ProgramReads { get; private set; }

    /// <summary>Decorates actual native output calls while preserving their values and error behavior.</summary>
    internal ExecutableDescriptorCapture()
    {
        var shader = (delegate* unmanaged[Stdcall]<uint, int, int*, void>)native.Original("glGetShaderiv");
        var program = (delegate* unmanaged[Stdcall]<uint, int, int*, void>)native.Original("glGetProgramiv");
        native.Add("glGetProgramiv", new IntegerOutput((name, parameter, value) =>
        {
            program(name, parameter, value);
            ProgramReads++;
        }));
        native.Add("glGetShaderiv", new IntegerOutput((name, parameter, value) =>
        {
            shader(name, parameter, value);
            if (parameter == (int)ShaderParameter.ShaderType) StageReads++;
        }));
        var uniform = (delegate* unmanaged[Stdcall]<uint, int, int*, void>)native.Original("glGetUniformiv");
        native.Add("glGetUniformiv", new IntegerOutput((name, location, value) =>
        {
            uniform(name, location, value);
            UnitReads++;
        }));
        var query = (delegate* unmanaged[Stdcall]<uint, int, int, int*, void>)native.Original("glGetProgramInterfaceiv");
        native.Add("glGetProgramInterfaceiv", new InterfaceOutput((name, kind, parameter, value) =>
        {
            query(name, kind, parameter, value);
            InterfaceReads++;
        }));
        native.Install();
    }
    #endregion

    #region Public API
    /// <summary>Restores production binding installation after the measured native interval.</summary>
    public void Dispose() => native.Dispose();
    #endregion
}
