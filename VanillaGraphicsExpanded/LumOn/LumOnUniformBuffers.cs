using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Publishes LumOn per-frame and world-probe shared state via Uniform Buffer Objects (UBOs).
/// </summary>
internal sealed class LumOnUniformBuffers : IDisposable
{
    public const int FrameBinding = 12;
    public const int WorldProbeBinding = 13;

    private const int WorldProbeMaxLevels = 8;

    private const int FrameUboSizeBytes = 512;
    private const int WorldProbeUboSizeBytes = 288;

    private readonly byte[] frameBytes = new byte[FrameUboSizeBytes];
    private readonly byte[] worldProbeBytes = new byte[WorldProbeUboSizeBytes];

    private GpuUniformBuffer? frameUbo;
    private GpuUniformBuffer? worldProbeUbo;

    public GpuUniformBuffer FrameUbo => frameUbo ?? throw new InvalidOperationException("Frame UBO is not created.");

    public GpuUniformBuffer WorldProbeUbo => worldProbeUbo ?? throw new InvalidOperationException("World-probe UBO is not created.");

    public bool HasWorldProbeUbo => worldProbeUbo is not null && worldProbeUbo.BufferId != 0;

    public GpuUniformBuffer? WorldProbeUboOrNull => HasWorldProbeUbo ? worldProbeUbo : null;

    public void EnsureCreated()
    {
        if (frameUbo is null || frameUbo.BufferId == 0)
        {
            frameUbo?.Dispose();
            frameUbo = GpuUniformBuffer.Create(debugName: "LumOn.FrameUBO");
        }
    }

    public void EnsureWorldProbeCreated()
    {
        if (worldProbeUbo is null || worldProbeUbo.BufferId == 0)
        {
            worldProbeUbo?.Dispose();
            worldProbeUbo = GpuUniformBuffer.Create(debugName: "LumOn.WorldProbeUBO");
        }
    }

    public void UpdateFrame(
        ReadOnlySpan<float> invProjectionMatrix,
        ReadOnlySpan<float> projectionMatrix,
        ReadOnlySpan<float> viewMatrix,
        ReadOnlySpan<float> invViewMatrix,
        ReadOnlySpan<float> prevViewProjMatrix,
        ReadOnlySpan<float> invCurrViewProjMatrix,
        float screenWidth,
        float screenHeight,
        float halfResWidth,
        float halfResHeight,
        float probeGridWidth,
        float probeGridHeight,
        float zNear,
        float zFar,
        int probeSpacing,
        int frameIndex,
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

        ValidateMat4(invProjectionMatrix, nameof(invProjectionMatrix));
        ValidateMat4(projectionMatrix, nameof(projectionMatrix));
        ValidateMat4(viewMatrix, nameof(viewMatrix));
        ValidateMat4(invViewMatrix, nameof(invViewMatrix));
        ValidateMat4(prevViewProjMatrix, nameof(prevViewProjMatrix));
        ValidateMat4(invCurrViewProjMatrix, nameof(invCurrViewProjMatrix));

        int offset = 0;

        WriteMat4(frameBytes, offset, invProjectionMatrix); offset += 64;
        WriteMat4(frameBytes, offset, projectionMatrix); offset += 64;
        WriteMat4(frameBytes, offset, viewMatrix); offset += 64;
        WriteMat4(frameBytes, offset, invViewMatrix); offset += 64;
        WriteMat4(frameBytes, offset, prevViewProjMatrix); offset += 64;
        WriteMat4(frameBytes, offset, invCurrViewProjMatrix); offset += 64;

        WriteVec4(frameBytes, offset, screenWidth, screenHeight, halfResWidth, halfResHeight); offset += 16;
        WriteVec4(frameBytes, offset, probeGridWidth, probeGridHeight, zNear, zFar); offset += 16;

        WriteIvec4(frameBytes, offset, probeSpacing, frameIndex, historyValid, anchorJitterEnabled); offset += 16;
        WriteIvec4(frameBytes, offset, pmjCycleLength, enableVelocityReprojection, 0, 0); offset += 16;

        WriteVec4(frameBytes, offset, anchorJitterScale, velocityRejectThreshold, 0f, 0f); offset += 16;

        WriteVec4(frameBytes, offset, sunPosition.X, sunPosition.Y, sunPosition.Z, 0f); offset += 16;
        WriteVec4(frameBytes, offset, sunColor.X, sunColor.Y, sunColor.Z, 0f); offset += 16;
        WriteVec4(frameBytes, offset, ambientColor.X, ambientColor.Y, ambientColor.Z, 0f); offset += 16;

        if (offset != FrameUboSizeBytes)
        {
            throw new InvalidOperationException($"Frame UBO packing size mismatch: wrote {offset} bytes, expected {FrameUboSizeBytes}.");
        }

        FrameUbo.UploadOrResize(frameBytes, FrameUboSizeBytes, growExponentially: false);
    }

    public void UpdateWorldProbe(
        Vec3f skyTint,
        Vector3 cameraPosWS,
        ReadOnlySpan<Vector3> originMinCorner,
        ReadOnlySpan<Vector3> ringOffset)
    {
        EnsureWorldProbeCreated();

        int offset = 0;

        WriteVec4(worldProbeBytes, offset, skyTint.X, skyTint.Y, skyTint.Z, 0f); offset += 16;
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

    public void Dispose()
    {
        frameUbo?.Dispose();
        worldProbeUbo?.Dispose();
        frameUbo = null;
        worldProbeUbo = null;
    }

    private static void ValidateMat4(ReadOnlySpan<float> m, string paramName)
    {
        if (m.Length < 16)
        {
            throw new ArgumentException("Expected at least 16 elements.", paramName);
        }
    }

    private static void WriteMat4(byte[] dst, int byteOffset, ReadOnlySpan<float> m)
    {
        if (m.Length < 16)
        {
            throw new ArgumentException("Expected at least 16 elements.", nameof(m));
        }

        if (!BitConverter.IsLittleEndian)
        {
            for (int i = 0; i < 16; i++)
            {
                WriteFloat(dst, byteOffset + i * 4, m[i]);
            }
            return;
        }

        MemoryMarshal.AsBytes(m.Slice(0, 16)).CopyTo(dst.AsSpan(byteOffset, 64));
    }

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

    private static void WriteIvec4(byte[] dst, int byteOffset, int x, int y, int z, int w)
    {
        Span<byte> b = dst.AsSpan(byteOffset, 16);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(12, 4), w);
    }

    private static void WriteFloat(byte[] dst, int byteOffset, float v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(dst.AsSpan(byteOffset, 4), v);
    }

    private static void WriteInt(byte[] dst, int byteOffset, int v)
    {
        BinaryPrimitives.WriteInt32LittleEndian(dst.AsSpan(byteOffset, 4), v);
    }
}
