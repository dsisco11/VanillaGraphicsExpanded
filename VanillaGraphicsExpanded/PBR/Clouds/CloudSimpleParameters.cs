using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Packs native Simple shape inputs and physical light/medium values without changing engine uniforms.</summary>
internal sealed class CloudSimpleParameters : CpuUniformBuffer
{
    private readonly float[] modelView = new float[16];

    #region Public API
    #region Layout
    internal const int ProjectionOffset = 0;
    internal const int ViewOffset = 64;
    internal const int OffsetLengthOffset = 128;
    internal const int TileClockAlphaOffset = 144;
    internal const int PerceptionOffset = 160;
    internal const int PlayerOffset = 176;
    internal const int SolarOffset = 192;
    internal const int LunarOffset = 208;
    internal const int EnvironmentOffset = 224;
    internal const int SunDirectionOffset = 240;
    internal const int MoonDirectionOffset = 256;
    internal const int AerialParametersOffset = 272;
    internal const int FogOffset = 288;
    internal const int FogColorOffset = 304;
    internal const int MurkOffset = 320;
    internal const int ReservedOffset = 336;
    internal const int FogSpheresOffset = 352;
    internal const int FogMediaOffset = 368;
    internal const int FogSphereStride = 32;
    internal const int FogSphereCapacity = 3;
    #endregion

    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 448;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates matrices, fourteen vectors and three compact local fog records.</summary>
    internal CloudSimpleParameters() : base() { }

    /// <summary>Copies post-tick placement with the current camera; native artistic lighting is never captured.</summary>
    internal void Capture(ICoreClientAPI api, CloudSimpleInput input, AtmosphereLighting lighting)
    {
        var render = api.Render; var u = render.ShaderUniforms; var placement = input.Placement;
        var mv = new Matrixf().Set(render.CameraMatrixOriginf).Translate(placement.Offset.X, placement.Offset.Y, placement.Offset.Z);
        Array.Copy(mv.Values, modelView, 16);
        CaptureGeometry(render.CurrentProjectionMatrix, modelView,
            new(placement.Offset, (float)input.TileSize * input.Width),
            new((float)(((double)placement.CenterX - placement.TileX) * input.TileSize),
                (float)(((double)placement.CenterZ - placement.TileZ) * input.TileSize), placement.Clock,
                Math.Clamp(1 - 1.5f * Math.Max(0, u.GlitchStrength - .1f), 0, 1)),
            new(u.PerceptionEffectId, LocalPerception(u.PerceptionEffectIntensity), u.TimeCounter, u.FogSphereQuantity),
            new(u.PlayerPos.X, u.PlayerPos.Y, u.PlayerPos.Z, 0));
        var fog = api.Ambient.BlendedFogColor; var murk = u.WaterMurkColor;
        CaptureLighting(lighting, u.CameraUnderwater,
            new(api.Ambient.BlendedFogDensity, api.Ambient.BlendedFogMin, api.Ambient.BlendedFlatFogDensity,
                api.Ambient.BlendedFlatFogYPosForShader - (float)api.World.Player.Entity.CameraPos.Y),
            new(fog.R, fog.G, fog.B), new(murk.R, murk.G, murk.B));
        if (u.FogSphereQuantity is < 0 or > 3 || u.FogSpheres.Length < u.FogSphereQuantity * 8)
            throw new ArgumentException("Simple clouds require the native three-sphere fog contract.");
        for (int i = 0; i < u.FogSphereQuantity; i++)
        {
            int start = i * 8;
            Vector4 sphere = new(u.FogSpheres[start], u.FogSpheres[start+1], u.FogSpheres[start+2], u.FogSpheres[start+3]);
            Vector4 medium = new(u.FogSpheres[start+4], u.FogSpheres[start+5], u.FogSpheres[start+6], u.FogSpheres[start+7]);
            Validate(sphere); Validate(medium);
            if (sphere.W <= 0 || medium.X < 0) throw new ArgumentException("Unsupported local cloud fog sphere.");
            WriteVector4(FogSpheresOffset + i * FogSphereStride, sphere); WriteVector4(FogMediaOffset + i * FogSphereStride, medium);
        }
    }

    /// <summary>Applies the native vertex factor locally, rejecting arithmetic overflow before submission.</summary>
    internal static float LocalPerception(float intensity)
    {
        float value = intensity * 20;
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(intensity));
        return value;
    }

    /// <summary>Stages explicitly owned matrices and shape values for normal or controlled view submission.</summary>
    internal void CaptureGeometry(ReadOnlySpan<float> projection, ReadOnlySpan<float> view,
        Vector4 offsetLength, Vector4 tileClockAlpha, Vector4 perception, Vector4 player)
    {
        if (projection.Length != 16 || view.Length != 16 || offsetLength.W <= 0
            || tileClockAlpha.W is < 0 or > 1 || perception.W is < 0 or > 3 || perception.W != MathF.Floor(perception.W))
            throw new ArgumentException("Unsupported Simple cloud shape inputs.");
        foreach (float value in projection) if (!float.IsFinite(value)) throw new ArgumentException("Invalid cloud projection.");
        foreach (float value in view) if (!float.IsFinite(value)) throw new ArgumentException("Invalid cloud view.");
        Validate(offsetLength); Validate(tileClockAlpha); Validate(perception); Validate(player);
        WriteMatrix4(ProjectionOffset, projection); WriteMatrix4(ViewOffset, view);
        WriteVector4(OffsetLengthOffset, offsetLength); WriteVector4(TileClockAlphaOffset, tileClockAlpha);
        WriteVector4(PerceptionOffset, perception); WriteVector4(PlayerOffset, player);
    }

    /// <summary>Copies both extinguished celestial sources, E/pi environment and matching aerial decoding.</summary>
    internal void CaptureLighting(AtmosphereLighting lighting, float underwater, Vector4 fog, Vector3 fogColor, Vector3 murk)
    {
        _ = CloudSimpleLighting.Maximum(lighting, 0);
        Validate(fog); Validate(new(0, 0, 0, underwater));
        if (fog.X < 0 || !LightingCalibration.FiniteNonnegative(fogColor) || !LightingCalibration.FiniteNonnegative(murk))
            throw new ArgumentException("Unsupported Simple cloud medium.");
        WriteVector4(SolarOffset, new(lighting.Solar, 0)); WriteVector4(LunarOffset, new(lighting.Lunar, 0));
        WriteVector4(EnvironmentOffset, new(lighting.Environment, 0));
        WriteVector4(SunDirectionOffset, new(Vector3.Normalize(lighting.Sun), 0)); WriteVector4(MoonDirectionOffset, new(Vector3.Normalize(lighting.Moon), 0));
        WriteVector4(AerialParametersOffset, new(lighting.Altitude, lighting.HorizonElevation, underwater, 1 / lighting.LookupStorageScale));
        WriteVector4(FogOffset, fog);
        WriteVector4(FogColorOffset, new(fogColor, 0));
        WriteVector4(MurkOffset, new(murk, 0));
        // Last vector is reserved, keeping future layout changes explicit.
        WriteVector4(ReservedOffset, Vector4.Zero);
    }
    #endregion

    #region Private
    /// <summary>Rejects nonfinite values before publishing them in a shader input slice.</summary>
    private static void Validate(Vector4 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new ArgumentException("Simple cloud parameters must be finite.");
    }
    #endregion
}
