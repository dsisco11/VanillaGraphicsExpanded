using System;
using System.Numerics;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Selects atmospheric transport once per resource generation and retains asynchronous CPU fallback.</summary>
internal sealed class AtmosphereBackend : IDisposable
{
    private readonly ICoreAPI api;
    private AtmosphereComputation? cpu;
    private AtmosphereGpuComputation? gpu;
    private bool selected;
    private bool disposed;

    /// <summary>Exposes the CPU completion dependency for diagnostics without transferring publication ownership.</summary>
    internal Task<AtmosphereLighting>? PendingCpu => cpu?.Pending;

    /// <summary>Retains asset access without creating resources before a render context exists.</summary>
    internal AtmosphereBackend(ICoreAPI api) => this.api = api;

    #region Scheduling
    /// <summary>Admits work to one backend; initialization or execution failure falls back without clearing display.</summary>
    internal AtmosphereLighting? Update(Vector3 sun, float altitude, float clouds, int width, int height, int quality)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!selected)
        {
            selected = true;
            if (GpuSupport.TryInitialize() && AtmosphereGpuComputation.Supported)
            {
                try { gpu = AtmosphereGpuComputation.Create(api); }
                catch (Exception ex) { api.Logger.Warning("Atmosphere GPU initialization failed; using CPU transport: {0}", ex.Message); }
            }
            if (gpu is null) cpu = new();
        }
        if (gpu is not null)
        {
            try { return gpu.Update(sun, altitude, clouds, width, height, quality); }
            catch (Exception ex)
            {
                gpu.Dispose(); gpu = null; cpu = new();
                api.Logger.Warning("Atmosphere GPU transport failed; using CPU transport: {0}", ex.Message);
            }
        }
        return cpu!.Update(sun, altitude, clouds, width, height, quality);
    }
    #endregion

    #region Lifetime
    /// <summary>Discards old-generation work; the caller retains its independently published lighting and texture.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; gpu?.Dispose(); cpu?.Dispose();
    }
    #endregion
}
