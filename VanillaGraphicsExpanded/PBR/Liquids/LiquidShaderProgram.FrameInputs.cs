using System;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Exposes typed frame inputs independently of their packed storage.</summary>
internal sealed partial class LiquidShaderProgram
{
    private ShaderSettings? frameCaptureSettings;
    private bool boundaryCapture;

    #region Frame inputs
    /// <summary>Stages the column-major near-cascade transform; Use submits the completed frame.</summary>
    internal ReadOnlySpan<float> ShadowMatrixNear { set => frame.ShadowMatrixNear = value; }
    /// <summary>Stages the column-major far-cascade transform; Use submits the completed frame.</summary>
    internal ReadOnlySpan<float> ShadowMatrixFar { set => frame.ShadowMatrixFar = value; }
    /// <summary>Stages still-water time, flow time, reserved zero, and wind time; Use submits the completed frame.</summary>
    internal Vector4 Animation { set => frame.Animation = value; }
    /// <summary>Stages near and far shadow ranges in XY; ZW are reserved; Use submits the completed frame.</summary>
    internal Vector4 ShadowRanges { set => frame.ShadowRanges = value; }
    /// <summary>Stages world player position in XYZ; W is reserved; Use submits the completed frame.</summary>
    internal Vector4 PlayerPosition { set => frame.PlayerPosition = value; }
    /// <summary>Stages tile UV dimensions in XY and atlas pixel dimensions in ZW; Use submits the completed frame.</summary>
    internal Vector4 AtlasMetrics { set => frame.AtlasMetrics = value; }
    /// <summary>Stages season fraction, sea level, atlas height, and seasonal temperature; Use submits the completed frame.</summary>
    internal Vector4 Season { set => frame.Season = value; }
    /// <summary>Stages world sun direction in XYZ; W is reserved; Use submits the completed frame.</summary>
    internal Vector4 SunDirection { set => frame.SunDirection = value; }
    /// <summary>Stages solar irradiance in RGB; W is reserved; Use submits the completed frame.</summary>
    internal Vector4 SolarIrradiance { set => frame.SolarIrradiance = value; }
    /// <summary>Stages environment irradiance in RGB; W is reserved; Use submits the completed frame.</summary>
    internal Vector4 EnvironmentIrradiance { set => frame.EnvironmentIrradiance = value; }
    /// <summary>Stages altitude, horizon elevation, camera-underwater amount, and reserved zero; Use submits the completed frame.</summary>
    internal Vector4 AerialParameters { set => frame.AerialParameters = value; }
    /// <summary>Stages psychedelic strength in Y; other components are reserved; Use submits the completed frame.</summary>
    internal Vector4 Perception { set => frame.Perception = value; }
    /// <summary>Stages perception world offset in XYZ; W is reserved; Use submits the completed frame.</summary>
    internal Vector4 PerceptionPosition { set => frame.PerceptionPosition = value; }
    /// <summary>Stages the active fog-sphere count independently of shared lighting.</summary>
    internal void SetFogSphereCount(int spheres)
    {
        if ((uint)spheres > 3) throw new ArgumentOutOfRangeException(nameof(spheres));
        frame.SetFogSphereCount(spheres);
    }
    /// <summary>Stages one ColorMapRect array element.</summary>
    internal void SetColorMapRect(int index, Vector4 value) => frame.SetColorMapRect(index, value);
    /// <summary>Stages one FogSphereComponent array element.</summary>
    internal void SetFogSphereComponent(int index, float value) => frame.SetFogSphereComponent(index, value);
    /// <summary>Copies the selected liquid pass's coherent frame inputs; arrays use std140 sixteen-byte strides.</summary>
    internal void CaptureFrameInputs(ICoreClientAPI api, Vec2f tileSize)
    {
        var render = api.Render;
        var u = render.ShaderUniforms;
        var atmosphere = AtmosphereModSystem.Lighting;
        ShadowMatrixNear = u.ToShadowMapSpaceMatrixNear;
        ShadowMatrixFar = u.ToShadowMapSpaceMatrixFar;
        Animation = new(u.WaterStillCounter, u.WaterFlowCounter, 0, u.WindWaveCounter);
        int shadows = Vintagestory.Client.NoObf.ClientSettings.ShadowMapQuality;
        ShadowRanges = new(shadows > 1 ? u.ShadowRangeNear : 0, shadows > 0 ? u.ShadowRangeFar : 0, 0, 0);
        PlayerPosition = new(u.PlayerPos.X, u.PlayerPos.Y, u.PlayerPos.Z, 0);
        AtlasMetrics = new(tileSize.X, tileSize.Y, api.BlockTextureAtlas.Size.Width, api.BlockTextureAtlas.Size.Height);
        Season = new(u.SeasonRel, u.SeaLevel, u.BlockAtlasHeight, u.SeasonTemperature);
        SunDirection = new(atmosphere?.Sun ?? Vector3.UnitY, 0);
        SolarIrradiance = new(atmosphere?.Solar ?? Vector3.Zero, 0);
        EnvironmentIrradiance = new(atmosphere?.Environment ?? Vector3.Zero, 0);
        AerialParameters = new(atmosphere?.Altitude ?? 0, atmosphere?.HorizonElevation ?? 0, u.CameraUnderwater, 0);
        // Boundary capture consumes no fog spheres. Keep its count zero so retained fog bytes
        // are never advertised as current; surface recapture refreshes the active prefix.
        var settings = RequestedSettings;
        if (!ReferenceEquals(frameCaptureSettings, settings))
        {
            // Resolve from this exact immutable snapshot, only when requested options change.
            // Reload does not change its meaning; mode transitions invalidate the reference naturally.
            boundaryCapture = ShaderOptionAccess.Get(settings, CaptureModeOption) == 3;
            frameCaptureSettings = settings;
        }
        int spheres = boundaryCapture ? 0 : Math.Clamp(u.FogSphereQuantity, 0, Math.Min(3, u.FogSpheres.Length / 8));
        SetFogSphereCount(spheres);
        Perception = new(0, u.PsychedelicStrength, 0, 0);
        PerceptionPosition = new(u.PlayerPosForFoam.X, u.PlayerPosForFoam.Y, u.PlayerPosForFoam.Z, 0);
        // The shared vertex executable still evaluates climate/season coordinates in every mode.
        for (int i = 0; i < 40; i++)
            SetColorMapRect(i, new(u.ColorMapRects4[i * 4], u.ColorMapRects4[i * 4 + 1], u.ColorMapRects4[i * 4 + 2], u.ColorMapRects4[i * 4 + 3]));
        for (int i = 0; i < spheres * 8; i++) SetFogSphereComponent(i, u.FogSpheres[i]);
    }
    #endregion
}
