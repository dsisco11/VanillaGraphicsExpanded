using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Native;

/// <summary>Constructs an owned universal camera snapshot matching controlled shader receiver geometry.</summary>
internal static class TestFrameCamera
{
    #region Public API
    #region Captured frame inputs
    /// <summary>Recaptures a complete exported frame through the owner's validation while preserving every AO camera field.</summary>
    internal static VgeFrameUniformBuffer CreateFromPacked(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != VgeFrameUniformBuffer.LightingCalibrationOffset
            && bytes.Length != VgeFrameUniformBuffer.SkyCalibrationOffset
            && bytes.Length != VgeFrameUniformBuffer.AccessorBlockCalibrationOffset
            && bytes.Length != VgeFrameUniformBuffer.PackedSize)
            throw new ArgumentException("Captured camera prefix must match a supported frame layout.", nameof(bytes));
        var values = MemoryMarshal.Cast<byte, float>(bytes);
        var origin = UboPacking.ReadIVec4(bytes, VgeFrameUniformBuffer.RenderOriginChunkOffset);
        int matrixFloatCount = VgeFrameUniformBuffer.MatrixSizeBytes / sizeof(float);
        var camera = new VgeFrameUniformBuffer();
        // Decode typed scalars instead of mutating protected storage. Capture recomputes only
        // the inverse view-projection matrix; AO borrows the exact view and inverse projection.
        camera.Capture(values.Slice(FloatIndex(VgeFrameUniformBuffer.ProjectionOffset), matrixFloatCount),
            values.Slice(FloatIndex(VgeFrameUniformBuffer.ViewOffset), matrixFloatCount),
            values.Slice(FloatIndex(VgeFrameUniformBuffer.InverseProjectionOffset), matrixFloatCount),
            values.Slice(FloatIndex(VgeFrameUniformBuffer.InverseViewOffset), matrixFloatCount),
            values.Slice(FloatIndex(VgeFrameUniformBuffer.PreviousViewProjectionOffset), matrixFloatCount),
            values.Slice(FloatIndex(VgeFrameUniformBuffer.CurrentViewProjectionOffset), matrixFloatCount),
            new(values[FloatIndex(VgeFrameUniformBuffer.ScreenSizeOffset)], values[FloatIndex(VgeFrameUniformBuffer.ScreenSizeOffset) + 1]),
            values[FloatIndex(VgeFrameUniformBuffer.TimeSecondsOffset)],
            UboPacking.ReadUInt32(bytes, VgeFrameUniformBuffer.FrameIndexOffset),
            new(values[FloatIndex(VgeFrameUniformBuffer.CameraPositionOffset)], values[FloatIndex(VgeFrameUniformBuffer.CameraPositionOffset) + 1],
                values[FloatIndex(VgeFrameUniformBuffer.CameraPositionOffset) + 2]),
            new(values[FloatIndex(VgeFrameUniformBuffer.FogColorOffset)], values[FloatIndex(VgeFrameUniformBuffer.FogColorOffset) + 1],
                values[FloatIndex(VgeFrameUniformBuffer.FogColorOffset) + 2]),
            values[FloatIndex(VgeFrameUniformBuffer.FogColorOffset) + 3],
            new(values[FloatIndex(VgeFrameUniformBuffer.ClipPlanesOffset)], values[FloatIndex(VgeFrameUniformBuffer.ClipPlanesOffset) + 1]),
            new VectorInt3(origin.x, origin.y, origin.z),
            new(values[FloatIndex(VgeFrameUniformBuffer.RenderOriginRemainderOffset)], values[FloatIndex(VgeFrameUniformBuffer.RenderOriginRemainderOffset) + 1],
                values[FloatIndex(VgeFrameUniformBuffer.RenderOriginRemainderOffset) + 2]),
            values[FloatIndex(VgeFrameUniformBuffer.DeltaTimeOffset)],
            (UboPacking.ReadUInt32(bytes, VgeFrameUniformBuffer.FrameFlagsOffset) & 1u) != 0,
            values[FloatIndex(VgeFrameUniformBuffer.CameraPositionOffset) + 3]);
        if (bytes.Length == VgeFrameUniformBuffer.SkyCalibrationOffset)
            camera.SetLightingCalibration(new(values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset)],
                values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset) + 1]));
        else if (bytes.Length == VgeFrameUniformBuffer.AccessorBlockCalibrationOffset)
            camera.SetLightingCalibration(new(values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset)],
                values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset) + 1],
                values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset) + 3],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset)],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset) + 1],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset) + 2],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset) + 3]));
        else if (bytes.Length == VgeFrameUniformBuffer.PackedSize)
            camera.SetLightingCalibration(new(values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset)],
                values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset) + 1],
                values[FloatIndex(VgeFrameUniformBuffer.LightingCalibrationOffset) + 3],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset)],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset) + 1],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset) + 2],
                values[FloatIndex(VgeFrameUniformBuffer.SkyCalibrationOffset) + 3],
                values[FloatIndex(VgeFrameUniformBuffer.AccessorBlockCalibrationOffset)],
                values[FloatIndex(VgeFrameUniformBuffer.AccessorBlockCalibrationOffset) + 1]));
        return camera;
    }
    #endregion

    #region Analytic camera construction
    /// <summary>Inverts the supplied transforms and captures a consistent current and previous camera generation.</summary>
    internal static VgeFrameUniformBuffer Create(float[] inverseProjection, float[] view, int width = 1, int height = 1,
        float near = .1f, float far = 100, Vector3 fogColor = default, float fogDensity = 0, float fogMinimum = 0,
        VectorInt3 renderOriginChunkCoord = default, Vector3 renderOriginBlockRemainder = default)
    {
        // Fixtures supply inverse projection because they author depth analytically; derive its matching forward matrix.
        float[] projection = new float[16], inverseView = new float[16], viewProjection = new float[16];
        Mat4f.Invert(projection, inverseProjection);
        Mat4f.Invert(inverseView, view);
        Mat4f.Mul(viewProjection, projection, view);
        var camera = new VgeFrameUniformBuffer();
        camera.Capture(projection, view, inverseProjection, inverseView, viewProjection, viewProjection,
            new Vector2(width, height), 0, 0, Vector3.Zero, fogColor, fogDensity, new(near, far),
            renderOriginChunkCoord, renderOriginBlockRemainder, fogMinimum: fogMinimum);
        return camera;
    }
    /// <summary>Provides a neutral camera for screen-space fixtures while retaining the actual output dimensions.</summary>
    internal static VgeFrameUniformBuffer CreateIdentity(int width, int height, float deltaTime = 0, uint frameIndex = 0)
    {
        float[] identity = Mat4f.Create();
        var camera = new VgeFrameUniformBuffer();
        camera.Capture(identity, identity, identity, identity, identity, identity, new(width, height),
            0, frameIndex, Vector3.Zero, Vector3.Zero, 0, deltaTime: deltaTime);
        return camera;
    }

    /// <summary>Captures a forward projection and explicit clipping range for rasterized liquid geometry.</summary>
    internal static VgeFrameUniformBuffer CreateFromProjection(float[] projection, int width, int height, float near, float far,
        Vector3 fogColor=default,float fogDensity=0,float fogMinimum=0)
    {
        float[] inverse = new float[16];
        if (Mat4f.Invert(inverse, projection) is null) throw new ArgumentException("Projection must be invertible.", nameof(projection));
        float[] identity = Mat4f.Create();
        var camera = new VgeFrameUniformBuffer();
        camera.Capture(projection, identity, inverse, identity, projection, projection, new(width, height),
            0, 0, Vector3.Zero, fogColor, fogDensity, new(near, far),fogMinimum:fogMinimum);
        return camera;
    }

    /// <summary>Preserves System.Numerics column storage when publishing an analytically authored fixture projection.</summary>
    internal static VgeFrameUniformBuffer CreateFromProjection(Matrix4x4 projection, int width, int height, float near = .1f, float far = 100)
    {
        float[] packed = [projection.M11,projection.M12,projection.M13,projection.M14,
            projection.M21,projection.M22,projection.M23,projection.M24,
            projection.M31,projection.M32,projection.M33,projection.M34,
            projection.M41,projection.M42,projection.M43,projection.M44];
        return CreateFromProjection(packed, width, height, near, far);
    }

    /// <summary>Captures the independently authored solar receiver projection without relying on shader-local camera state.</summary>
    internal static VgeFrameUniformBuffer CreateSolar(int width, int height, float[]? view = null)
    {
        float zoom = .75f / MathF.Tan(VanillaGraphicsExpanded.PBR.Atmosphere.AtmosphereSolarDisk.AngularRadius);
        float[] projection = [zoom,0,0,0, 0,zoom,0,0, 0,0,-1,-1, 0,0,-1,0];
        float[] inverse = new float[16];
        Mat4f.Invert(inverse, projection);
        return Create(inverse, view ?? Mat4f.Create(), width, height, .5f, 100);
    }

    /// <summary>Matches direct-lighting fixtures that author the inverse view transform of their receiver geometry.</summary>
    internal static VgeFrameUniformBuffer CreateFromInverseView(float[] inverseProjection, float[] inverseView,
        int width = 1, int height = 1, float near = .1f, float far = 100,
        VectorInt3 renderOriginChunkCoord = default, Vector3 renderOriginBlockRemainder = default)
    {
        float[] view = new float[16];
        Mat4f.Invert(view, inverseView);
        return Create(inverseProjection, view, width, height, near, far,
            renderOriginChunkCoord: renderOriginChunkCoord, renderOriginBlockRemainder: renderOriginBlockRemainder);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Converts an owner-declared byte offset to a float index for the captured std140 payload.</summary>
    private static int FloatIndex(int offsetBytes) => offsetBytes / sizeof(float);
    #endregion
}
