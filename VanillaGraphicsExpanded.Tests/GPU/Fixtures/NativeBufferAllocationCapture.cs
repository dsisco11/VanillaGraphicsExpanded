using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes actual allocation dispatch for frozen-input and retirement tests.</summary>
internal sealed unsafe class NativeBufferAllocationCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();
    #region Internal API
    /// <summary>Decorates allocation while preserving raw arguments and the original driver effect.</summary>
    internal NativeBufferAllocationCapture(Action? before = null, Action? after = null)
    {
        nint original = native.Original("glNamedBufferData");
        native.Add("glNamedBufferData", new Allocate((buffer, size, data, usage) =>
        {
            if (before is not null) native.Observe(before);
            ((delegate* unmanaged[Stdcall]<uint, nint, nint, int, void>)original)(buffer, size, data, usage);
            if (after is not null) native.Observe(after);
        }));
        native.Install();
    }
    #endregion
    #region Public API
    /// <summary>Restores ordinary loading and reports contained test callback errors.</summary>
    public void Dispose() => native.Dispose();
    #endregion
    #region Private
    /// <summary>Matches native named allocation.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Allocate(uint buffer, nint size, nint data, int usage);
    #endregion
}
