using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ShaderStorageBlockBinding(int @program, int @storageBlockIndex, int @storageBlockBinding)
        => StateCache.Current.ExecuteCoreShaderStorageBlockBinding(@program, @storageBlockIndex, @storageBlockBinding);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ShaderStorageBlockBinding(uint @program, uint @storageBlockIndex, uint @storageBlockBinding)
        => StateCache.Current.ExecuteCoreShaderStorageBlockBinding(@program, @storageBlockIndex, @storageBlockBinding);
    #endregion
}
