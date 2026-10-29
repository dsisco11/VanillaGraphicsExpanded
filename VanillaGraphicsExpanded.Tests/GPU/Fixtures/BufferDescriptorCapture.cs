using System;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts actual buffer descriptor and identity reads independently of manager reader assignment.</summary>
internal sealed unsafe class BufferDescriptorCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();
    internal int DescriptorReads { get; private set; }
    internal int IdentityReads { get; private set; }

    #region Internal API
    /// <summary>Decorates both named descriptor widths and the explicit object-probe entrypoint.</summary>
    internal BufferDescriptorCapture()
    {
        nint integers = native.Original("glGetNamedBufferParameteriv");
        nint longs = native.Original("glGetNamedBufferParameteri64v");
        nint identity = native.Original("glIsBuffer");
        native.Add("glGetNamedBufferParameteriv", new IntegerQuery((id, field, value) =>
        {
            DescriptorReads++;
            ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)integers)(id, field, value);
        }));
        native.Add("glGetNamedBufferParameteri64v", new LongQuery((id, field, value) =>
        {
            DescriptorReads++;
            ((delegate* unmanaged[Stdcall]<uint, int, long*, void>)longs)(id, field, value);
        }));
        native.Add("glIsBuffer", new IdentityQuery(id =>
        {
            IdentityReads++;
            return ((delegate* unmanaged[Stdcall]<uint, byte>)identity)(id);
        }));
        native.Install();
    }
    #endregion

    #region Public API
    /// <summary>Restores original driver loading without changing canonical resource facts.</summary>
    public void Dispose() => native.Dispose();
    #endregion

    #region Private
    /// <summary>Matches the 32-bit descriptor-output ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IntegerQuery(uint id, int field, int* value);
    /// <summary>Matches the 64-bit descriptor-output ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void LongQuery(uint id, int field, long* value);
    /// <summary>Matches GLboolean object-identity output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte IdentityQuery(uint id);
    #endregion
}
