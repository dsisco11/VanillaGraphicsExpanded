using System;
using System.Threading.Tasks;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

internal sealed class TextureStreamingStageCopyArtifactGpuStore<TKey> : IArtifactGpuStore<TKey, ArtifactStageCopyUploadPayload>
{
    public ValueTask StoreAsync(ArtifactSession session, TKey key, in ArtifactStageCopyUploadPayload payload)
    {
        if (session.CancellationToken.IsCancellationRequested)
        {
            return ValueTask.CompletedTask;
        }

        TextureStageResult result = payload.Data.Kind switch
        {
            TextureUploadDataKind.Bytes => TextureStreamingSystem.StageCopy(
                payload.TextureId,
                payload.Target,
                payload.Region,
                payload.PixelFormat,
                payload.PixelType,
                ((byte[])payload.Data.DataArray).AsSpan(),
                payload.Priority,
                payload.UnpackAlignment,
                payload.UnpackRowLength,
                payload.UnpackImageHeight),

            TextureUploadDataKind.UShorts => TextureStreamingSystem.StageCopy(
                payload.TextureId,
                payload.Target,
                payload.Region,
                payload.PixelFormat,
                payload.PixelType,
                ((ushort[])payload.Data.DataArray).AsSpan(),
                payload.Priority,
                payload.UnpackAlignment,
                payload.UnpackRowLength,
                payload.UnpackImageHeight),

            TextureUploadDataKind.Halfs => TextureStreamingSystem.StageCopy(
                payload.TextureId,
                payload.Target,
                payload.Region,
                payload.PixelFormat,
                payload.PixelType,
                ((Half[])payload.Data.DataArray).AsSpan(),
                payload.Priority,
                payload.UnpackAlignment,
                payload.UnpackRowLength,
                payload.UnpackImageHeight),

            TextureUploadDataKind.Floats => TextureStreamingSystem.StageCopy(
                payload.TextureId,
                payload.Target,
                payload.Region,
                payload.PixelFormat,
                payload.PixelType,
                ((float[])payload.Data.DataArray).AsSpan(),
                payload.Priority,
                payload.UnpackAlignment,
                payload.UnpackRowLength,
                payload.UnpackImageHeight),

            _ => throw new InvalidOperationException("Unsupported upload data kind."),
        };

        if (result.Outcome == TextureStageOutcome.Rejected)
        {
            if (result.RejectReason == TextureStageRejectReason.ManagerDisposed && session.CancellationToken.IsCancellationRequested)
            {
                return ValueTask.CompletedTask;
            }

            throw new InvalidOperationException($"StageCopy rejected: {result.RejectReason}");
        }

        _ = result;
        return ValueTask.CompletedTask;
    }
}
