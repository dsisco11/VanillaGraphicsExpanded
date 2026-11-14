using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Numerics;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Publishes LumOn per-frame and world-probe shared state via Uniform Buffer Objects (UBOs).
/// </summary>
internal sealed class LumOnUniformBuffers : IDisposable
{
    public const string FrameBlockName = "LumOnFrameUBO";
    public const string WorldProbeBlockName = "LumOnWorldProbeUBO";

    public const int FrameBinding = GpuBindingRegistry.Ubo.LumOnFrame;
    public const int WorldProbeBinding = GpuBindingRegistry.Ubo.WorldProbe;

    private readonly LumOnFrameParameters frameValues = new();
    private readonly LumOnWorldProbeParameters worldProbeValues = new();

    private GpuUniformBuffer? frameUbo;
    private GpuUniformBuffer? worldProbeUbo;

    #region Public API
    /// <summary>Exposes the current native-compatible effect-control buffer.</summary>
    public GpuUniformBuffer FrameUbo => frameUbo ?? throw new InvalidOperationException("Frame UBO is not created.");

    public GpuUniformBuffer WorldProbeUbo => worldProbeUbo ?? throw new InvalidOperationException("World-probe UBO is not created.");

    public bool HasWorldProbeUbo => worldProbeUbo is not null && worldProbeUbo.BufferId != 0;

    public GpuUniformBuffer? WorldProbeUboOrNull => HasWorldProbeUbo ? worldProbeUbo : null;

    /// <summary>Creates the effect-control buffer without allocating any camera storage.</summary>
    public void EnsureCreated()
    {
        if (frameUbo is null || frameUbo.BufferId == 0)
        {
            frameUbo?.Dispose();
            frameUbo = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "LumOn.FrameUBO" }).RequireResource();
        }
    }

    /// <summary>Creates storage for the independently owned world-probe clipmap controls.</summary>
    public void EnsureWorldProbeCreated()
    {
        if (worldProbeUbo is null || worldProbeUbo.BufferId == 0)
        {
            worldProbeUbo?.Dispose();
            worldProbeUbo = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "LumOn.WorldProbeUBO" }).RequireResource();
        }
    }

    /// <summary>Publishes LumOn-specific target dimensions, history controls and environment lighting.</summary>
    public void UpdateFrame(
        float halfResWidth,
        float halfResHeight,
        float probeGridWidth,
        float probeGridHeight,
        int probeSpacing,
        int historyValid,
        int anchorJitterEnabled,
        int pmjCycleLength,
        int enableVelocityReprojection,
        float anchorJitterScale,
        float velocityRejectThreshold,
        Vec3f sunPosition,
        Vec3f sunColor,
        Vec3f ambientColor)
    {
        EnsureCreated();

        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        frameValues.Set(halfResWidth, halfResHeight, probeGridWidth, probeGridHeight, probeSpacing,
            historyValid, anchorJitterEnabled, pmjCycleLength, enableVelocityReprojection,
            anchorJitterScale, velocityRejectThreshold,
            atmosphere?.DirectDirection ?? new Vector3(sunPosition.X, sunPosition.Y, sunPosition.Z),
            atmosphere?.DirectIrradiance ?? new Vector3(sunColor.X, sunColor.Y, sunColor.Z),
            atmosphere?.Environment ?? new Vector3(ambientColor.X, ambientColor.Y, ambientColor.Z));
        FrameUbo.UploadOrResize(frameValues.Bytes, growExponentially: false);
    }

    /// <summary>Publishes clipmap placement and fallback lighting independently of camera matrices.</summary>
    public void UpdateWorldProbe(
        Vec3f skyTint,
        Vector3 cameraPosWS,
        ReadOnlySpan<Vector3> originMinCorner,
        ReadOnlySpan<Vector3> ringOffset)
    {
        EnsureWorldProbeCreated();

        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        worldProbeValues.Set(atmosphere?.Environment ?? new Vector3(skyTint.X, skyTint.Y, skyTint.Z),
            cameraPosWS, originMinCorner, ringOffset);
        WorldProbeUbo.UploadOrResize(worldProbeValues.Bytes, growExponentially: false);
    }

    /// <summary>Retires effect and clipmap storage when their lighting owner is disposed.</summary>
    public void Dispose()
    {
        frameUbo?.Dispose();
        worldProbeUbo?.Dispose();
        frameValues.Dispose();
        worldProbeValues.Dispose();
        frameUbo = null;
        worldProbeUbo = null;
    }

    #endregion
}
