using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayElementBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayElementBuffer(int @vaobj, int @buffer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, true);
        bool returned = false;
        try
        {
            GL.VertexArrayElementBuffer(@vaobj, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayElementBuffer(uint @vaobj, uint @buffer)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, true);
        bool returned = false;
        try
        {
            GL.VertexArrayElementBuffer(@vaobj, @buffer);
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
