using System;
using System.Numerics;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Packs spatial sky effects independently of the shared camera and display exposure.</summary>
internal sealed class AtmosphereSkyInputs : CpuUniformBuffer
{
    private static readonly AccessTools.FieldRef<DefaultShaderUniforms, float> ReadDaylight =
        AccessTools.FieldRefAccess<DefaultShaderUniforms, float>("SkyDaylight");
    #region Public API
    /// <summary>Allocates effect vectors and matching lookup-encoding metadata.</summary>
    internal AtmosphereSkyInputs() : base(96) { }
    /// <summary>Matches admitted authored sky additions to the current immutable effect input slice.</summary>
    internal float CompatibilityMaximumNits { get; private set; }

    /// <summary>Captures atmospheric compatibility values and constructs the owned camera-relative transform.</summary>
    internal void Capture(ICoreClientAPI api, AtmosphereLighting lighting, bool sceneLinear)
    {
        var render = api.Render;
        var uniforms = render.ShaderUniforms;
        var murk = uniforms.WaterMurkColor;
        if (!LightingCalibration.FiniteNonnegative(new(murk.R, murk.G, murk.B)))
            throw new ArgumentException("Sky tint requires admitted finite authored RGB.");
        float compatibilityMaximum = SceneColor.SceneColorCompatibilityBounds.PhysicalSky(uniforms.PsychedelicStrength,
            Math.Max(murk.R, Math.Max(murk.G, murk.B)), uniforms.NightVisionStrength);
        Span<Vector4> values = stackalloc Vector4[]
        {
            new(lighting.Sun, lighting.HorizonElevation),
            new(api.Ambient.BlendedFogDensity, api.Ambient.BlendedFogMin,
                uniforms.FlagFogDensity, uniforms.FlatFogStartYPos - uniforms.PlayerPos.Y),
            new(ReadDaylight(uniforms), api.Ambient.BlendedCloudDensity,
                (float)(api.World.Player.Entity.Pos.Y - api.World.SeaLevel), uniforms.FogWaveCounter),
            new(murk.R, murk.G, murk.B, uniforms.CameraUnderwater),
            new(uniforms.NightVisionStrength, uniforms.PsychedelicStrength,
                uniforms.WindWaveCounter * 5, sceneLinear ? 1 : 0),
            new(1 / lighting.LookupStorageScale, 0, 0, 0)
        };
        // Validate the complete expression inputs before changing either the packed
        // slice or its envelope. Overflowing derived wind/altitude is not a new generation.
        foreach (var value in values)
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
                throw new ArgumentException("Sky publication requires finite effect inputs.");
        for (int index = 0; index < values.Length; index++) WriteVector4(index * 16, values[index]);
        CompatibilityMaximumNits = compatibilityMaximum;
    }
    #endregion
}
