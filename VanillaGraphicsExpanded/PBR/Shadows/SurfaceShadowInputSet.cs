using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

namespace VanillaGraphicsExpanded.PBR.Shadows;

/// <summary>Owns one surface parameter block while borrowing an exact light and shadow publication.</summary>
internal sealed class SurfaceShadowInputSet : IDisposable
{
    private readonly SurfaceShadowParameters parameters = new();
    private ShadowLightSnapshot? lights;
    private ShadowFramePublication? publication;
    private Atmosphere.AtmosphereLighting? atmosphere;
    private ShadowPageSettings settings;
    private ShadowLightSnapshot? packedLights;
    private ShadowFramePublication? packedPublication;
    private Atmosphere.AtmosphereLighting? packedAtmosphere;
    private bool packedReady;
    private bool packed;
    private bool disposed;

    #region Public API
    /// <summary>Captures a coherent input interval and freezes the fallback bias policy.</summary>
    internal SurfaceShadowInputSet(ShadowLightSnapshot? lights, ShadowFramePublication? publication,
        Atmosphere.AtmosphereLighting? atmosphere, ShadowPageSettings settings)
    {
        this.settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Freeze();
        parameters.SetCaptureGuard(RefreshParameters);
        Update(lights, publication, atmosphere);
    }

    /// <summary>Indicates whether this exact light snapshot can consume the currently open page interval.</summary>
    internal bool IsCurrent => !disposed && publication is { IsCurrent: true }
        && ReferenceEquals(publication.Lights, lights);

    /// <summary>Refreshes the default frame-owned set without changing its UBO allocation or borrowed ownership.</summary>
    internal void Update(ShadowLightSnapshot? currentLights, ShadowFramePublication? currentPublication,
        Atmosphere.AtmosphereLighting? currentAtmosphere)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lights = currentLights;
        publication = currentPublication;
        atmosphere = currentAtmosphere;
    }

    /// <summary>Applies the frozen fallback bias policy for a newly attached consumer generation.</summary>
    internal void UpdatePolicy(ShadowPageSettings currentSettings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        settings = (currentSettings ?? throw new ArgumentNullException(nameof(currentSettings))).Freeze();
        packed = false;
    }

    /// <summary>Returns the packed current illumination and exact source indices for this interval.</summary>
    internal CpuUniformBuffer Parameters
    {
        get
        {
            RefreshParameters();
            return parameters;
        }
    }

    /// <summary>Returns the selected projection requests or an initialized disabled resource.</summary>
    internal GpuShaderStorageBuffer Requests => IsCurrent ? publication!.Requests : SurfaceShadowBindings.DisabledBuffer;
    /// <summary>Returns the matching page table or an initialized disabled resource.</summary>
    internal GpuShaderStorageBuffer Entries => IsCurrent ? publication!.Pages.Entries : SurfaceShadowBindings.DisabledBuffer;
    /// <summary>Returns whole-light readiness for the exact publication or an initialized disabled resource.</summary>
    internal GpuShaderStorageBuffer Readiness => IsCurrent ? publication!.Readiness : SurfaceShadowBindings.DisabledBuffer;
    /// <summary>Returns per-source miss diagnostics or an initialized disabled resource.</summary>
    internal GpuShaderStorageBuffer Diagnostics => IsCurrent && publication!.SampleFailures is { } failures
        ? failures : SurfaceShadowBindings.DisabledBuffer;
    /// <summary>Returns the current integer depth array or the safe one-layer disabled array.</summary>
    internal GpuTextureRef Depth => IsCurrent ? publication!.Pages.DepthArray : SurfaceShadowBindings.DisabledDepth;

    /// <summary>Retires only this object's parameter packing; frame resources remain owned by their gate.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        parameters.Dispose();
        publication = null;
        lights = null;
        atmosphere = null;
    }
    #endregion

    #region Private
    /// <summary>Refreshes the packed source map only when identity or immediate publication eligibility changes.</summary>
    private void RefreshParameters()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        bool ready = IsCurrent;
        if (packed && ReferenceEquals(lights, packedLights) && ReferenceEquals(publication, packedPublication)
            && ReferenceEquals(atmosphere, packedAtmosphere) && ready == packedReady)
            return;
        var current = ready ? publication : null;
        parameters.Set(lights, current, atmosphere, current?.Pages.Settings ?? settings);
        packedLights = lights;
        packedPublication = publication;
        packedAtmosphere = atmosphere;
        packedReady = ready;
        packed = true;
    }
    #endregion
}
