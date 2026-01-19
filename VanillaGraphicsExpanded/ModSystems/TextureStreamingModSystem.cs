using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class TextureStreamingModSystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? capi;
    private TextureStreamingManagerRenderer? renderer;
    private bool rendererRegistered;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        renderer ??= new TextureStreamingManagerRenderer();
        api.Event.RegisterRenderer(renderer, EnumRenderStage.Before, "vge_texture_streaming");
        rendererRegistered = true;

        ConfigModSystem.Config.Sanitize();
        TextureStreamingSystem.Configure(BuildTextureStreamingSettings(ConfigModSystem.Config));
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI)
        {
            return;
        }

        ConfigModSystem.Config.Sanitize();
        TextureStreamingSystem.Configure(BuildTextureStreamingSettings(ConfigModSystem.Config));
    }

    public override void Dispose()
    {
        base.Dispose();

        if (capi != null && rendererRegistered && renderer is not null)
        {
            try
            {
                capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Before);
            }
            catch
            {
                // Best-effort.
            }
        }

        renderer?.Dispose();
        renderer = null;
        rendererRegistered = false;

        TextureStreamingSystem.Dispose();

        capi = null;
    }

    private static TextureStreamingSettings BuildTextureStreamingSettings(LumOnConfig cfg)
    {
        return new TextureStreamingSettings(
            EnablePboStreaming: cfg.TextureStreamingEnabled,
            AllowDirectUploads: cfg.TextureStreamingAllowDirectUploads,
            ForceDisablePersistent: cfg.TextureStreamingForceDisablePersistent,
            UseCoherentMapping: cfg.TextureStreamingUseCoherentMapping,
            MaxUploadsPerFrame: cfg.TextureStreamingMaxUploadsPerFrame,
            MaxBytesPerFrame: cfg.TextureStreamingMaxBytesPerFrame,
            MaxStagingBytes: cfg.TextureStreamingMaxStagingBytes,
            PersistentRingBytes: cfg.TextureStreamingPersistentRingBytes,
            TripleBufferBytes: cfg.TextureStreamingTripleBufferBytes,
            PboAlignment: cfg.TextureStreamingPboAlignment);
    }
}
