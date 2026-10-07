using System;
using System.Numerics;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Packs view-ray transforms and spatial sky effects independently of display exposure.</summary>
internal sealed class AtmosphereSkyInputs : CpuUniformBuffer
{
    private static readonly AccessTools.FieldRef<DefaultShaderUniforms, float> ReadDaylight =
        AccessTools.FieldRefAccess<DefaultShaderUniforms, float>("SkyDaylight");
    #region Public API
    /// <summary>Allocates two matrices followed by six std140 vector slots.</summary>
    internal AtmosphereSkyInputs() : base(224) { }

    /// <summary>Captures atmospheric compatibility values and constructs the owned camera-relative transform.</summary>
    internal void Capture(ICoreClientAPI api, AtmosphereLighting lighting, bool sceneLinear)
    {
        var render = api.Render;
        var uniforms = render.ShaderUniforms;
        // Atmospheric publication precedes this draw; camera matrices remain engine owned.
        // Remove camera translation before inverting the world-to-clip transform.
        // Both matrices use the engine column-major convention, without changing its stacks.
        Span<float> view = stackalloc float[16];
        Span<float> viewProjection = stackalloc float[16];
        Span<float> inverse = stackalloc float[16];
        render.CurrentModelviewMatrix.AsSpan().CopyTo(view);
        view[12] = view[13] = view[14] = 0;
        MatrixHelper.Multiply(render.CurrentProjectionMatrix, view, viewProjection);
        if (!MatrixHelper.Invert(viewProjection, inverse))
            throw new InvalidOperationException("Sky camera transform is singular.");
        WriteMatrix4(0, inverse);
        WriteMatrix4(64, viewProjection);
        WriteVector4(128, new(lighting.Sun, lighting.HorizonElevation));
        WriteVector4(144, new(api.Ambient.BlendedFogDensity, api.Ambient.BlendedFogMin,
            uniforms.FlagFogDensity, uniforms.FlatFogStartYPos - uniforms.PlayerPos.Y));
        WriteVector4(160, new(ReadDaylight(uniforms), api.Ambient.BlendedCloudDensity,
            (float)(api.World.Player.Entity.Pos.Y - api.World.SeaLevel), uniforms.FogWaveCounter));
        WriteVector4(176, new(uniforms.ZNear, uniforms.ZFar, render.FrameWidth, render.FrameHeight));
        var murk = uniforms.WaterMurkColor;
        WriteVector4(192, new(murk.R, murk.G, murk.B, uniforms.CameraUnderwater));
        WriteVector4(208, new(uniforms.NightVisionStrength, uniforms.PsychedelicStrength,
            uniforms.WindWaveCounter * 5, sceneLinear ? 1 : 0));
    }
    #endregion
}
