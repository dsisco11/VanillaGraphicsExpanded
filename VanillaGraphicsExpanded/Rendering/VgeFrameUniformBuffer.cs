using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns the universal std140 camera snapshot, shared by shaders independently of lighting mode.</summary>
internal sealed class VgeFrameUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Size of seven column-major matrices, frame scalars and the precise render-origin bridge.</summary>
    internal const int PackedSize = 544;

    /// <summary>Creates retained CPU packing with versioned publication through the existing uniform allocator.</summary>
    internal VgeFrameUniformBuffer() : base(PackedSize) { }

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

        WriteMatrix4(0, projection);
        WriteMatrix4(64, view);
        WriteMatrix4(128, inverseProjection);
        WriteMatrix4(192, inverseView);
        WriteMatrix4(256, previousViewProjection);
        WriteMatrix4(320, currentViewProjection);
        WriteVector2(384, screenSize);
        WriteFloat(392, timeSeconds);
        WriteUInt32(396, frameIndex);
        WriteVector4(400, new Vector4(cameraPosition, fogMinimum));
        WriteVector4(416, new Vector4(fogColor, fogDensity));
        WriteMatrix4(432, inverseViewProjection);
        WriteVector2(496, clipPlanes);
        WriteFloat(504, deltaTime);
        WriteUInt32(508, cameraCut ? 1u : 0u);
        WriteIntVector4(512, renderOriginChunkCoord.X, renderOriginChunkCoord.Y, renderOriginChunkCoord.Z, 0);
        WriteVector4(528, new Vector4(renderOriginBlockRemainder, 0));
    }
    /// <summary>Indicates that temporal consumers must reject the previous camera generation.</summary>
    internal bool CameraCut => (MemoryMarshal.Read<uint>(Bytes.Slice(508, 4)) & 1u) != 0;
    /// <summary>Exposes the retained projection for CPU consumers without recapturing the engine camera.</summary>
    internal ReadOnlySpan<float> Projection => MemoryMarshal.Cast<byte, float>(Bytes.Slice(0, 64));
    /// <summary>Exposes the exact shared view transform for CPU consumers.</summary>
    internal ReadOnlySpan<float> View => MemoryMarshal.Cast<byte, float>(Bytes.Slice(64, 64));
    /// <summary>Exposes the shared inverse view for origin-sensitive CPU consumers.</summary>
    internal ReadOnlySpan<float> InverseView => MemoryMarshal.Cast<byte, float>(Bytes.Slice(192, 64));
    /// <summary>Exposes current clip coordinates without repeating matrix composition.</summary>
    internal ReadOnlySpan<float> CurrentViewProjection => MemoryMarshal.Cast<byte, float>(Bytes.Slice(320, 64));
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
