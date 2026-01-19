using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal static class TextureStreamingSystem
{
    private static readonly object Gate = new();
    private static TextureStreamingManager? manager;

    public static TextureStreamingManager Manager
    {
        get
        {
            lock (Gate)
            {
                manager ??= new TextureStreamingManager();
                return manager;
            }
        }
    }

    public static void Configure(TextureStreamingSettings settings)
    {
        Manager.UpdateSettings(settings);
    }

    public static TextureStreamingDiagnostics GetDiagnosticsSnapshot()
    {
        return Manager.GetDiagnosticsSnapshot();
    }

    public static TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<byte> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        return Manager.StageCopy(
            textureId,
            target,
            region,
            pixelFormat,
            pixelType,
            data,
            priority,
            unpackAlignment,
            unpackRowLength,
            unpackImageHeight);
    }

    public static TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<ushort> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        return Manager.StageCopy(
            textureId,
            target,
            region,
            pixelFormat,
            pixelType,
            data,
            priority,
            unpackAlignment,
            unpackRowLength,
            unpackImageHeight);
    }

    public static TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<Half> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        return Manager.StageCopy(
            textureId,
            target,
            region,
            pixelFormat,
            pixelType,
            data,
            priority,
            unpackAlignment,
            unpackRowLength,
            unpackImageHeight);
    }

    public static TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<float> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        return Manager.StageCopy(
            textureId,
            target,
            region,
            pixelFormat,
            pixelType,
            data,
            priority,
            unpackAlignment,
            unpackRowLength,
            unpackImageHeight);
    }

    public static void TickOnRenderThread()
    {
        Manager.TickOnRenderThread();
    }

    public static void Dispose()
    {
        lock (Gate)
        {
            manager?.Dispose();
            manager = null;
        }
    }
}
