using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    /// <summary>Selects compatible persisted code without allocating or mutating a native executable.</summary>
    internal static RHIShaderBinary? Select(PreparedProgramBinary inputs)
    {
        LastLoadWasHit = false;
        if (inputs.Key == null || inputs.Store == null || !inputs.Store.TryRead(inputs.Key, out int format, out byte[] bytes))
            return null;
        if (!GpuSupport.Graphics.ProgramBinaryFormats.Contains(format))
        {
            inputs.Store.Remove(inputs.Key);
            return null;
        }
        return new(format, bytes);
    }

    /// <summary>Evicts a rejected persisted candidate before the asset owner requests fresh stage creation.</summary>
    internal static void Reject(PreparedProgramBinary inputs)
    {
        LastLoadWasHit = false;
        if (inputs.Key != null) inputs.Store?.Remove(inputs.Key);
    }

    /// <summary>Persists a ready executable through its original execution context without native cache policy.</summary>
    internal static void Save(RHICommandContext context, RHIShader shader, PreparedProgramBinary inputs)
    {
        if (inputs.Key == null || inputs.Store == null) return;
        // Extraction is optional. Persistence failure must never retire a successfully created executable.
        try
        {
            var binary = context.ExportShaderBinary(shader, ProgramBinaryStore.MaximumBinaryBytes);
            if (binary != null) inputs.Store.Write(inputs.Key, binary.NativeFormat, binary.Bytes.ToArray());
        }
        catch (Exception) { }
    }

    /// <summary>Captures shader inputs and includes the current driver and context profile in their identity.</summary>
    internal static PreparedProgramBinary Prepare(ShaderLoadPlan plan, ShaderAssetReader read,
        Func<ShaderBinaryDigest.Manifest?> digestIndex)
    {
        LastLoadWasHit = false;
        ProgramBinaryStore? store = null;
        string driver = string.Empty;
        if (currentOverride != null || !Bypass)
        {
            try
            {
                var capabilities = GpuSupport.Graphics;
                int formats = capabilities.ProgramBinaryFormats.Length;
                if (formats > 0)
                {
                    store = currentOverride != null ? currentOverride.Store : DefaultStore.Value;
                    driver = string.Join("\n", capabilities.VendorString, capabilities.RendererString,
                        capabilities.VersionString, capabilities.ShadingLanguageVersionString,
                        capabilities.ContextProfileMaskValue, capabilities.ContextFlags,
                        RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
                }
            }
            catch (Exception) { store = null; }
        }
        return new(plan, read, store, driver, digestIndex);
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
