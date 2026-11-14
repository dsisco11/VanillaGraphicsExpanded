using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Packs liquid frame and atmosphere-medium inputs independently of shared direct lighting.</summary>
internal sealed class LiquidFrameParamsUbo : CpuUniformBuffer
{
    internal const string BlockName = "VgeLiquidFrameParams";
    internal const int BlockSize = 1216;

    #region Public API
    #region Layout
    internal const int AnimationOffset = 0;
    internal const int PlayerPositionOffset = 16;
    internal const int AtlasMetricsOffset = 32;
    internal const int SeasonOffset = 48;
    internal const int EnvironmentIrradianceOffset = 64;
    internal const int AerialParametersOffset = 80;
    internal const int FogSphereCountOffset = 96;
    internal const int PerceptionOffset = 112;
    internal const int PerceptionPositionOffset = 128;
    internal const int ColorMapRectsOffset = 144;
    internal const int FogSphereComponentsOffset = 784;
    internal const int MediumLookupEnabledOffset = 1168;
    internal const int VolumeTransportEnabledOffset = 1172;
    internal const int RefractionEnabledOffset = 1176;
    internal const int SceneLinearOffset = 1180;
    internal const int ScatteringSunOffset = 1184;
    internal const int ScatteringMoonOffset = 1200;
    internal const int ColorMapRectCapacity = 40;
    internal const int ColorMapRectStride = 16;
    internal const int FogSphereComponentCapacity = 24;
    internal const int FogSphereComponentStride = 16;
    internal const int FogSphereComponentsPerSphere = 8;
    internal const int FogSphereCapacity = FogSphereComponentCapacity / FogSphereComponentsPerSphere;
    internal const int FogSphereComponentsSize = FogSphereComponentCapacity * FogSphereComponentStride;
    #endregion

    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates the block declared by liquids/params.glsl.</summary>
    internal LiquidFrameParamsUbo() : base(Rendering.Uniforms.UniformBufferUsage.SingleFrame) { }
    #region Frame values
    /// <summary>Stages the three liquid animation counters and clears the reserved component.</summary>
    internal void SetAnimation(float stillTime, float flowTime, float windTime)
    {
        WriteVector4(AnimationOffset, new(stillTime, flowTime, 0, windTime));
    }
    /// <summary>Stages player position with a deterministic reserved component.</summary>
    internal void SetPlayerPosition(Vector3 position)
    {
        WriteVector4(PlayerPositionOffset, new(position, 0));
    }
    /// <summary>Packs tile dimensions and atlas pixel extents.</summary>
    internal void SetAtlasMetrics(Vector2 tileSize, int width, int height)
    {
        WriteVector4(AtlasMetricsOffset, new(tileSize, width, height));
    }
    /// <summary>Packs climate and season controls.</summary>
    internal void SetSeason(float fraction, float seaLevel, float atlasHeight, float temperature)
    {
        WriteVector4(SeasonOffset, new(fraction, seaLevel, atlasHeight, temperature));
    }
    /// <summary>Stages diffuse irradiance without the surface direct-light terms.</summary>
    internal void SetEnvironmentIrradiance(Vector3 irradiance)
    {
        WriteVector4(EnvironmentIrradianceOffset, new(irradiance, 0));
    }
    /// <summary>Packs atmospheric coordinates and the inverse storage scale used for sampling.</summary>
    internal void SetAerialParameters(float altitude, float horizon, float underwater, float lookupStorageScale)
    {
        WriteVector4(AerialParametersOffset, new(altitude, horizon, underwater, 1 / lookupStorageScale));
    }
    /// <summary>Stages perception strength and origin while clearing reserved components.</summary>
    internal void SetPerception(float strength, Vector3 position)
    {
        WriteVector4(PerceptionOffset, new(0, strength, 0, 0));
        WriteVector4(PerceptionPositionOffset, new(position, 0));
    }
    /// <summary>Stages the admitted source directions for atmospheric scattering.</summary>
    internal void SetScatteringDirections(Vector3 sun, Vector3 moon)
    {
        WriteVector4(ScatteringSunOffset, new(sun, 0));
        WriteVector4(ScatteringMoonOffset, new(moon, 0));
    }
    #endregion

    #region Feature selection
    /// <summary>Enables the current material-medium coefficient lookup.</summary>
    internal bool MediumLookupEnabled { set => WriteFloat(MediumLookupEnabledOffset, value ? 1 : 0); }
    /// <summary>Moves bulk attenuation to scene-linear composition for a valid boundary capture.</summary>
    internal bool VolumeTransportEnabled { set => WriteFloat(VolumeTransportEnabledOffset, value ? 1 : 0); }
    /// <summary>Enables refraction only with coherent immutable opaque inputs.</summary>
    internal bool RefractionEnabled { set => WriteFloat(RefractionEnabledOffset, value ? 1 : 0); }
    /// <summary>Selects unexposed scene-linear output for the shared HDR handoff.</summary>
    internal bool SceneLinear { set => WriteFloat(SceneLinearOffset, value ? 1 : 0); }
    #endregion

    #region Native frame capture
    /// <summary>Sets the bounded number of active fog spheres.</summary>
    internal void SetFogSphereCount(int spheres)
    {
        if ((uint)spheres > FogSphereCapacity) throw new ArgumentOutOfRangeException(nameof(spheres));
        WriteUInt32(FogSphereCountOffset, (uint)spheres);
    }
    /// <summary>Stages a color-map rectangle at its std140 array stride.</summary>
    internal void SetColorMapRect(int index, Vector4 value)
    {
        if ((uint)index >= ColorMapRectCapacity) throw new ArgumentOutOfRangeException(nameof(index));
        WriteVector4(ColorMapRectsOffset + index * ColorMapRectStride, value);
    }
    /// <summary>Stages a fog-sphere component at its std140 array stride.</summary>
    internal void SetFogSphereComponent(int index, float value)
    {
        if ((uint)index >= FogSphereComponentCapacity) throw new ArgumentOutOfRangeException(nameof(index));
        WriteFloat(FogSphereComponentsOffset + index * FogSphereComponentStride, value);
    }

    /// <summary>Captures native frame data and packs its arrays using this layout's owned capacities and strides.</summary>
    internal void Capture(ICoreClientAPI api, Vec2f tileSize, bool boundaryCapture)
    {
        var u = api.Render.ShaderUniforms;
        var atmosphere = AtmosphereModSystem.Lighting;
        SetAnimation(u.WaterStillCounter, u.WaterFlowCounter, u.WindWaveCounter);
        SetPlayerPosition(new(u.PlayerPos.X, u.PlayerPos.Y, u.PlayerPos.Z));
        SetAtlasMetrics(new(tileSize.X, tileSize.Y), api.BlockTextureAtlas.Size.Width, api.BlockTextureAtlas.Size.Height);
        SetSeason(u.SeasonRel, u.SeaLevel, u.BlockAtlasHeight, u.SeasonTemperature);
        SetScatteringDirections(atmosphere?.Sun ?? Vector3.UnitY, atmosphere?.Moon ?? Vector3.UnitY);
        SetEnvironmentIrradiance(atmosphere?.Environment ?? Vector3.Zero);
        SetAerialParameters(atmosphere?.Altitude ?? 0, atmosphere?.HorizonElevation ?? 0, u.CameraUnderwater,
            atmosphere?.LookupStorageScale ?? 1);
        // Boundary capture advertises no fog spheres; surface capture refreshes the active prefix.
        int spheres = boundaryCapture ? 0 : Math.Clamp(u.FogSphereQuantity, 0,
            Math.Min(FogSphereCapacity, u.FogSpheres.Length / FogSphereComponentsPerSphere));
        SetFogSphereCount(spheres);
        SetPerception(u.PsychedelicStrength, new(u.PlayerPosForFoam.X, u.PlayerPosForFoam.Y, u.PlayerPosForFoam.Z));
        // Climate coordinates remain active in every mode, so always populate the complete rectangle array.
        for (int i = 0; i < ColorMapRectCapacity; i++)
            SetColorMapRect(i, new(u.ColorMapRects4[i * 4], u.ColorMapRects4[i * 4 + 1],
                u.ColorMapRects4[i * 4 + 2], u.ColorMapRects4[i * 4 + 3]));
        for (int i = 0; i < spheres * FogSphereComponentsPerSphere; i++) SetFogSphereComponent(i, u.FogSpheres[i]);
    }
    #endregion
    #endregion
}
