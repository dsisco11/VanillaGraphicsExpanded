using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes completed native timestamp reads and optionally supplies a deterministic result sequence.</summary>
internal sealed unsafe class TimestampResultCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();
    private readonly List<(uint Query, ulong Native, ulong Returned)> results = [];

    #region Public API
    /// <summary>Restores native dispatch and reports any contained observation failure.</summary>
    public void Dispose() => native.Dispose();
    #endregion

    #region Internal API
    /// <summary>Exposes actual query identities, original driver values and values returned to the consumer.</summary>
    internal IReadOnlyList<(uint Query, ulong Native, ulong Returned)> Results => results;

    /// <summary>Forwards real result reads once, replacing only the requested completed values.</summary>
    internal TimestampResultCapture(params ulong[] completedValues)
    {
        nint original = native.Original("glGetQueryObjectui64v");
        native.Add("glGetQueryObjectui64v", new ReadResult((query, parameter, value) =>
        {
            ((delegate* unmanaged[Stdcall]<uint, int, ulong*, void>)original)(query, parameter, value);
            if (parameter != (int)GetQueryObjectParam.QueryResult) return;

            ulong observed = *value, returned = observed;
            // Availability remains native. An inactive, previously fetched callback preserves
            // the original result because Observe no longer executes its replacement action.
            native.Observe(() =>
            {
                int index = results.Count;
                if (index < completedValues.Length) returned = completedValues[index];
                results.Add((query, observed, returned));
            });
            *value = returned;
        }));
        native.Install();
    }
    #endregion

    #region Private
    /// <summary>Matches the native unsigned 64-bit query-result ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ReadResult(uint query, int parameter, ulong* value);
    #endregion
}
