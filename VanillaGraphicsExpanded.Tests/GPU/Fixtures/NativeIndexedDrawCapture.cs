using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes indexed native submissions while preserving existing binding-table decorators.</summary>
internal sealed unsafe class NativeIndexedDrawCapture : IDisposable
{
    private static readonly List<OpenGLNativeSlot> roots = [];
    private readonly List<(nint[] Table, int Index, OpenGLNativeSlot Slot)> installed = [];
    private Action? onDraw;
    private Exception? failure;
    private int disposed;

    #region Public API
    /// <summary>Disables observation and restores only entries still owned by this scope.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref onDraw, null);
        foreach (var entry in installed)
            Interlocked.CompareExchange(ref entry.Table[entry.Index], entry.Slot.Original, entry.Slot.Callback);
        installed.Clear();
        if (failure is not null) throw new InvalidOperationException("Indexed draw observation failed.", failure);
    }
    #endregion

    #region Internal API
    /// <summary>Decorates only the indexed draw entry in each currently loaded binding root.</summary>
    internal NativeIndexedDrawCapture(Action observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        onDraw = observation;
        try
        {
            foreach (var root in new[] { typeof(OpenTK.Graphics.OpenGL.GL), typeof(OpenTK.Graphics.OpenGL4.GL) })
            {
                var captured = OpenGLBindingTable.For(root).Capture();
                int index = Array.IndexOf(captured.Names, "glDrawElementsInstancedBaseVertexBaseInstance");
                if (index < 0) throw new NotSupportedException("The installed binding root has no indexed submission entry.");
                if (installed.Any(entry => ReferenceEquals(entry.Table, captured.Pointers) && entry.Index == index)) continue;
                nint original = Volatile.Read(ref captured.Pointers[index]);
                var slot = new OpenGLNativeSlot(original, new Indexed((mode, count, type, offset, instances, baseVertex, baseInstance) =>
                {
                    // Forward the actual current entry, including any outer decorator, exactly once.
                    ((delegate* unmanaged[Stdcall]<int, int, int, nint, int, int, uint, void>)original)
                        (mode, count, type, offset, instances, baseVertex, baseInstance);
                    try { Volatile.Read(ref onDraw)?.Invoke(); }
                    catch (Exception error) { Interlocked.CompareExchange(ref failure, error, null); }
                }));
                // A previously fetched callback remains callable after scope disposal, without retaining the fixture action.
                lock (roots) roots.Add(slot);
                if (Interlocked.CompareExchange(ref captured.Pointers[index], slot.Callback, original) != original)
                    throw new InvalidOperationException("The indexed submission entry changed during observation admission.");
                installed.Add((captured.Pointers, index, slot));
            }
        }
        catch { Dispose(); throw; }
    }
    #endregion

    #region Private
    /// <summary>Matches the installed indexed-instanced native submission ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void Indexed(int mode, int count, int type, nint offset, int instances, int baseVertex, uint baseInstance);
    #endregion
}
