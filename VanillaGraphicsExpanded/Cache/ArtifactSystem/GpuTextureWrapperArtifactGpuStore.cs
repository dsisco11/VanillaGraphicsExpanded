using System;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

internal sealed class GpuTextureWrapperArtifactGpuStore<TKey> : IArtifactGpuStore<TKey, ArtifactGpuTextureUploadPayload>
{
    public ValueTask StoreAsync(ArtifactSession session, TKey key, in ArtifactGpuTextureUploadPayload payload)
    {
        if (session.CancellationToken.IsCancellationRequested)
        {
            return ValueTask.CompletedTask;
        }

        if (payload.Texture is null)
        {
            throw new ArgumentNullException(nameof(payload.Texture));
        }

        switch (payload.Kind)
        {
            case ArtifactGpuTextureUploadKind.Float2DFull:
                payload.Texture.UploadDataStreamed(payload.FloatData ?? throw new ArgumentNullException(nameof(payload.FloatData)), payload.Priority, payload.MipLevel);
                break;

            case ArtifactGpuTextureUploadKind.Float2DRegion:
                payload.Texture.UploadDataStreamed(
                    payload.FloatData ?? throw new ArgumentNullException(nameof(payload.FloatData)),
                    payload.X,
                    payload.Y,
                    payload.Width,
                    payload.Height,
                    payload.Priority,
                    payload.MipLevel);
                break;

            case ArtifactGpuTextureUploadKind.UShort2DFull:
                payload.Texture.UploadDataStreamed(payload.UShortData ?? throw new ArgumentNullException(nameof(payload.UShortData)), payload.Priority, payload.MipLevel);
                break;

            case ArtifactGpuTextureUploadKind.Float3DRegion:
                payload.Texture.UploadDataStreamed3D(
                    payload.FloatData ?? throw new ArgumentNullException(nameof(payload.FloatData)),
                    payload.X,
                    payload.Y,
                    payload.Z,
                    payload.Width,
                    payload.Height,
                    payload.Depth,
                    payload.Priority,
                    payload.MipLevel);
                break;

            default:
                throw new InvalidOperationException($"Unsupported upload kind: {payload.Kind}");
        }

        return ValueTask.CompletedTask;
    }
}
