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
    /// <summary>Allocates five effect-specific std140 vector slots.</summary>
    internal AtmosphereSkyInputs() : base(80) { }

    /// <summary>Captures atmospheric compatibility values and constructs the owned camera-relative transform.</summary>
    internal void Capture(ICoreClientAPI api, AtmosphereLighting lighting, bool sceneLinear)
    {
        var render = api.Render;
        var uniforms = render.ShaderUniforms;
        WriteVector4(0, new(lighting.Sun, lighting.HorizonElevation));
        WriteVector4(16, new(api.Ambient.BlendedFogDensity, api.Ambient.BlendedFogMin,
            uniforms.FlagFogDensity, uniforms.FlatFogStartYPos - uniforms.PlayerPos.Y));
        WriteVector4(32, new(ReadDaylight(uniforms), api.Ambient.BlendedCloudDensity,
            (float)(api.World.Player.Entity.Pos.Y - api.World.SeaLevel), uniforms.FogWaveCounter));
        var murk = uniforms.WaterMurkColor;
        WriteVector4(48, new(murk.R, murk.G, murk.B, uniforms.CameraUnderwater));
        WriteVector4(64, new(uniforms.NightVisionStrength, uniforms.PsychedelicStrength,
            uniforms.WindWaveCounter * 5, sceneLinear ? 1 : 0));
    }
    #endregion
}
