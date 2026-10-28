using System;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains one immutable driver address and its ABI-compatible managed observation callback.</summary>
internal sealed class OpenGLNativeSlot
{
    private readonly Delegate callback;

    #region Internal API
    /// <summary>Gets the true driver entrypoint retained before this callback became visible.</summary>
    internal nint Original { get; }
    /// <summary>Gets the rooted reverse-P/Invoke thunk installed in OpenTK.</summary>
    internal nint Callback { get; }
    /// <summary>Roots the delegate for at least the lifetime of every published table entry.</summary>
    internal OpenGLNativeSlot(nint original, Delegate callback)
    {
        if (original == 0) throw new ArgumentException("A native driver address is required.", nameof(original));
        Original = original;
        this.callback = callback;
        Callback = Marshal.GetFunctionPointerForDelegate(callback);
    }

    #endregion
}
