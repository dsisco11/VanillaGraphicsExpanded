using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using Vintagestory.API.Config;

namespace VanillaGraphicsExpanded.Rendering.ProgramBinaries;

/// <summary>Coordinates optional driver executable loading without owning installed programs or their binding contracts.</summary>
internal static class DriverProgramCache
{
    private static readonly Lazy<ProgramBinaryStore> DefaultStore = new(() => new(Path.Combine(GamePaths.DataPath, "VGE", "Cache", "Programs")));
    [ThreadStatic] internal static bool Bypass;
    [ThreadStatic] private static Override? currentOverride;
    /// <summary>Reports whether the most recent attempt on this GL thread loaded a cached executable.</summary>
    [ThreadStatic] internal static bool LastLoadWasHit;

    #region Executable cache
    /// <summary>Captures shader inputs and includes the current driver and context profile in their identity.</summary>
    internal static PreparedProgramBinary Prepare(ShaderLoadPlan plan, ShaderAssetReader read,
        Func<ShaderBinaryDigest.Manifest?> digestIndex)
    {
        LastLoadWasHit = false;
        ProgramBinaryStore? store = null;
        string driver = string.Empty;
        if (currentOverride != null || !Bypass)
        {
            GlDebug.ThrowIfErrors("Before program binary capability query");
            try
            {
                GL.GetInteger(GetPName.NumProgramBinaryFormats, out int formats);
                if (formats > 0)
                {
                    store = currentOverride != null ? currentOverride.Store : DefaultStore.Value;
                    driver = string.Join("\n", GL.GetString(StringName.Vendor), GL.GetString(StringName.Renderer),
                        GL.GetString(StringName.Version), GL.GetString(StringName.ShadingLanguageVersion),
                        GL.GetInteger(GetPName.ContextProfileMask), GL.GetInteger(GetPName.ContextFlags),
                        RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
                }
            }
            catch (Exception) { store = null; }
            if (GlDebug.GetErrors().Length != 0) store = null;
        }
        return new(plan, read, store, driver, digestIndex);
    }

    /// <summary>Loads a validated binary into a candidate; the caller owns and discards a rejected candidate.</summary>
    internal static bool TryLoad(int program, PreparedProgramBinary inputs)
    {
        if (inputs.Key == null || inputs.Store == null || !inputs.Store.TryRead(inputs.Key, out int format, out byte[] bytes)) return false;
        GlDebug.ThrowIfErrors("Before cached program load");
        try
        {
            // Reject unavailable formats before calling the driver, avoiding GL_INVALID_ENUM on stale metadata.
            GL.GetInteger(GetPName.NumProgramBinaryFormats, out int count);
            int[] formats = new int[count];
            GL.GetInteger(GetPName.ProgramBinaryFormats, formats);
            if (Array.IndexOf(formats, format) < 0) { inputs.Store.Remove(inputs.Key); return false; }
            unsafe
            {
                fixed (byte* pointer = bytes) GL.ProgramBinary(program, (BinaryFormat)format, (IntPtr)pointer, bytes.Length);
            }
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (GlDebug.GetErrors().Length != 0 || linked == 0) { inputs.Store.Remove(inputs.Key); return false; }
            LastLoadWasHit = true;
            return true;
        }
        catch (Exception)
        {
            inputs.Store.Remove(inputs.Key);
            return false;
        }
        finally { GlDebug.ClearErrors(); }
    }

    /// <summary>Requests retrievable executable data only when caching is available.</summary>
    internal static void RequestRetrievable(int program, bool enabled)
    {
        if (!enabled) return;
        GlDebug.ThrowIfErrors("Before program binary retrieval hint");
        try { GL.ProgramParameter(program, ProgramParameterName.ProgramBinaryRetrievableHint, 1); }
        catch (Exception) { }
        GlDebug.ClearErrors();
    }

    /// <summary>Extracts a successfully linked executable; extraction or persistence failures leave it usable.</summary>
    internal static void Save(int program, PreparedProgramBinary inputs)
    {
        if (inputs.Key == null || inputs.Store == null) return;
        GlDebug.ThrowIfErrors("Before program binary extraction");
        try
        {
            GL.GetProgram(program, GetProgramParameterName.ProgramBinaryLength, out int length);
            if (length <= 0 || length > ProgramBinaryStore.MaximumBinaryBytes) return;
            byte[] bytes = new byte[length];
            unsafe
            {
                fixed (byte* pointer = bytes)
                {
                    GL.GetProgramBinary(program, length, out int actual, out BinaryFormat format, (IntPtr)pointer);
                    if (GlDebug.GetErrors().Length != 0 || actual <= 0 || actual > length) return;
                    if (actual != length) Array.Resize(ref bytes, actual);
                    inputs.Store.Write(inputs.Key, (int)format, bytes);
                }
            }
        }
        catch (Exception) { /* Extraction is optional; retain the successfully linked executable. */ }
        finally { GlDebug.ClearErrors(); }
    }
    #endregion

    #region Test isolation
    /// <summary>Overrides storage on the calling GL thread; null explicitly bypasses the disk cache.</summary>
    internal static IDisposable UseStoreForTesting(ProgramBinaryStore? store) => new Override(store);

    /// <summary>Restores the preceding thread-local cache policy after a focused test.</summary>
    private sealed class Override : IDisposable
    {
        internal readonly ProgramBinaryStore? Store;
        private readonly Override? previous;
        /// <summary>Installs one scoped policy without changing other GL threads.</summary>
        internal Override(ProgramBinaryStore? store) { Store = store; previous = currentOverride; currentOverride = this; }
        /// <summary>Restores the outer policy.</summary>
        public void Dispose() => currentOverride = previous;
    }
    #endregion
}
