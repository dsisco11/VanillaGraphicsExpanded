using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Arb.BindBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BindBuffer(BufferTargetArb @target, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferBinding((BufferTarget)@target);
        bool returned = false;
        try
        {
            GL.Arb.BindBuffer(@target, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BindBuffer(BufferTargetArb @target, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferBinding((BufferTarget)@target);
        bool returned = false;
        try
        {
            GL.Arb.BindBuffer(@target, @buffer);
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
