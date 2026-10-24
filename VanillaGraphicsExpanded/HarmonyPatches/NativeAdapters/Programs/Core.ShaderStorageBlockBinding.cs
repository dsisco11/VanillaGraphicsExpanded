using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.ShaderStorageBlockBinding overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ShaderStorageBlockBinding(int @program, int @storageBlockIndex, int @storageBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false);
        bool returned = false;
        try
        {
            GL.ShaderStorageBlockBinding(@program, @storageBlockIndex, @storageBlockBinding);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ShaderStorageBlockBinding(uint @program, uint @storageBlockIndex, uint @storageBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false);
        bool returned = false;
        try
        {
            GL.ShaderStorageBlockBinding(@program, @storageBlockIndex, @storageBlockBinding);
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
