using System;
using System.Numerics;
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

    #region Public API
    #region Frame values
    /// <summary>Stages liquid animation times through the owning frame block.</summary>
    internal void SetAnimation(float stillTime, float flowTime, float windTime)
    { RequireInputMutation(); frame.SetAnimation(stillTime, flowTime, windTime); }
    /// <summary>Stages the player world position.</summary>
    internal void SetPlayerPosition(Vector3 position)
    { RequireInputMutation(); frame.SetPlayerPosition(position); }
    /// <summary>Stages tile and atlas dimensions.</summary>
    internal void SetAtlasMetrics(Vector2 tileSize, int width, int height)
    { RequireInputMutation(); frame.SetAtlasMetrics(tileSize, width, height); }
    /// <summary>Stages native climate controls.</summary>
    internal void SetSeason(float fraction, float seaLevel, float atlasHeight, float temperature)
    { RequireInputMutation(); frame.SetSeason(fraction, seaLevel, atlasHeight, temperature); }
    /// <summary>Stages diffuse atmosphere irradiance.</summary>
    internal void SetEnvironmentIrradiance(Vector3 irradiance)
    { RequireInputMutation(); frame.SetEnvironmentIrradiance(irradiance); }
    /// <summary>Stages atmospheric coordinates and their storage encoding scale.</summary>
    internal void SetAerialParameters(float altitude, float horizon, float underwater, float lookupStorageScale)
    { RequireInputMutation(); frame.SetAerialParameters(altitude, horizon, underwater, lookupStorageScale); }
    /// <summary>Stages perception intensity and world origin.</summary>
    internal void SetPerception(float strength, Vector3 position)
    { RequireInputMutation(); frame.SetPerception(strength, position); }
    #endregion

    #region Native frame capture
    /// <summary>Stages the active fog-sphere count.</summary>
    internal void SetFogSphereCount(int spheres)
    { RequireInputMutation(); frame.SetFogSphereCount(spheres); }
    /// <summary>Stages one color-map rectangle.</summary>
    internal void SetColorMapRect(int index, Vector4 value)
    { RequireInputMutation(); frame.SetColorMapRect(index, value); }
    /// <summary>Stages one native fog-sphere component.</summary>
    internal void SetFogSphereComponent(int index, float value)
    { RequireInputMutation(); frame.SetFogSphereComponent(index, value); }
    /// <summary>Selects the liquid pass's capture policy and delegates frame packing to its UBO.</summary>
    internal void CaptureFrameInputs(ICoreClientAPI api, Vec2f tileSize)
    {
        RequireInputMutation();
        var settings = RequestedSettings;
        if (!ReferenceEquals(frameCaptureSettings, settings))
        {
            // Resolve from this exact immutable snapshot, only when requested options change.
            // Reload does not change its meaning; mode transitions invalidate the reference naturally.
            boundaryCapture = ShaderOptionAccess.Get(settings, CaptureModeOption) == 3;
            frameCaptureSettings = settings;
        }
        frame.Capture(api, tileSize, boundaryCapture);
    }
    #endregion
    #endregion
}
