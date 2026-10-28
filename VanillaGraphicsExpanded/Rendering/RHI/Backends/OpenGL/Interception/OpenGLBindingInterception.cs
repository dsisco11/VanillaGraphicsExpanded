using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using OpenTK;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns binding-table installation and original dispatch independently of caller assemblies.</summary>
internal static partial class OpenGLBindingInterception
{
    private static readonly object gate = new();
    private static readonly Dictionary<(string Name, nint Original), OpenGLNativeSlot> slots = [];
    private static readonly ConcurrentDictionary<nint, OpenGLNativeSlot> callbacks = [];
    private static readonly HashSet<(nint[] Table, int Index, OpenGLNativeSlot Slot)> installed = [];
    private static bool enabled;

    #region Internal API
    /// <summary>Allows post-call observation until uninstall; an already admitted observer may finish.</summary>
    internal static bool ObservationEnabled => Volatile.Read(ref enabled);

    /// <summary>Wraps a loader so each selected slot is published as a callback without an unhooked interval.</summary>
    internal static IBindingsContext ObserveLoader(IBindingsContext loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new ObservedBindings(loader);
    }

    /// <summary>Installs supported callbacks into bindings already loaded before the rendering owner starts.</summary>
    internal static void Install()
    {
        lock (gate)
        {
            Volatile.Write(ref enabled, true);
            InstallTable(OpenGLBindingTable.For(typeof(OpenTK.Graphics.OpenGL.GL)));
            InstallTable(OpenGLBindingTable.For(typeof(OpenTK.Graphics.OpenGL4.GL)));
        }
    }

    /// <summary>Records ownership after a completed or partially failed loader, including replacement arrays.</summary>
    internal static void BindingsLoaded(Type root)
    {
        lock (gate)
        {
            var table = OpenGLBindingTable.For(root);
            if (enabled) InstallTable(table);
            else RestoreTable(table);
        }
    }

    /// <summary>Returns a true original address without temporarily exposing unhooked table entries.</summary>
    internal static nint Original(nint pointer)
    {
        return callbacks.TryGetValue(pointer, out var slot) ? slot.Original : pointer;
    }

    /// <summary>Stops new installation and restores only entries still owned by this observer.</summary>
    internal static void Uninstall()
    {
        lock (gate)
        {
            Volatile.Write(ref enabled, false);
            // Restore only our entries. A caller that already fetched a callback can still
            // forward through its immutable original; the dictionaries retain its delegate.
            foreach (var item in installed)
                Interlocked.CompareExchange(ref item.Table[item.Index], item.Slot.Original, item.Slot.Callback);
            installed.Clear();
        }
    }
    #endregion

    #region Private
    /// <summary>Atomically replaces loaded entries without overwriting a later loader or another component.</summary>
    private static void InstallTable(OpenGLBindingTable binding)
    {
        var captured = binding.Capture();
        for (int index = 0; index < captured.Pointers.Length; index++)
        {
            nint previous = Volatile.Read(ref captured.Pointers[index]);
            nint replacement = ObserveAddress(captured.Names[index], previous);
            if (replacement == previous)
            {
                if (callbacks.TryGetValue(previous, out var owned))
                    installed.Add((captured.Pointers, index, owned));
                continue;
            }
            if (Interlocked.CompareExchange(ref captured.Pointers[index], replacement, previous) == previous)
                installed.Add((captured.Pointers, index, callbacks[replacement]));
        }
    }

    /// <summary>Removes owned callbacks published by a loader that finished after observation was disabled.</summary>
    private static void RestoreTable(OpenGLBindingTable binding)
    {
        // A loader may have already fetched a callback before uninstall. Its finalizer reconciles
        // those later writes without waiting for dispatch, tracking in-flight calls or replacing other owners.
        var captured = binding.Capture();
        for (int index = 0; index < captured.Pointers.Length; index++)
        {
            nint pointer = Volatile.Read(ref captured.Pointers[index]);
            if (callbacks.TryGetValue(pointer, out var slot))
                Interlocked.CompareExchange(ref captured.Pointers[index], slot.Original, pointer);
        }
    }

    /// <summary>Reuses immutable callbacks for repeated loads of the same driver address and operation.</summary>
    private static nint ObserveAddress(string name, nint original)
    {
        lock (gate)
        {
            if (!enabled || original == 0) return original;
            if (callbacks.TryGetValue(original, out var existing)) return existing.Callback;
            var key = (name, original);
            if (!slots.TryGetValue(key, out var slot))
            {
                slot = CreateSlot(name, original);
                if (slot is null) return original;
                slots.Add(key, slot);
                if (!callbacks.TryAdd(slot.Callback, slot))
                    throw new InvalidOperationException("A native observation callback was registered twice.");
            }
            return slot.Callback;
        }
    }

    /// <summary>Substitutes callbacks while OpenTK itself retains ownership of name loading and table assignment.</summary>
    private sealed class ObservedBindings(IBindingsContext original) : IBindingsContext
    {
        #region Public API
        /// <inheritdoc />
        public nint GetProcAddress(string procName) => ObserveAddress(procName, original.GetProcAddress(procName));
        #endregion
    }
    #endregion
}
