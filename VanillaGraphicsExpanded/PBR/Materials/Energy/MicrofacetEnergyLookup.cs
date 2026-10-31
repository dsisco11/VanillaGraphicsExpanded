using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.ModSystems;

namespace VanillaGraphicsExpanded.PBR.Materials.Energy;

/// <summary>Owns the filtered material-energy texture for the active rendering context and shared quality level.</summary>
internal static class MicrofacetEnergyLookup
{
    private static Texture2D? texture;
    private static (nint Handle, long Generation) context;
    private static RHISampledTextureBindings? bindings;

    #region Public API
    /// <summary>Reuses complete storage or uploads a validated replacement before exposing it to shaders.</summary>
    internal static Texture2D Prepare()
    {
        var current = RenderContextRegistry.Current();
        if (current == default) throw new InvalidOperationException("Microfacet lookup requires a live rendering context.");
        int size = ShadingQuality.EnergyLookupSize(ConfigModSystem.Config.ShadingQuality);
        if (context == current && texture is not null && texture.Width == size) return texture;
        var support = GpuSupport.Graphics;
        if (support.MaxCombinedTextureImageUnits <= GpuBindingRegistry.Sampler.MicrofacetEnergy)
            throw new InvalidOperationException("The microfacet energy lookup exceeds texture binding capabilities.");
        var candidate = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rg16f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "PBR.MicrofacetEnergy" }).RequireResource();
        try { candidate.UploadDataImmediate(MicrofacetEnergyTable.Shared.CreateTextureData(size)); }
        catch { candidate.Dispose(); throw; }
        // Reloaded shaders reuse storage; quality/context replacement becomes visible only
        // after the complete synchronous upload succeeds, preserving the previous texture on failure.
        bindings = null;
        var previous = texture; texture = candidate; context = current; previous?.Dispose();
        return candidate;
    }

    /// <summary>Withdraws publication before retiring its owned GPU storage at world/context teardown.</summary>
    internal static void Retire()
    {
        bindings = null;
        var previous = texture; texture = null; context = default; previous?.Dispose();
    }
    /// <summary>Reuses the lookup owner's sampled declaration independently of external slot mutations.</summary>
    internal static RHISampledTextureBindings PrepareBindings()
    {
        var selected = Prepare();
        if (bindings is null || bindings.RequiresRefresh)
            bindings = [new(GpuBindingRegistry.Sampler.MicrofacetEnergy, selected, GpuSamplers.LinearClamp)];
        return bindings;
    }
    #endregion
}
