using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.BindBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BindBuffer(BufferTarget @target, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferBinding(@target);
        bool returned = false;
        try
        {
            GL.BindBuffer(@target, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BindBuffer(BufferTarget @target, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferBinding(@target);
        bool returned = false;
        try
        {
            GL.BindBuffer(@target, @buffer);
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
