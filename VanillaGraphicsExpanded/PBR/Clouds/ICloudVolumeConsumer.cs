using System;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Exposes selected draw admission and lifetime without sharing cloud algorithms or resources.</summary>
internal interface ICloudVolumeConsumer : IRenderer, IDisposable
{
    /// <summary>Prepares the draw before scene-lighting admission.</summary>
    bool Prepare();
    /// <summary>Withdraws retained state at explicit recovery boundaries.</summary>
    void Reset();
    /// <summary>Reserves current and retained physical response before Opaque.</summary>
    float Maximum(AtmosphereLighting lighting,float localFogMaximum);
}
