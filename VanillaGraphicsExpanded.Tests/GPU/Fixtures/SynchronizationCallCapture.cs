using System;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts actual query/fence factories and validity reads across ordinary and saved-original dispatch.</summary>
internal sealed unsafe class SynchronizationCallCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();
    internal int QueryCreations { get; private set; }
    internal int FenceCreations { get; private set; }
    internal int IdentityReads { get; private set; }

    #region Internal API
    /// <summary>Decorates real driver addresses without replacing return values or consuming native errors.</summary>
    internal SynchronizationCallCapture()
    {
        nint query = native.Original("glCreateQueries"), fence = native.Original("glFenceSync");
        nint isQuery = native.Original("glIsQuery"), isSync = native.Original("glIsSync");
        native.Add("glCreateQueries", new Create((target, count, names) =>
        {
            QueryCreations++;
            ((delegate* unmanaged[Stdcall]<int, int, uint*, void>)query)(target, count, names);
        }));
        native.Add("glFenceSync", new Fence((condition, flags) =>
        {
            FenceCreations++;
            return ((delegate* unmanaged[Stdcall]<int, int, nint>)fence)(condition, flags);
        }));
        native.Add("glIsQuery", new IsQuery(name =>
        {
            IdentityReads++;
            return ((delegate* unmanaged[Stdcall]<uint, byte>)isQuery)(name);
        }));
        native.Add("glIsSync", new IsSync(name =>
        {
            IdentityReads++;
            return ((delegate* unmanaged[Stdcall]<nint, byte>)isSync)(name);
        }));
        native.Install();
    }
    #endregion
    #region Public API
    /// <summary>Restores ordinary loading while retaining originals for already fetched callbacks.</summary>
    public void Dispose() => native.Dispose();
    #endregion
    #region Private
    /// <summary>Matches target-specific counted query creation.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Create(int target, int count, uint* names);
    /// <summary>Preserves native pointer-width sync results.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate nint Fence(int condition, int flags);
    /// <summary>Matches integer-name validity results.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte IsQuery(uint name);
    /// <summary>Matches pointer-name validity results.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte IsSync(nint name);
    #endregion
}
