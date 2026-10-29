using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts actual VAO descriptor reads across both OpenTK and saved-original backend routes.</summary>
internal sealed unsafe class GeometryDescriptorCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();
    internal int Reads { get; private set; }

    #region Internal API
    /// <summary>Decorates each descriptor shape formerly used by cold geometry certification.</summary>
    internal GeometryDescriptorCapture()
    {
        foreach (string symbol in new[] { "glGetVertexAttribiv", "glGetVertexArrayiv" })
        {
            nint original = native.Original(symbol);
            native.Add(symbol, new IntegerQuery((name, parameter, value) =>
            {
                Reads++;
                ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)original)(name, parameter, value);
            }));
        }
        nint indexed = native.Original("glGetVertexArrayIndexediv");
        native.Add("glGetVertexArrayIndexediv", new IndexedQuery((name, index, parameter, value) =>
        {
            Reads++;
            ((delegate* unmanaged[Stdcall]<uint, uint, int, int*, void>)indexed)(name, index, parameter, value);
        }));
        nint wide = native.Original("glGetVertexArrayIndexed64iv");
        native.Add("glGetVertexArrayIndexed64iv", new WideQuery((name, index, parameter, value) =>
        {
            Reads++;
            ((delegate* unmanaged[Stdcall]<uint, uint, int, long*, void>)wide)(name, index, parameter, value);
        }));
        native.Install();
    }
    #endregion

    #region Public API
    /// <summary>Restores ordinary driver loading without changing tracked resource facts.</summary>
    public void Dispose() => native.Dispose();
    #endregion

    #region Private
    /// <summary>Matches the named/attribute integer query ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IntegerQuery(uint name, int parameter, int* value);
    /// <summary>Matches named indexed integer descriptors.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IndexedQuery(uint name, uint index, int parameter, int* value);
    /// <summary>Matches native-width vertex-offset output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void WideQuery(uint name, uint index, int parameter, long* value);
    #endregion
}
