using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Publishes atmospheric compatibility colors before ambient blending without changing other modifiers.</summary>
internal sealed class AtmosphereAmbientPublication : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly AmbientModifier modifier = new AmbientModifier().EnsurePopulated();
    private AmbientModifier? previous;
    private AmbientModifier? foreignOwner;
    private static readonly AccessTools.FieldRef<DefaultShaderUniforms, float> Daylight =
        AccessTools.FieldRefAccess<DefaultShaderUniforms, float>("SkyDaylight");
    private static readonly AccessTools.FieldRef<ClientMain, Vintagestory.API.MathTools.Vec3f> Fog =
        AccessTools.FieldRefAccess<ClientMain, Vintagestory.API.MathTools.Vec3f>("FogColorSky");

    #region Public API
    /// <summary>Retains the client ambient owner; no global state changes occur until publication.</summary>
    internal AtmosphereAmbientPublication(ICoreClientAPI api) => this.api = api;

    /// <summary>Replaces the sunglow slot in place so ordered weather and underwater modifiers retain priority.</summary>
    internal bool Publish(AtmosphereLighting lighting)
    {
        // The owned sky still needs fresh visibility inputs when another mod controls ambient colors.
        var values = AtmosphereLegacyLighting.From(lighting);
        Daylight(api.Render.ShaderUniforms) = values.Daylight;
        if (api.World is ClientMain game) Fog(game).Set(values.Fog.X, values.Fog.Y, values.Fog.Z);
        var modifiers = api.Ambient.CurrentModifiers;
        if (!modifiers.TryGetValue("sunglow", out var current)) return false;
        if (ReferenceEquals(current, foreignOwner)) return false;
        if (previous is not null && !ReferenceEquals(current, modifier))
        {
            // A foreign replacement is not ours to reclaim on the following frame.
            foreignOwner = current;
            previous = null;
            return false;
        }
        previous ??= current;
        modifier.AmbientColor.Value[0] = values.Ambient.X;
        modifier.AmbientColor.Value[1] = values.Ambient.Y;
        modifier.AmbientColor.Value[2] = values.Ambient.Z;
        modifier.AmbientColor.Weight = 1;
        modifier.FogColor.Value[0] = values.Fog.X;
        modifier.FogColor.Value[1] = values.Fog.Y;
        modifier.FogColor.Value[2] = values.Fog.Z;
        modifier.FogColor.Weight = 1;
        if (!ReferenceEquals(current, modifier)) modifiers["sunglow"] = modifier;
        return true;
    }

    /// <summary>Restores only the slot still owned by this publisher, preserving foreign replacements.</summary>
    public void Dispose()
    {
        var modifiers = api.Ambient.CurrentModifiers;
        if (previous is not null && modifiers.TryGetValue("sunglow", out var current))
        {
            if (ReferenceEquals(current, modifier)) modifiers["sunglow"] = previous;
            else foreignOwner = current;
        }
        previous = null;
    }
    #endregion
}
