using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Publishes only a fenced complete cube, catalog, calibration and diffuse source representation.</summary>
internal sealed class CelestialResourceSet : IDisposable
{
    /// <summary>Contains immutable catalog, quality and light quadrature from this exact source generation.</summary>
    internal CelestialSource Source { get; }
    /// <summary>Contains all six completed packed HDR faces and their solid-angle mip chain.</summary>
    internal DynamicTextureCube Galaxy { get; }
    #region Public API
    /// <summary>Takes resource ownership only after every generation submission has completed successfully.</summary>
    internal CelestialResourceSet(CelestialSource source, DynamicTextureCube galaxy) { Source = source; Galaxy = galaxy; }
    /// <summary>Retires storage through the existing render-context disposal owner.</summary>
    public void Dispose() => Galaxy.Dispose();
    #endregion
}
