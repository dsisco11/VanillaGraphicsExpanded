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
    private static readonly System.Threading.AsyncLocal<Override?> currentOverride = new();
    private static readonly object testStoreGate = new();
    private static readonly System.Collections.Generic.List<DefaultOverride> testStores = [];
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
        var (store, driver) = CaptureStore();
        return new(plan, read, store, driver, digestIndex);
    }

    /// <summary>Captures the effective store and driver identity for asset and generated-fixture executables.</summary>
    internal static (ProgramBinaryStore? Store, string Driver) CaptureStore()
    {
        ProgramBinaryStore? store = null;
        string driver = string.Empty;
        try
        {
            var capabilities = GpuSupport.Graphics;
            if (capabilities.ProgramBinaryFormats.Length > 0)
            {
                // Explicit per-suite scopes take priority. The fixture default spans runner threads,
                // contains bytes only, and is removed independently of the thread doing teardown.
                lock (testStoreGate)
                    store = currentOverride.Value != null ? currentOverride.Value.Store :
                        testStores.Count > 0 ? testStores[^1].Store : DefaultStore.Value;
                driver = string.Join("\n", capabilities.VendorString, capabilities.RendererString,
                    capabilities.VersionString, capabilities.ShadingLanguageVersionString,
                    capabilities.ContextProfileMaskValue, capabilities.ContextFlags,
                    RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
            }
        }
        catch (Exception) { store = null; }
        return (store, driver);
    }

    #endregion

    #region Test isolation
    /// <summary>Overrides storage in the caller's execution flow; null explicitly bypasses all cache storage.</summary>
    internal static IDisposable UseStoreForTesting(ProgramBinaryStore? store) => new Override(store);

    /// <summary>Installs a fixture-owned default across runner threads without replacing explicit suite policies.</summary>
    internal static IDisposable UseDefaultStoreForTesting(ProgramBinaryStore store) => new DefaultOverride(store);

    /// <summary>Retains a byte-store policy until its fixture retires, including out-of-order fixture teardown.</summary>
    private sealed class DefaultOverride : IDisposable
    {
        internal readonly ProgramBinaryStore Store;
        /// <summary>Registers an isolated fixture default before any shader preparation.</summary>
        internal DefaultOverride(ProgramBinaryStore store)
        {
            Store = store;
            lock (testStoreGate) testStores.Add(this);
        }
        /// <summary>Removes this lease without changing other active fixture defaults.</summary>
        public void Dispose() { lock (testStoreGate) testStores.Remove(this); }
    }

    /// <summary>Restores the preceding flow-local cache policy after a focused test.</summary>
    private sealed class Override : IDisposable
    {
        internal readonly ProgramBinaryStore? Store;
        private readonly Override? previous;
        /// <summary>Installs one scoped policy which follows the suite across awaited runner-thread changes.</summary>
        internal Override(ProgramBinaryStore? store) { Store = store; previous = currentOverride.Value; currentOverride.Value = this; }
        /// <summary>Restores the outer policy.</summary>
        public void Dispose() => currentOverride.Value = previous;
    }
    #endregion
}
