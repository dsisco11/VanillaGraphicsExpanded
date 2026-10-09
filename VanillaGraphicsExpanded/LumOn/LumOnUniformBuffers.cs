using System;
using System.Buffers.Binary;
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

    private const int WorldProbeMaxLevels = 8;

    private const int FrameUboSizeBytes = 112;
    private const int WorldProbeUboSizeBytes = 288;

    private readonly byte[] frameBytes = new byte[FrameUboSizeBytes];
    private readonly byte[] worldProbeBytes = new byte[WorldProbeUboSizeBytes];

    private GpuUniformBuffer? frameUbo;
    private GpuUniformBuffer? worldProbeUbo;

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
            frameUbo = GpuUniformBuffer.Create(debugName: "LumOn.FrameUBO");
        }
    }

    /// <summary>Creates storage for the independently owned world-probe clipmap controls.</summary>
    public void EnsureWorldProbeCreated()
    {
        if (worldProbeUbo is null || worldProbeUbo.BufferId == 0)
        {
            worldProbeUbo?.Dispose();
            worldProbeUbo = GpuUniformBuffer.Create(debugName: "LumOn.WorldProbeUBO");
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

        int offset = 0;

        WriteVec4(frameBytes, offset, halfResWidth, halfResHeight, probeGridWidth, probeGridHeight); offset += 16;
        WriteIvec4(frameBytes, offset, probeSpacing, 0, historyValid, anchorJitterEnabled); offset += 16;
        WriteIvec4(frameBytes, offset, pmjCycleLength, enableVelocityReprojection, 0, 0); offset += 16;

        WriteVec4(frameBytes, offset, anchorJitterScale, velocityRejectThreshold, 0f, 0f); offset += 16;

        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        WriteVec4(frameBytes, offset, atmosphere?.Sun.X ?? sunPosition.X, atmosphere?.Sun.Y ?? sunPosition.Y,
            atmosphere?.Sun.Z ?? sunPosition.Z, 0f); offset += 16;
        WriteVec4(frameBytes, offset, atmosphere?.Solar.X ?? sunColor.X, atmosphere?.Solar.Y ?? sunColor.Y, atmosphere?.Solar.Z ?? sunColor.Z, 0f); offset += 16;
        WriteVec4(frameBytes, offset, atmosphere?.Environment.X ?? ambientColor.X, atmosphere?.Environment.Y ?? ambientColor.Y, atmosphere?.Environment.Z ?? ambientColor.Z, 0f); offset += 16;

        if (offset != FrameUboSizeBytes)
        {
            throw new InvalidOperationException($"Frame UBO packing size mismatch: wrote {offset} bytes, expected {FrameUboSizeBytes}.");
        }

        FrameUbo.UploadOrResize(frameBytes, FrameUboSizeBytes, growExponentially: false);
    }

    /// <summary>Publishes clipmap placement and fallback lighting independently of camera matrices.</summary>
    public void UpdateWorldProbe(
        Vec3f skyTint,
        Vector3 cameraPosWS,
        ReadOnlySpan<Vector3> originMinCorner,
        ReadOnlySpan<Vector3> ringOffset)
    {
        EnsureWorldProbeCreated();

        int offset = 0;

        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        WriteVec4(worldProbeBytes, offset, atmosphere?.Environment.X ?? skyTint.X, atmosphere?.Environment.Y ?? skyTint.Y, atmosphere?.Environment.Z ?? skyTint.Z, 0f); offset += 16;
        WriteVec4(worldProbeBytes, offset, cameraPosWS.X, cameraPosWS.Y, cameraPosWS.Z, 0f); offset += 16;

        for (int i = 0; i < WorldProbeMaxLevels; i++)
        {
            Vector3 o = (i < originMinCorner.Length) ? originMinCorner[i] : default;
            WriteVec4(
                worldProbeBytes,
                offset,
                o.X,
                o.Y,
                o.Z,
                0f);
            offset += 16;
        }

        for (int i = 0; i < WorldProbeMaxLevels; i++)
        {
            Vector3 r = (i < ringOffset.Length) ? ringOffset[i] : default;
            WriteVec4(
                worldProbeBytes,
                offset,
                r.X,
                r.Y,
                r.Z,
                0f);
            offset += 16;
        }

        if (offset != WorldProbeUboSizeBytes)
        {
            throw new InvalidOperationException($"World-probe UBO packing size mismatch: wrote {offset} bytes, expected {WorldProbeUboSizeBytes}.");
        }

        WorldProbeUbo.UploadOrResize(worldProbeBytes, WorldProbeUboSizeBytes, growExponentially: false);
    }

    /// <summary>Retires effect and clipmap storage when their lighting owner is disposed.</summary>
    public void Dispose()
    {
        frameUbo?.Dispose();
        worldProbeUbo?.Dispose();
        frameUbo = null;
        worldProbeUbo = null;
    }

    /// <summary>Packs one std140 floating-point vector in little-endian order.</summary>
    private static void WriteVec4(byte[] dst, int byteOffset, float x, float y, float z, float w)
    {
        if (!BitConverter.IsLittleEndian)
        {
            WriteFloat(dst, byteOffset + 0, x);
            WriteFloat(dst, byteOffset + 4, y);
            WriteFloat(dst, byteOffset + 8, z);
            WriteFloat(dst, byteOffset + 12, w);
            return;
        }

        Span<byte> b = dst.AsSpan(byteOffset, 16);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(12, 4), w);
    }

    /// <summary>Packs one std140 integer vector without floating-point conversion.</summary>
    private static void WriteIvec4(byte[] dst, int byteOffset, int x, int y, int z, int w)
    {
        Span<byte> b = dst.AsSpan(byteOffset, 16);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(12, 4), w);
    }

    /// <summary>Packs a scalar for vector components on alternate host byte orders.</summary>
    private static void WriteFloat(byte[] dst, int byteOffset, float v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(dst.AsSpan(byteOffset, 4), v);
    }

}
