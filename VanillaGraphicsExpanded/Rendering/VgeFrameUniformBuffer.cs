using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns the universal std140 camera snapshot, shared by shaders independently of lighting mode.</summary>
internal sealed class VgeFrameUniformBuffer : CpuUniformBuffer
{
    #region Public API
    #region Layout
    internal const int ProjectionOffset = 0;
    internal const int ViewOffset = 64;
    internal const int InverseProjectionOffset = 128;
    internal const int InverseViewOffset = 192;
    internal const int PreviousViewProjectionOffset = 256;
    internal const int CurrentViewProjectionOffset = 320;
    internal const int ScreenSizeOffset = 384;
    internal const int TimeSecondsOffset = 392;
    internal const int FrameIndexOffset = 396;
    internal const int CameraPositionOffset = 400;
    internal const int FogColorOffset = 416;
    internal const int InverseViewProjectionOffset = 432;
    internal const int ClipPlanesOffset = 496;
    internal const int DeltaTimeOffset = 504;
    internal const int FrameFlagsOffset = 508;
    internal const int RenderOriginChunkOffset = 512;
    internal const int RenderOriginRemainderOffset = 528;
    internal const int LightingCalibrationOffset = 544;
    internal const int SkyCalibrationOffset = 560;
    internal const int AccessorBlockCalibrationOffset = 576;
    internal const int CalibrationGenerationOffset = 584;
    internal const int MatrixSizeBytes = 16 * sizeof(float);
    #endregion

    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => PackedSize;

    /// <summary>Size of seven column-major matrices, frame scalars, render-origin bridge and source calibration.</summary>
    internal const int PackedSize = 592;

    /// <summary>Identifies the calibration retained by this complete CPU publication.</summary>
    internal PBR.LightingCalibration LightingCalibration { get; private set; } = PBR.LightingCalibration.Default;
    /// <summary>Identifies source interpretation changes independently of camera and light-list revisions.</summary>
    internal uint CalibrationGeneration { get; private set; }

    /// <summary>Creates retained CPU packing with versioned publication through the existing uniform allocator.</summary>
    internal VgeFrameUniformBuffer() : base() { SetLightingCalibration(PBR.LightingCalibration.Default); }

    /// <summary>Publishes source calibration through the existing shared frame binding without changing engine fields.</summary>
    internal void SetLightingCalibration(PBR.LightingCalibration calibration)
    {
        calibration.Validate();
        uint generation = CalibrationGeneration;
        if (generation == 0 || LightingCalibration != calibration)
        {
            // Keep zero reserved for unavailable publication and preserve the exact integer
            // identity in the existing std140 padding, rather than converting it to a float.
            unchecked { generation++; }
            if (generation == 0) generation = 1;
        }
        WriteVector4(LightingCalibrationOffset, new(calibration.BlockReferenceLux, calibration.BlockNeutralFloor,
            PBR.LightingCalibration.EmissionReferenceNits, calibration.PointCandelaPerUnit));
        WriteVector4(SkyCalibrationOffset, new(calibration.TerrainSkyFloor, calibration.TerrainSkyFull,
            calibration.AccessorSkyFloor, calibration.AccessorSkyFull));
        WriteVector2(AccessorBlockCalibrationOffset, new(calibration.AccessorBlockReferenceLux > 0 ? calibration.AccessorBlockReferenceLux : calibration.BlockReferenceLux,
            calibration.AccessorBlockReferenceLux > 0 ? calibration.AccessorBlockNeutralFloor : calibration.BlockNeutralFloor));
        WriteUInt32(CalibrationGenerationOffset, generation);
        LightingCalibration = calibration;
        CalibrationGeneration = generation;
    }

    /// <summary>Captures one view's complete frame data without allocating GPU storage or publishing partial writes.</summary>
    internal void Capture(ReadOnlySpan<float> projection, ReadOnlySpan<float> view,
        ReadOnlySpan<float> inverseProjection, ReadOnlySpan<float> inverseView,
        ReadOnlySpan<float> previousViewProjection, ReadOnlySpan<float> currentViewProjection,
        Vector2 screenSize, float timeSeconds, uint frameIndex, Vector3 cameraPosition,
        Vector3 fogColor, float fogDensity, Vector2 clipPlanes = default,
        VectorInt3 renderOriginChunkCoord = default, Vector3 renderOriginBlockRemainder = default,
        float deltaTime = 0, bool cameraCut = false, float fogMinimum = 0)
    {
        // Validate every input before changing the snapshot so malformed alternate views cannot
        // leave a mixture of camera generations available for subsequent submission.
        ValidateMatrix(projection, nameof(projection));
        ValidateMatrix(view, nameof(view));
        ValidateMatrix(inverseProjection, nameof(inverseProjection));
        ValidateMatrix(inverseView, nameof(inverseView));
        ValidateMatrix(previousViewProjection, nameof(previousViewProjection));
        ValidateMatrix(currentViewProjection, nameof(currentViewProjection));
        if (!float.IsFinite(screenSize.X) || !float.IsFinite(screenSize.Y) || screenSize.X <= 0 || screenSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenSize));
        if (!float.IsFinite(timeSeconds)) throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        if (!float.IsFinite(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        ValidateVector(cameraPosition, nameof(cameraPosition));
        ValidateVector(fogColor, nameof(fogColor));
        if (!float.IsFinite(fogMinimum)) throw new ArgumentOutOfRangeException(nameof(fogMinimum));
        ValidateVector(renderOriginBlockRemainder, nameof(renderOriginBlockRemainder));
        if (!float.IsFinite(fogDensity) || fogDensity < 0) throw new ArgumentOutOfRangeException(nameof(fogDensity));
        if (!float.IsFinite(clipPlanes.X) || !float.IsFinite(clipPlanes.Y))
            throw new ArgumentOutOfRangeException(nameof(clipPlanes));
        Span<float> inverseViewProjection = stackalloc float[16];
        MatrixHelper.Multiply(inverseView, inverseProjection, inverseViewProjection);
        ValidateMatrix(inverseViewProjection, nameof(inverseViewProjection));

        WriteMatrix4(ProjectionOffset, projection);
        WriteMatrix4(ViewOffset, view);
        WriteMatrix4(InverseProjectionOffset, inverseProjection);
        WriteMatrix4(InverseViewOffset, inverseView);
        WriteMatrix4(PreviousViewProjectionOffset, previousViewProjection);
        WriteMatrix4(CurrentViewProjectionOffset, currentViewProjection);
        WriteVector2(ScreenSizeOffset, screenSize);
        WriteFloat(TimeSecondsOffset, timeSeconds);
        WriteUInt32(FrameIndexOffset, frameIndex);
        WriteVector4(CameraPositionOffset, new Vector4(cameraPosition, fogMinimum));
        WriteVector4(FogColorOffset, new Vector4(fogColor, fogDensity));
        WriteMatrix4(InverseViewProjectionOffset, inverseViewProjection);
        WriteVector2(ClipPlanesOffset, clipPlanes);
        WriteFloat(DeltaTimeOffset, deltaTime);
        WriteUInt32(FrameFlagsOffset, cameraCut ? 1u : 0u);
        WriteIntVector4(RenderOriginChunkOffset, renderOriginChunkCoord.X, renderOriginChunkCoord.Y, renderOriginChunkCoord.Z, 0);
        WriteVector4(RenderOriginRemainderOffset, new Vector4(renderOriginBlockRemainder, 0));
    }
    /// <summary>Indicates that temporal consumers must reject the previous camera generation.</summary>
    internal bool CameraCut => (MemoryMarshal.Read<uint>(Bytes.Slice(FrameFlagsOffset, 4)) & 1u) != 0;
    /// <summary>Identifies the exact captured world frame for frozen-lighting publication and diagnostics.</summary>
    internal uint FrameIndex => MemoryMarshal.Read<uint>(Bytes.Slice(FrameIndexOffset, 4));
    /// <summary>Exposes the frozen pixel extent used by this camera publication.</summary>
    internal Vector2 ScreenSize => MemoryMarshal.Read<Vector2>(Bytes.Slice(ScreenSizeOffset, 2 * sizeof(float)));
    /// <summary>Exposes frozen fog color and density without exporting byte-layout knowledge.</summary>
    internal Vector4 FogParameters => MemoryMarshal.Read<Vector4>(Bytes.Slice(FogColorOffset, 4 * sizeof(float)));
    /// <summary>Exposes the retained projection for CPU consumers without recapturing the engine camera.</summary>
    internal ReadOnlySpan<float> Projection => MemoryMarshal.Cast<byte, float>(Bytes.Slice(ProjectionOffset, MatrixSizeBytes));
    /// <summary>Exposes the near/far distances from the same camera snapshot as the projection.</summary>
    internal Vector2 ClipPlanes => MemoryMarshal.Read<Vector2>(Bytes.Slice(ClipPlanesOffset, 8));
    /// <summary>Exposes the exact shared view transform for CPU consumers.</summary>
    internal ReadOnlySpan<float> View => MemoryMarshal.Cast<byte, float>(Bytes.Slice(ViewOffset, MatrixSizeBytes));
    /// <summary>Returns the exact integer world partition selected by this frozen camera publication.</summary>
    internal VectorInt3 RenderOriginChunk
    {
        get
        {
            // The wire slot is an ivec4; the host vector's SIMD storage is not a three-int wire layout.
            var (x, y, z, _) = UboPacking.ReadIVec4(Bytes, RenderOriginChunkOffset);
            return new(x, y, z);
        }
    }
    /// <summary>Returns the camera's bounded within-partition origin component.</summary>
    internal Vector3 RenderOriginRemainder => MemoryMarshal.Read<Vector3>(Bytes.Slice(RenderOriginRemainderOffset, 12));
    /// <summary>Exposes the shared inverse view for origin-sensitive CPU consumers.</summary>
    internal ReadOnlySpan<float> InverseView => MemoryMarshal.Cast<byte, float>(Bytes.Slice(InverseViewOffset, MatrixSizeBytes));
    /// <summary>Exposes current clip coordinates without repeating matrix composition.</summary>
    internal ReadOnlySpan<float> CurrentViewProjection => MemoryMarshal.Cast<byte, float>(Bytes.Slice(CurrentViewProjectionOffset, MatrixSizeBytes));
    /// <summary>Projects current render-relative positions into the preceding shared camera, including precise origin rebasing.</summary>
    internal ReadOnlySpan<float> PreviousViewProjection => MemoryMarshal.Cast<byte, float>(Bytes.Slice(PreviousViewProjectionOffset, MatrixSizeBytes));
    #endregion

    #region Private
    /// <summary>Checks a complete finite column-major transform before any shared snapshot mutation.</summary>
    private static void ValidateMatrix(ReadOnlySpan<float> matrix, string name)
    {
        if (matrix.Length != 16) throw new ArgumentException("Expected sixteen column-major matrix elements.", name);
        foreach (float value in matrix)
            if (!float.IsFinite(value)) throw new ArgumentException("Camera transforms must be finite.", name);
    }

    /// <summary>Rejects non-finite camera or fog components before changing any packed bytes.</summary>
    private static void ValidateVector(Vector3 vector, string name)
    {
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new ArgumentException("Frame vectors must be finite.", name);
    }
    #endregion
}
