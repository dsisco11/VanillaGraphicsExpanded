using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Arb.VertexAttribPointer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, IntPtr @pointer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(int @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, ref T5 @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, ref @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[,] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexAttribPointer<T5>(uint @index, int @size, VertexAttribPointerTypeArb @type, bool @normalized, int @stride, T5[] @pointer)
        where T5 : struct
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.Arb.VertexAttribPointer<T5>(@index, @size, @type, @normalized, @stride, @pointer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    #endregion
}
