using System;
using System.Buffers.Binary;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

internal readonly struct LumOnFrameUboData
{
    public readonly float[] InvProjectionMatrix;
    public readonly float[] ProjectionMatrix;
    public readonly float[] ViewMatrix;
    public readonly float[] InvViewMatrix;
    public readonly float[] PrevViewProjMatrix;
    public readonly float[] InvCurrViewProjMatrix;

    public readonly Vec2f ScreenSize;
    public readonly Vec2f HalfResSize;
    public readonly Vec2f ProbeGridSize;
    public readonly float ZNear;
    public readonly float ZFar;

    public readonly int ProbeSpacing;
    public readonly int FrameIndex;
    public readonly int HistoryValid;
    public readonly int AnchorJitterEnabled;

    public readonly int PmjCycleLength;
    public readonly int EnableVelocityReprojection;

    public readonly float AnchorJitterScale;
    public readonly float VelocityRejectThreshold;

    public readonly Vec3f SunPosition;
    public readonly Vec3f SunColor;
    public readonly Vec3f AmbientColor;

    public LumOnFrameUboData(
        float[] invProjectionMatrix,
        float[] projectionMatrix,
        float[] viewMatrix,
        float[] invViewMatrix,
        float[] prevViewProjMatrix,
        float[] invCurrViewProjMatrix,
        Vec2f screenSize,
        Vec2f halfResSize,
        Vec2f probeGridSize,
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
        InvProjectionMatrix = invProjectionMatrix;
        ProjectionMatrix = projectionMatrix;
        ViewMatrix = viewMatrix;
        InvViewMatrix = invViewMatrix;
        PrevViewProjMatrix = prevViewProjMatrix;
        InvCurrViewProjMatrix = invCurrViewProjMatrix;

        ScreenSize = screenSize;
        HalfResSize = halfResSize;
        ProbeGridSize = probeGridSize;
        ZNear = zNear;
        ZFar = zFar;

        ProbeSpacing = probeSpacing;
        FrameIndex = frameIndex;
        HistoryValid = historyValid;
        AnchorJitterEnabled = anchorJitterEnabled;

        PmjCycleLength = pmjCycleLength;
        EnableVelocityReprojection = enableVelocityReprojection;

        AnchorJitterScale = anchorJitterScale;
        VelocityRejectThreshold = velocityRejectThreshold;

        SunPosition = sunPosition;
        SunColor = sunColor;
        AmbientColor = ambientColor;
    }
}

internal readonly struct LumOnWorldProbeUboData
{
    public readonly Vec3f SkyTint;
    public readonly Vec3f CameraPosWS;
    public readonly Vec3f[]? OriginMinCorner;
    public readonly Vec3f[]? RingOffset;

    public LumOnWorldProbeUboData(
        Vec3f skyTint,
        Vec3f cameraPosWS,
        Vec3f[]? originMinCorner,
        Vec3f[]? ringOffset)
    {
        SkyTint = skyTint;
        CameraPosWS = cameraPosWS;
        OriginMinCorner = originMinCorner;
        RingOffset = ringOffset;
    }
}

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

    public void UpdateFrame(in LumOnFrameUboData data)
    {
        EnsureCreated();

        ValidateMat4(data.InvProjectionMatrix, nameof(data.InvProjectionMatrix));
        ValidateMat4(data.ProjectionMatrix, nameof(data.ProjectionMatrix));
        ValidateMat4(data.ViewMatrix, nameof(data.ViewMatrix));
        ValidateMat4(data.InvViewMatrix, nameof(data.InvViewMatrix));
        ValidateMat4(data.PrevViewProjMatrix, nameof(data.PrevViewProjMatrix));
        ValidateMat4(data.InvCurrViewProjMatrix, nameof(data.InvCurrViewProjMatrix));

        int offset = 0;

        WriteMat4(frameBytes, offset, data.InvProjectionMatrix); offset += 64;
        WriteMat4(frameBytes, offset, data.ProjectionMatrix); offset += 64;
        WriteMat4(frameBytes, offset, data.ViewMatrix); offset += 64;
        WriteMat4(frameBytes, offset, data.InvViewMatrix); offset += 64;
        WriteMat4(frameBytes, offset, data.PrevViewProjMatrix); offset += 64;
        WriteMat4(frameBytes, offset, data.InvCurrViewProjMatrix); offset += 64;

        WriteVec4(frameBytes, offset, data.ScreenSize.X, data.ScreenSize.Y, data.HalfResSize.X, data.HalfResSize.Y); offset += 16;
        WriteVec4(frameBytes, offset, data.ProbeGridSize.X, data.ProbeGridSize.Y, data.ZNear, data.ZFar); offset += 16;

        WriteIvec4(frameBytes, offset, data.ProbeSpacing, data.FrameIndex, data.HistoryValid, data.AnchorJitterEnabled); offset += 16;
        WriteIvec4(frameBytes, offset, data.PmjCycleLength, data.EnableVelocityReprojection, 0, 0); offset += 16;

        WriteVec4(frameBytes, offset, data.AnchorJitterScale, data.VelocityRejectThreshold, 0f, 0f); offset += 16;

        WriteVec4(frameBytes, offset, data.SunPosition.X, data.SunPosition.Y, data.SunPosition.Z, 0f); offset += 16;
        WriteVec4(frameBytes, offset, data.SunColor.X, data.SunColor.Y, data.SunColor.Z, 0f); offset += 16;
        WriteVec4(frameBytes, offset, data.AmbientColor.X, data.AmbientColor.Y, data.AmbientColor.Z, 0f); offset += 16;

        if (offset != FrameUboSizeBytes)
        {
            throw new InvalidOperationException($"Frame UBO packing size mismatch: wrote {offset} bytes, expected {FrameUboSizeBytes}.");
        }

        FrameUbo.UploadOrResize(frameBytes, FrameUboSizeBytes, growExponentially: false);
    }

    public void UpdateWorldProbe(in LumOnWorldProbeUboData data)
    {
        EnsureWorldProbeCreated();

        int offset = 0;

        WriteVec4(worldProbeBytes, offset, data.SkyTint.X, data.SkyTint.Y, data.SkyTint.Z, 0f); offset += 16;
        WriteVec4(worldProbeBytes, offset, data.CameraPosWS.X, data.CameraPosWS.Y, data.CameraPosWS.Z, 0f); offset += 16;

        var origins = data.OriginMinCorner ?? Array.Empty<Vec3f>();
        var rings = data.RingOffset ?? Array.Empty<Vec3f>();

        for (int i = 0; i < WorldProbeMaxLevels; i++)
        {
            var o = (i < origins.Length) ? origins[i] : null;
            WriteVec4(
                worldProbeBytes,
                offset,
                o?.X ?? 0f,
                o?.Y ?? 0f,
                o?.Z ?? 0f,
                0f);
            offset += 16;
        }

        for (int i = 0; i < WorldProbeMaxLevels; i++)
        {
            var r = (i < rings.Length) ? rings[i] : null;
            WriteVec4(
                worldProbeBytes,
                offset,
                r?.X ?? 0f,
                r?.Y ?? 0f,
                r?.Z ?? 0f,
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

    private static void ValidateMat4(float[] m, string paramName)
    {
        ArgumentNullException.ThrowIfNull(m, paramName);
        if (m.Length < 16)
        {
            throw new ArgumentException("Expected at least 16 elements.", paramName);
        }
    }

    private static void WriteMat4(byte[] dst, int byteOffset, float[] m)
    {
        for (int i = 0; i < 16; i++)
        {
            WriteFloat(dst, byteOffset + i * 4, m[i]);
        }
    }

    private static void WriteVec4(byte[] dst, int byteOffset, float x, float y, float z, float w)
    {
        WriteFloat(dst, byteOffset + 0, x);
        WriteFloat(dst, byteOffset + 4, y);
        WriteFloat(dst, byteOffset + 8, z);
        WriteFloat(dst, byteOffset + 12, w);
    }

    private static void WriteIvec4(byte[] dst, int byteOffset, int x, int y, int z, int w)
    {
        WriteInt(dst, byteOffset + 0, x);
        WriteInt(dst, byteOffset + 4, y);
        WriteInt(dst, byteOffset + 8, z);
        WriteInt(dst, byteOffset + 12, w);
    }

    private static void WriteFloat(byte[] dst, int byteOffset, float v)
    {
        BinaryPrimitives.WriteInt32LittleEndian(dst.AsSpan(byteOffset, 4), BitConverter.SingleToInt32Bits(v));
    }

    private static void WriteInt(byte[] dst, int byteOffset, int v)
    {
        BinaryPrimitives.WriteInt32LittleEndian(dst.AsSpan(byteOffset, 4), v);
    }
}
